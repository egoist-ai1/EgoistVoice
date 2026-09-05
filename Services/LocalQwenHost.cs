using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Egoist.Voice.Services;

/// <summary>Owns only the Qwen child it starts. Never controls the shared translation process.</summary>
public sealed class LocalQwenHost : IDisposable
{
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
    private Task<bool>? _start;
    private Process? _process;
    private OwnedProcessJob? _job;
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

    public static string ModelPath => Environment.GetEnvironmentVariable("EGOIST_VOICE_QWEN_MODEL_PATH") is { Length: > 0 } diagnosticModel
        ? Path.GetFullPath(diagnosticModel)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EgoistVoice", "TextModels", "Qwen3-4B-Q4_K_M.gguf");
    public static string RuntimePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Egoist", "TranslationEngine", "v1", "runtime", "llama-b10219-vulkan-win-x64-vc143", "llama-server.exe");
    public static bool IsInstalled => File.Exists(RuntimePath) && File.Exists(ModelPath);

    public Task<bool> StartAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.FromResult(false);
            // A crashed child may be restarted by an explicit user action, never a retry loop.
            if (_start is { IsCompleted: true } && (_process is null || _process.HasExited)) _start = null;
            return _start ??= Task.Run(StartCoreAsync);
        }
    }

    private async Task<bool> StartCoreAsync()
    {
        try
        {
            if (!IsInstalled) { Status = "Нужны текстовая Qwen и локальный runtime."; return false; }
            Status = "Проверяю текстовую модель…";
            await using (var file = new FileStream(ModelPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
            {
                if (file.Length != ModelBytes || !Convert.ToHexString(await SHA256.HashDataAsync(file, _lifetime.Token))
                        .Equals(ModelSha256, StringComparison.OrdinalIgnoreCase))
                { Status = "Текстовая модель повреждена. Нужна повторная установка."; return false; }
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
            "--ctx-size", "8192", "--parallel", "1", "--n-gpu-layers", "99", "--split-mode", "none", "--jinja",
            "--chat-template-kwargs", "{\"enable_thinking\":false}", "--api-key", AuthenticationToken,
            "--log-disable" }) start.ArgumentList.Add(arg);
        return start;
    }

    private void StopChild()
    {
        lock (_gate)
        {
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
        }
        StopChild();
        // The startup task can still observe the process/token; handles are released after it ends.
        _ = (_start ?? Task.CompletedTask).ContinueWith(_ => { _process?.Dispose(); _lifetime.Dispose(); }, TaskScheduler.Default);
    }
}
