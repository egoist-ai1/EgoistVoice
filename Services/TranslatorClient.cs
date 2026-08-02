using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Egoist.Voice.Services;

/// <summary>
/// Клиент локального переводчика (HY-MT1.5-7B через llama-server из комплекта
/// EGOIST Translator). Конвенция EGOIST: общий порт 47821 — кто первым поднял
/// сервер (переводчик или диктовка), тот им и владеет, второй просто ходит по
/// HTTP. Если сервер не запущен, но рантайм установлен
/// (%LOCALAPPDATA%\EgoistTranslator), клиент поднимает сайдкар сам и привязывает
/// его к Job Object — процесс с моделью гарантированно умирает вместе с нами.
/// </summary>
public sealed class TranslatorClient : IDisposable
{
    public const int SharedPort = 47821;

    private static readonly string RuntimeDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EgoistTranslator");

    private static string ServerExePath => Path.Combine(RuntimeDir, "llama", "llama-server.exe");

    private static string ModelsDir => Path.Combine(RuntimeDir, "models");

    private readonly HttpClient _health;
    private readonly HttpClient _api;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    private Process? _sidecar;
    private nint _job;
    private int _disposed;

    public TranslatorClient()
        : this(
            new HttpClient { Timeout = TimeSpan.FromSeconds(5) },
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
    {
    }

    internal TranslatorClient(HttpClient healthClient, HttpClient apiClient)
    {
        _health = healthClient;
        _api = apiClient;
    }

    public static bool RuntimeInstalled => File.Exists(ServerExePath) && FindModel() is not null;

    /// <summary>
    /// Переводит текст; null — переводчик недоступен (вызывающий вставляет
    /// оригинал). Прогресс медленных стадий отдаётся в <paramref name="status"/>.
    /// </summary>
    public async Task<string?> TranslateAsync(
        string text,
        string targetLanguage,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsHealthyAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!RuntimeInstalled)
                {
                    AppLog.Write("Перевод: рантайм EGOIST Translator не установлен (scripts\\install-model.ps1)");
                    return null;
                }

                status("Запускаю переводчик");
                if (!await EnsureServerAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }
            }

            status("Перевожу");

            var payload = JsonSerializer.Serialize(new
            {
                model = "hy-mt",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = $"Translate the following segment into {targetLanguage}, without additional explanation.\n\n{text}",
                    },
                },
                temperature = 0.3,
                top_p = 0.6,
                top_k = 20,
                repeat_penalty = 1.05,
                max_tokens = Math.Clamp(text.Length * 2, 256, 4096),
                stream = false,
            });

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(TimeSpan.FromSeconds(60));

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _api
                .PostAsync($"http://127.0.0.1:{SharedPort}/v1/chat/completions", content, linked.Token)
                .ConfigureAwait(false);

            var json = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Write($"Перевод: llama-server вернул HTTP {(int)response.StatusCode}");
                return null;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var body) &&
                body.ValueKind == JsonValueKind.String)
            {
                var translated = body.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(translated) ? null : translated;
            }

            AppLog.Write($"Перевод: неожиданный ответ llama-server ({json[..Math.Min(json.Length, 200)]})");
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Write("Перевод: тайм-аут ожидания модели");
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Write("Перевод не удался", exception);
            return null;
        }
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _health
                .GetAsync($"http://127.0.0.1:{SharedPort}/health", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            // Один лишь /health недостаточен: любой локальный сервис на общем порту мог бы
            // получить приватный текст. До POST проверяем, что порт обслуживает HY-MT.
            using var modelsResponse = await _health
                .GetAsync($"http://127.0.0.1:{SharedPort}/v1/models", cancellationToken)
                .ConfigureAwait(false);
            if (!modelsResponse.IsSuccessStatusCode)
            {
                return false;
            }

            var modelsJson = await modelsResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return HasExpectedModel(modelsJson);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    internal static bool HasExpectedModel(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return data.EnumerateArray().Any(model =>
                model.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                (id.GetString()?.Contains("HY-MT", StringComparison.OrdinalIgnoreCase) ?? false));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<bool> EnsureServerAsync(CancellationToken cancellationToken)
    {
        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await IsHealthyAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (_sidecar is null || _sidecar.HasExited)
            {
                Spawn();
            }

            // Q8_0 (~8 ГБ) грузится в VRAM десятки секунд; на этот порт мог
            // одновременно встать и EGOIST Translator — тогда наш сайдкар умрёт
            // на bind, а health всё равно позеленеет. Это тоже успех.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(150);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsHealthyAsync(cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            AppLog.Write("Перевод: llama-server не поднялся за 150 секунд");
            return false;
        }
        finally
        {
            _startLock.Release();
        }
    }

    private void Spawn()
    {
        var model = FindModel();
        if (model is null)
        {
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = ServerExePath,
            Arguments = $"-m \"{model}\" --host 127.0.0.1 --port {SharedPort} -c 8192 -ngl 99 --no-webui --jinja",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(ServerExePath) ?? "",
        };

        _sidecar?.Dispose();
        _sidecar = Process.Start(psi);
        if (_sidecar is null)
        {
            AppLog.Write("Перевод: не удалось запустить llama-server");
            return;
        }

        AppLog.Write($"Перевод: llama-server запущен, PID {_sidecar.Id}, порт {SharedPort}");
        AssignToJob(_sidecar);
    }

    private static string? FindModel()
    {
        try
        {
            if (!Directory.Exists(ModelsDir))
            {
                return null;
            }

            return Directory.EnumerateFiles(ModelsDir, "*.gguf")
                .Select(f => new FileInfo(f))
                .Where(f => f.Name.Contains("HY-MT", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Length)
                .FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private void AssignToJob(Process process)
    {
        if (_job == 0)
        {
            _job = JobNative.CreateJobObjectW(0, null);
            if (_job != 0)
            {
                var info = new JobNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = { LimitFlags = JobNative.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
                };

                JobNative.SetInformationJobObject(
                    _job,
                    JobNative.JobObjectExtendedLimitInformation,
                    ref info,
                    (uint)Marshal.SizeOf<JobNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
            }
        }

        if (_job != 0 && !JobNative.AssignProcessToJobObject(_job, process.Handle))
        {
            AppLog.Write($"Перевод: AssignProcessToJobObject не удался ({Marshal.GetLastWin32Error()})");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_sidecar is { HasExited: false })
            {
                _sidecar.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Job Object добьёт при закрытии хэндла
        }

        _sidecar?.Dispose();
        if (_job != 0)
        {
            JobNative.CloseHandle(_job);
            _job = 0;
        }

        _health.Dispose();
        _api.Dispose();
        _startLock.Dispose();
    }

    /// <summary>Job Object: сайдкар с моделью умирает вместе с приложением, включая крэш.</summary>
    private static class JobNative
    {
        internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
        internal const int JobObjectExtendedLimitInformation = 9;

        [StructLayout(LayoutKind.Sequential)]
        internal struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(
            nint hJob, int jobObjectInfoClass,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(nint hObject);
    }
}
