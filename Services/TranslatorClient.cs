using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Egoist.Translation.Client;
using Egoist.Translation.Contracts;

namespace Egoist.Voice.Services;

public enum TranslationFailureKind
{
    None,
    EngineMissing,
    EngineBusy,
    Timeout,
    UnsupportedLanguage,
    IncompatibleEngine,
    ModelInvalid,
    Cancelled,
    Failed
}

public sealed record VoiceTranslationOutcome(
    string? Text,
    TranslationFailureKind Failure,
    string UserMessage)
{
    public bool Succeeded => Failure == TranslationFailureKind.None && !string.IsNullOrWhiteSpace(Text);

    public static VoiceTranslationOutcome Success(string text) =>
        new(text, TranslationFailureKind.None, "Перевод готов");

    public static VoiceTranslationOutcome Error(TranslationFailureKind failure, string message) =>
        new(null, failure, message);
}

internal interface ITranslationEngineGateway : IDisposable
{
    Task<EngineStatusSnapshot> EnsureAvailableAsync(CancellationToken cancellationToken);

    Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Privacy-safe Voice adapter for the shared current-user Engine Host. Source
/// text is framed only after the named-pipe handshake and model identity pass.
/// Voice never owns or kills the shared host because Translator may use it.
/// </summary>
public sealed class TranslatorClient : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> TargetLanguages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["English"] = TranslationLanguages.English,
            ["Russian"] = TranslationLanguages.Russian,
            ["Chinese"] = "zh",
            ["French"] = "fr",
            ["German"] = "de",
            ["Spanish"] = "es",
            ["Portuguese"] = "pt",
            ["Japanese"] = "ja",
            ["Korean"] = "ko",
            ["Italian"] = "it",
            ["Turkish"] = "tr",
            ["Ukrainian"] = "uk",
            ["Polish"] = "pl",
        };

    private readonly ITranslationEngineGateway _gateway;
    private int _disposed;

    public TranslatorClient()
        : this(new SharedTranslationEngineGateway())
    {
    }

    internal TranslatorClient(ITranslationEngineGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public async Task<VoiceTranslationOutcome> TranslateAsync(
        string text,
        string targetLanguage,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(status);

        if (!TargetLanguages.TryGetValue(targetLanguage, out var targetCode))
        {
            return VoiceTranslationOutcome.Error(
                TranslationFailureKind.UnsupportedLanguage,
                "Этот язык пока не входит в офлайн-пакет");
        }

        try
        {
            status("Проверяю движок");
            var engine = await _gateway.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
            status(StatusLabel(engine.State));

            var result = await _gateway.TranslateAsync(new TranslationRequest
            {
                SourceText = text,
                SourceLanguage = TranslationLanguages.Auto,
                TargetLanguage = targetCode,
                Profile = TranslationProfile.NaturalMessage,
                Format = text.Contains('\n', StringComparison.Ordinal)
                    ? TranslationFormat.Paragraphs
                    : TranslationFormat.Plain,
                Priority = RequestPriority.Interactive,
                DeadlineMs = 60_000,
                ContextKey = "egoist-voice-dictation",
            }, cancellationToken).ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(result.Text)
                ? VoiceTranslationOutcome.Error(TranslationFailureKind.Failed, "Движок вернул пустой перевод")
                : VoiceTranslationOutcome.Success(result.Text.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TranslationEngineException exception)
        {
            var failure = MapFailure(exception.Error.Code);
            AppLog.Write($"Перевод не выполнен: {exception.Error.Code}; retryable={exception.Error.Retryable}");
            return VoiceTranslationOutcome.Error(failure.Kind, failure.Message);
        }
        catch (Exception)
        {
            AppLog.Write("Перевод не выполнен: unexpected local engine failure");
            return VoiceTranslationOutcome.Error(TranslationFailureKind.Failed, "Не удалось выполнить перевод");
        }
    }

    internal static bool SupportsTarget(string targetLanguage) =>
        TargetLanguages.ContainsKey(targetLanguage);

    private static string StatusLabel(EngineState state) => state switch
    {
        EngineState.Verifying => "Проверяю модель",
        EngineState.Loading => "Запускаю модель",
        EngineState.Ready => "Перевожу",
        EngineState.Sleeping => "Пробуждаю модель",
        EngineState.Repairing => "Восстанавливаю движок",
        _ => "Перевожу",
    };

    private static (TranslationFailureKind Kind, string Message) MapFailure(ProtocolErrorCode code) => code switch
    {
        ProtocolErrorCode.EngineMissing =>
            (TranslationFailureKind.EngineMissing, "Установите общий пакет перевода"),
        ProtocolErrorCode.EngineBusy =>
            (TranslationFailureKind.EngineBusy, "Движок занят — повторите через несколько секунд"),
        ProtocolErrorCode.Timeout =>
            (TranslationFailureKind.Timeout, "Перевод не успел завершиться"),
        ProtocolErrorCode.Cancelled =>
            (TranslationFailureKind.Cancelled, "Перевод отменён"),
        ProtocolErrorCode.IncompatibleClient =>
            (TranslationFailureKind.IncompatibleEngine, "Обновите общий движок перевода"),
        ProtocolErrorCode.ModelMismatch =>
            (TranslationFailureKind.ModelInvalid, "Модель перевода не прошла проверку"),
        ProtocolErrorCode.AmbiguousLanguage =>
            (TranslationFailureKind.UnsupportedLanguage, "Не удалось определить язык исходного текста"),
        _ =>
            (TranslationFailureKind.Failed, "Не удалось выполнить перевод"),
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _gateway.Dispose();
        }
    }
}

