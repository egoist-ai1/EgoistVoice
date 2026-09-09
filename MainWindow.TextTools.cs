using System.Diagnostics;
using Egoist.Voice.Core;
using Egoist.Voice.Services;
using NAudio.Wave;

namespace Egoist.Voice;

public partial class MainWindow
{
    private readonly LocalTextFormatter _textFormatter = new();
    private LocalQwenHost? _localQwen;
    private bool _textModelStartupAllowed;
    private bool _previousAutoQwen;
    private DictationSettings _currentTextSettings = DictationSettings.Default;
    public string LastOperationSummary { get; private set; } = "Время обработки появится после первой диктовки.";

    public string LocalQwenStatus => _localQwen?.Status ?? "Локальная Qwen выключена.";
    public bool CanStartLocalQwen => LocalQwenHost.IsInstalled;
    public void BeginTextModelWarmup()
    {
        _textModelStartupAllowed = true;
        ApplyLocalQwenPreference(_currentTextSettings);
    }
    private void ApplyLocalQwenPreference(DictationSettings settings)
    {
        if (!_textModelStartupAllowed) return;
        var autoStart = settings.StartLocalQwen && !settings.PreserveSpokenWords;
        if (autoStart)
        {
            if (_localQwen is null)
            {
                _localQwen = new LocalQwenHost();
                _ = _localQwen.StartAsync();
            }
        }
        else if (_previousAutoQwen) { _localQwen?.Dispose(); _localQwen = null; }
        _previousAutoQwen = autoStart;
    }
    public Task<bool> StartLocalQwenAsync() => (_localQwen ??= new LocalQwenHost()).StartAsync();

    public async Task<TranscriptionResult> TranscribeFileForEditorAsync(string path,
        IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
    {
        if (_isRecording || _isProcessing) throw new InvalidOperationException("Дождитесь завершения текущей диктовки.");
        _isProcessing = true;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
        var clock = Stopwatch.StartNew();
        try
        {
            var result = await _transcription.TranscribeAsync(path, progress, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            return new(_postProcessor.Process(result.Text), clock.Elapsed);
        }
        finally { _isProcessing = false; }
    }

    public Task<IReadOnlyList<string>> GetTextModelsAsync(string endpoint, CancellationToken cancellationToken) =>
        _textFormatter.GetModelsAsync(endpoint, cancellationToken);

    public async Task<TranscriptionResult> TranscribeHistoryForEditorAsync(string recordingId,
        IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
    {
        if (_isRecording || _isProcessing) throw new InvalidOperationException("Дождитесь завершения текущей диктовки.");
        if (_transcription is not ISampleTranscriptionService engine)
            throw new InvalidOperationException("Текущий движок не поддерживает повторную обработку записи.");
        _isProcessing = true;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
        var clock = Stopwatch.StartNew();
        try
        {
            _recentRecordings.StopPlayback();
            var samples = await _recentRecordings.ReadSamplesForTranscriptionAsync(recordingId, operation.Token);
            var result = await engine.TranscribeSamplesAsync(samples, 16_000, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            return new(_postProcessor.Process(result.Text), clock.Elapsed);
        }
        finally { _isProcessing = false; }
    }

    public Task<TextFormattingResult> EditTextAsync(string text, string endpoint, string model,
        bool correctWords, CancellationToken cancellationToken) =>
        FormatTextWithHostAsync(text, endpoint, model, TimeSpan.FromSeconds(30), correctWords, cancellationToken);

    private async Task<TextFormattingResult> FormatTextWithHostAsync(string text, string endpoint, string model,
        TimeSpan budget, bool allowWordCorrection, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(budget);
        IDisposable? lease = null;
        try
        {
            if (model == LocalQwenHost.ModelId &&
                LocalTextFormatter.TryGetEndpoint(endpoint, out var target) &&
                target.AbsoluteUri.TrimEnd('/') == LocalQwenHost.Endpoint)
            {
                lease = await (_localQwen ??= new LocalQwenHost()).AcquireAsync(operation.Token);
                if (lease is null)
                    return new(text, TextFormattingStatus.Unavailable, clock.Elapsed);
            }
            var remaining = budget - clock.Elapsed;
            if (remaining < TimeSpan.FromMilliseconds(100))
                return new(text, TextFormattingStatus.Timeout, clock.Elapsed);
            var result = await _textFormatter.FormatAsync(text, endpoint, model, remaining,
                allowWordCorrection, operation.Token);
            return result with { Elapsed = clock.Elapsed };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(text, TextFormattingStatus.Timeout, clock.Elapsed);
        }
        finally
        {
            lease?.Dispose();
        }
    }
}
