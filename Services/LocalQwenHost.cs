using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Egoist.Voice.Services;

/// <summary>Owns only the Qwen child it starts. Never controls the shared translation process.</summary>
public sealed class LocalQwenHost : IDisposable
{
    private static readonly TimeSpan DefaultIdleUnloadDelay = TimeSpan.FromMinutes(5);
    public const string ModelId = "egoist-qwen3-4b";
    // A separate loopback port isolates native diagnostics from a running user session.
    private static readonly int Port = int.TryParse(Environment.GetEnvironmentVariable("EGOIST_VOICE_QWEN_TEST_PORT"), out var port)
        && port is >= 1024 and <= 65535 ? port : 47823;
    public static string Endpoint { get; } = $"http://127.0.0.1:{Port}/v1";
    public const long ModelBytes = 2_497_280_256;
    public const string ModelSha256 = "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5";
    public const string ModelRevision = "bc640142c66e1fdd12af0bd68f40445458f3869b";
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Threading.Timer _idleStopTimer;
    private readonly TimeSpan _idleUnloadDelay;
    private readonly Func<Task<bool>> _startCore;
    private Task<bool>? _start;
    private Process? _process;
    private OwnedProcessJob? _job;
    private int _activeLeases;
    private long _idleDeadlineTicks = long.MaxValue;
    private bool _disposed;
    internal static string AuthenticationToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private string _status = "Локальная Qwen выключена.";
    public string Status
    {
        get
        {
            lock (_gate)
            {
                if (_disposed) return "Локальная Qwen выключена.";
                return _start is { IsCompletedSuccessfully: true, Result: true } && _process is { HasExited: true }
                    ? "Qwen остановилась. Повторите запуск; быстрая диктовка доступна."
                    : _status;
            }
        }
        private set { lock (_gate) _status = value; }
    }

    public static string ModelPath => ResolveAssetPath(
        Environment.GetEnvironmentVariable("EGOIST_VOICE_QWEN_MODEL_PATH"), AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EgoistVoice", "TextModels", "Qwen3-4B-Q4_K_M.gguf"),
        Path.Combine("TextModels", "Qwen3-4B-Q4_K_M.gguf"));
    public static string RuntimePath => ResolveAssetPath(
        Environment.GetEnvironmentVariable("EGOIST_VOICE_QWEN_RUNTIME_PATH"), AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Egoist", "TranslationEngine", "v1", "runtime", "llama-b10219-vulkan-win-x64-vc143", "llama-server.exe"),
        Path.Combine("TextRuntime", "llama-server.exe"));

