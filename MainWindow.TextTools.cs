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
        if (settings.StartLocalQwen)
        {
            if (_localQwen is null)
            {
                _localQwen = new LocalQwenHost();
                _ = _localQwen.StartAsync();
            }
        }
        else if (_previousAutoQwen) { _localQwen?.Dispose(); _localQwen = null; }
        _previousAutoQwen = settings.StartLocalQwen;
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
            // Bound decoded memory before the ASR file reader allocates its sample buffer.
            await Task.Run(() =>
            {
                using var reader = new AudioFileReader(path);
                if (reader.TotalTime > TimeSpan.FromMinutes(30))
                    throw new InvalidOperationException("Разделите запись на фрагменты до 30 минут.");
            }, operation.Token);
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
        _textFormatter.FormatAsync(text, endpoint, model, TimeSpan.FromSeconds(30), correctWords, cancellationToken);
}