internal sealed class SharedTranslationEngineGateway : ITranslationEngineGateway
{
    private static readonly IReadOnlySet<string> TrustedModelHashes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "5c3fe0b1408a5ceb0143184ef247b11b579c525f4b02b060e6c851bb76fef1a4",
            "d98fe604dec1f28f58f80d7d560f7177e584d3b8e5835862687660e5ff97cb40",
        };

    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly NamedPipeTranslationClient _client;
    private int _disposed;

    public SharedTranslationEngineGateway(string? pipeName = null)
    {
        _client = new NamedPipeTranslationClient(
            "egoist-voice",
            typeof(SharedTranslationEngineGateway).Assembly.GetName().Version?.ToString(3) ?? "2.2.0",
            TrustedModelHashes,
            pipeName);
    }

    public async Task<EngineStatusSnapshot> EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        try
        {
            return await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TranslationEngineException exception) when (exception.Error.Code == ProtocolErrorCode.EngineMissing)
        {
        }

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                return await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (TranslationEngineException exception) when (exception.Error.Code == ProtocolErrorCode.EngineMissing)
            {
            }

            var command = ResolveHostCommand() ?? throw EngineMissing("Shared translation host is not installed.");
            using var process = Process.Start(command);
            if (process is null)
            {
                throw EngineMissing("Shared translation host could not be started.");
            }

            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(8))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await _client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (TranslationEngineException exception) when (exception.Error.Code == ProtocolErrorCode.EngineMissing)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new TranslationEngineException(new ProtocolError
            {
                Code = ProtocolErrorCode.Timeout,
                Message = "Shared translation host did not become available in time.",
                Retryable = true,
            });
        }
        finally
        {
            _startGate.Release();
        }
    }

    public Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken) =>
        _client.TranslateAsync(request, cancellationToken);

    internal static string? ResolveCurrentSharedHostDirectory(string hostRoot)
    {
        var pointerPath = Path.Combine(hostRoot, "current.json");
        if (!File.Exists(pointerPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(pointerPath), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 6 ||
                root.GetProperty("schemaVersion").GetInt32() != 1)
            {
                return null;
            }

            var generationId = root.GetProperty("generationId").GetString();
            var relativePath = root.GetProperty("relativePath").GetString();
            if (!IsSafeSegment(generationId) ||
                !string.Equals(relativePath, $"generations/{generationId}", StringComparison.Ordinal))
            {
                return null;
            }

            var fullHostRoot = Path.GetFullPath(hostRoot);
            var generationRoot = Path.GetFullPath(Path.Combine(hostRoot, "generations", generationId!));
            return generationRoot.StartsWith(fullHostRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? generationRoot
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static ProcessStartInfo? ResolveHostCommand()
    {
        var hostRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EGOIST", "TranslationEngine", "v1", "host");
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "engine-host"),
            ResolveCurrentSharedHostDirectory(hostRoot),
            hostRoot,
        };

        foreach (var directory in candidates)
        {
            if (directory is null)
            {
                continue;
            }

            var executable = Path.Combine(directory, "Egoist.Translation.EngineHost.exe");
            if (File.Exists(executable))
            {
                return HiddenStartInfo(executable, directory);
            }

            var assembly = Path.Combine(directory, "Egoist.Translation.EngineHost.dll");
            if (File.Exists(assembly))
            {
                var startInfo = HiddenStartInfo("dotnet", directory);
                startInfo.ArgumentList.Add(assembly);
                return startInfo;
            }
        }

        return null;
    }

    private static ProcessStartInfo HiddenStartInfo(string fileName, string workingDirectory) => new()
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
    };

    private static TranslationEngineException EngineMissing(string message) => new(new ProtocolError
    {
        Code = ProtocolErrorCode.EngineMissing,
        Message = message,
        Retryable = true,
    });

    private static bool IsSafeSegment(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && value is not ("." or "..") &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _startGate.Dispose();
        }
    }
}