    internal static string ResolveAssetPath(string? diagnosticPath, string applicationDirectory,
        string installedPath, string bundledRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(diagnosticPath)) return Path.GetFullPath(diagnosticPath);
        var bundledPath = Path.Combine(applicationDirectory, bundledRelativePath);
        return File.Exists(bundledPath) ? bundledPath : installedPath;
    }
    public static bool IsInstalled => File.Exists(RuntimePath) && File.Exists(ModelPath);

    public LocalQwenHost() : this(DefaultIdleUnloadDelay, null) { }

    internal LocalQwenHost(TimeSpan idleUnloadDelay, Func<Task<bool>>? startOverride)
    {
        if (idleUnloadDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleUnloadDelay));
        _idleUnloadDelay = idleUnloadDelay;
        _startCore = startOverride ?? StartCoreAsync;
        _idleStopTimer = new System.Threading.Timer(OnIdleStop);
    }

    /// <summary>
    /// Keeps this host's child loaded for one text operation. A cancelled caller stops waiting for
    /// startup without cancelling a startup already shared with another caller.
    /// </summary>
    public async Task<IDisposable?> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ActivityLease lease;
        lock (_gate)
        {
            if (_disposed) return null;
            _activeLeases++;
            CancelIdleStopLocked();
            lease = new ActivityLease(this);
        }

        try
        {
            if (!await StartAsync().WaitAsync(cancellationToken).ConfigureAwait(false))
            {
                lease.Dispose();
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public Task<bool> StartAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.FromResult(false);
            // A crashed child may be restarted by an explicit user action, never a retry loop.
            if (_start is { IsCompleted: true } && (_process is null || _process.HasExited))
            {
                CancelIdleStopLocked();
                _start = null;
            }
            if (_start is null)
            {
                CancelIdleStopLocked();
                var start = Task.Run(_startCore);
                _start = start;
                _ = start.ContinueWith(_ => ScheduleIdleStopIfUnused(start), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else if (_start is { IsCompletedSuccessfully: true, Result: true })
            {
                ScheduleIdleStopIfUnusedLocked();
            }
            return _start;
        }
    }

    private async Task<bool> StartCoreAsync()
    {
        try
        {
            if (!IsInstalled) { Status = "Нужны текстовая Qwen и локальный runtime."; return false; }
            Status = "Проверяю текстовую модель…";
            var isCustomModel = Environment.GetEnvironmentVariable("EGOIST_VOICE_QWEN_MODEL_PATH") is { Length: > 0 };
            if (!isCustomModel)
            {
                await using (var file = new FileStream(ModelPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
                {
                    if (file.Length != ModelBytes || !Convert.ToHexString(await SHA256.HashDataAsync(file, _lifetime.Token))
                            .Equals(ModelSha256, StringComparison.OrdinalIgnoreCase))
                    { Status = "Текстовая модель повреждена. Нужна повторная установка."; return false; }
                }
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            var start = CreateStartInfo();
            lock (_gate)
            {
                if (_disposed) return false;
                _process?.Dispose();
                _job?.Dispose();
                _process = Process.Start(start) ?? throw new InvalidOperationException("Qwen process did not start");
                _job = new OwnedProcessJob(_process);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            Status = "Загружаю Qwen в память…";
            using var formatter = new LocalTextFormatter();
            for (var attempt = 0; attempt < 80; attempt++)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                if (_process.HasExited) { Status = "Qwen не запустилась. Проверьте память и доступность порта 47823."; return false; }
                var models = await formatter.GetModelsAsync(Endpoint, _lifetime.Token).ConfigureAwait(false);
                if (models.Contains(ModelId))
                {
                    Status = "Готовлю первую генерацию Qwen…";
                    var prime = await formatter.FormatAsync("короткая проверка", Endpoint, ModelId,
                        TimeSpan.FromSeconds(20), false, _lifetime.Token).ConfigureAwait(false);
                    if (prime.Status is TextFormattingStatus.Timeout or TextFormattingStatus.Unavailable)
                    { Status = "Qwen не ответила при прогреве. Повторите запуск."; StopChild(); return false; }
                    Status = "Qwen готова · локально · GPU";
                    AppLog.Write("Local Qwen ready; loopback-only; text logging disabled");
                    return true;
                }
                await Task.Delay(250, _lifetime.Token).ConfigureAwait(false);
            }
            Status = "Qwen не успела запуститься. Быстрая диктовка доступна.";
            StopChild();
            return false;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception) { Status = "Не удалось запустить Qwen. Быстрая диктовка доступна."; StopChild(); return false; }
    }

    internal static ProcessStartInfo CreateStartInfo()
    {
        var start = new ProcessStartInfo(RuntimePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(RuntimePath)!
        };
        foreach (var arg in new[] { "--model", ModelPath, "--alias", ModelId, "--host", "127.0.0.1", "--port", Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ctx-size", "2048", "-fa", "on", "--no-mmap", "--parallel", "1", "--n-gpu-layers", "99", "--split-mode", "none", "--jinja",
            "--chat-template-kwargs", "{\"enable_thinking\":false}", "--api-key", AuthenticationToken,
            "-b", "512", "-ub", "256", "-t", "8", "-tb", "8",
            "--log-disable" }) start.ArgumentList.Add(arg);
        return start;
    }

    private void StopChild()
    {
        lock (_gate)
        {
            CancelIdleStopLocked();
            try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            _job?.Dispose();
            _job = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            CancelIdleStopLocked();
        }
        StopChild();
        _idleStopTimer.Dispose();
        // The startup task can still observe the process/token; handles are released after it ends.
        _ = (_start ?? Task.CompletedTask).ContinueWith(_ => { _process?.Dispose(); _lifetime.Dispose(); }, TaskScheduler.Default);
    }

    private void ReleaseActivity()
    {
        lock (_gate)
        {
            if (_activeLeases > 0) _activeLeases--;
            ScheduleIdleStopIfUnusedLocked();
        }
    }

    private void ScheduleIdleStopIfUnused(Task<bool> completedStart)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_start, completedStart)) ScheduleIdleStopIfUnusedLocked();
        }
    }

    private void ScheduleIdleStopIfUnusedLocked()
    {
        if (_disposed || _activeLeases != 0 || _start is not { IsCompletedSuccessfully: true, Result: true } ||
            _process is null || _process.HasExited) return;
        var delayMilliseconds = Math.Max(1L, (long)Math.Ceiling(_idleUnloadDelay.TotalMilliseconds));
        _idleDeadlineTicks = Environment.TickCount64 + delayMilliseconds;
        _idleStopTimer.Change(TimeSpan.FromMilliseconds(delayMilliseconds), Timeout.InfiniteTimeSpan);
    }

    private void CancelIdleStopLocked()
    {
        _idleDeadlineTicks = long.MaxValue;
        _idleStopTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void OnIdleStop(object? state)
    {
        lock (_gate)
        {
            if (_disposed || _activeLeases != 0 || _start is not { IsCompletedSuccessfully: true, Result: true } ||
                _process is null || _process.HasExited) return;
            if (_idleDeadlineTicks == long.MaxValue) return;
            var remainingMilliseconds = _idleDeadlineTicks - Environment.TickCount64;
            if (remainingMilliseconds > 0)
            {
                _idleStopTimer.Change(TimeSpan.FromMilliseconds(remainingMilliseconds), Timeout.InfiniteTimeSpan);
                return;
            }
            _idleDeadlineTicks = long.MaxValue;
            StopChild();
            _start = null;
            Status = "Qwen выгружена после простоя. Запустится при следующем обращении.";
            AppLog.Write("Local Qwen stopped after idle timeout");
        }
    }

    private sealed class ActivityLease(LocalQwenHost owner) : IDisposable
    {
        private LocalQwenHost? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseActivity();
    }

}
