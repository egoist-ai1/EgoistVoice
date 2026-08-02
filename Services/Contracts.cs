namespace Egoist.Voice.Services;

public interface IAudioCaptureService : IDisposable
{
    event EventHandler<float>? LevelChanged;

    /// <summary>
    /// Completed 16 kHz mono session normalized to −1…1. Normal dictation consumes the same
    /// in-memory array directly; this event exists for optional observers and never causes a WAV.
    /// </summary>
    event EventHandler<float[]>? SamplesAvailable;

    void Start();
    Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken);
    Task<string?> CancelAsync();
}

public sealed record AudioCaptureResult(
    string? Path,
    float[] Samples,
    int SampleRate,
    bool HasSpeech,
    TimeSpan Duration,
    TimeSpan DetectedSpeech,
    double PeakDecibels,
    string? RejectionMessage = null);

public interface ITranscriptionService : IDisposable
{
    Task WarmUpAsync(IProgress<ModelProgress>? progress, CancellationToken cancellationToken);
    Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        IProgress<ModelProgress>? progress,
        CancellationToken cancellationToken);
}

internal interface ITranscriptionEngine : ITranscriptionService
{
    string EngineName { get; }
}

/// <summary>
/// An engine that can decode audio already in memory.
/// </summary>
/// <remarks>
/// Live transcription needs this: a phrase lasts a second or two, and writing it to disk and
/// reading it back would cost a meaningful share of the latency the feature exists to remove. A
/// separate interface rather than a member on <see cref="ITranscriptionService"/> so callers that
/// only transcribe files are not forced to care.
/// </remarks>
public interface ISampleTranscriptionService
{
    Task<TranscriptionResult> TranscribeSamplesAsync(
        float[] samples,
        int sampleRate,
        CancellationToken cancellationToken);
}

/// <summary>
/// An engine whose native model can be released without discarding the service. Implemented by the
/// mixed-language fallback, which is idle most of the time and expensive to keep resident.
/// </summary>
internal interface IUnloadableEngine
{
    /// <summary>Returns false when the model is already gone or currently in use.</summary>
    bool TryUnload();
}

internal interface ITranscriptCandidateSelector
{
    TranscriptSelection Select(string gigaAm, string whisper);
}

public interface ITextInsertionService
{
    Task InsertAsync(string text, nint targetWindow, CancellationToken cancellationToken);
}

public interface IClipboardService
{
    Task CopyAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// A clipboard that can hand back what it overwrote. Separate from <see cref="IClipboardService"/>
/// so a caller that only needs to copy is not forced to reason about restore semantics.
/// </summary>
public interface IRestorableClipboardService : IClipboardService
{
    Task<ClipboardSnapshot> CopyAsync(string text, bool captureSnapshot, CancellationToken cancellationToken);
    Task<bool> TryRestoreAsync(ClipboardSnapshot snapshot, CancellationToken cancellationToken);
}

public interface IModelManager : IDisposable
{
    event EventHandler<ModelTransferProgress>? ProgressChanged;
    IReadOnlyList<ModelDescriptor> RequiredModels { get; }
    bool AreAllModelsReady { get; }
    ModelTransferProgress? CurrentProgress { get; }
    Task<string> EnsureModelAsync(
        ModelDescriptor descriptor,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken);
    Task DownloadRequiredModelsAsync(CancellationToken cancellationToken);
}

public sealed record TranscriptionResult(string Text, TimeSpan Elapsed);

/// <summary>
/// A percentage of <c>null</c> means indeterminate — a stage that has no meaningful fraction, such
/// as refining an already-decoded transcript. Rendering it as 0 % would read as "stuck".
/// </summary>
public sealed record ModelProgress(string Label, double? Percentage);

public enum ModelKind
{
    Speech
}

public sealed record ModelDescriptor(
    string Id,
    string DisplayName,
    ModelKind Kind,
    Uri DownloadUri,
    string FileName,
    long SizeBytes,
    string Sha256,
    bool Optional = false);

public enum ModelTransferStage
{
    Waiting,
    Downloading,
    Verifying,
    Loading,
    Ready,
    Failed
}

public sealed record ModelTransferProgress(
    string ModelName,
    int ModelIndex,
    int ModelCount,
    ModelTransferStage Stage,
    long BytesReceived,
    long TotalBytes,
    double Percentage,
    double OverallPercentage,
    double BytesPerSecond = 0,
    TimeSpan? EstimatedRemaining = null,
    string? Error = null);
