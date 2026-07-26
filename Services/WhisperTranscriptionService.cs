using System.Diagnostics;
using System.IO;
using Egoist.Voice.Core;
using NAudio.Utils;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace Egoist.Voice.Services;

public sealed class WhisperTranscriptionService : ITranscriptionEngine, IUnloadableEngine
{
    private readonly SemaphoreSlim _factoryLock = new(1, 1);
    private readonly IModelManager _modelManager;
    private readonly bool _ownsModelManager;
    private WhisperFactory? _factory;
    private volatile bool _disposed;

    public WhisperTranscriptionService(IModelManager? modelManager = null)
    {
        _modelManager = modelManager ?? new ModelManager(ModelCatalog.CreateRequiredModels());
        _ownsModelManager = modelManager is null;
    }

    public string EngineName => "Whisper";

    public async Task WarmUpAsync(
        IProgress<ModelProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_factory is not null)
        {
            return;
        }

        await _factoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_factory is not null)
            {
                return;
            }

            var transferProgress = progress is null
                ? null
                : new Progress<ModelTransferProgress>(value =>
                    progress.Report(new ModelProgress(GetProgressLabel(value), value.Percentage)));
            var modelPath = await _modelManager
                .EnsureModelAsync(ModelCatalog.Whisper, transferProgress, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new ModelProgress("Запускаю модель…", 100));
            var factory = await Task.Run(() => WhisperFactory.FromPath(modelPath), cancellationToken)
                .ConfigureAwait(false);
            await PrimeRuntimeAsync(factory, cancellationToken).ConfigureAwait(false);
            _factory = factory;
        }
        finally
        {
            _factoryLock.Release();
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        IProgress<ModelProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await WarmUpAsync(progress, cancellationToken).ConfigureAwait(false);

        // The decode holds the factory lock for its whole duration. Previously the lock covered
        // only warm-up, so the idle unloader could take it freely mid-decode and Dispose() the
        // native context underneath a running ProcessAsync — an access violation, not an exception.
        await _factoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var factory = _factory ?? throw new InvalidOperationException("Модель Whisper не загружена.");
            await using var audio = File.OpenRead(audioPath);
            using var processor = CreateProcessor(factory, detectLanguage: true);

            var segments = new List<TranscriptSegment>();
            await foreach (var segment in processor.ProcessAsync(audio, cancellationToken).ConfigureAwait(false))
            {
                segments.Add(new TranscriptSegment(segment.Text, segment.Start, segment.End));
            }

            stopwatch.Stop();
            return new TranscriptionResult(TranscriptFormatter.Format(segments), stopwatch.Elapsed);
        }
        finally
        {
            _factoryLock.Release();
        }
    }

    /// <summary>
    /// Whisper is now only reached when the primary engine has already produced Russian and
    /// something in it looks like an English term. The settings follow from that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Language is auto-detected, not forced to Russian.</b> Forcing "ru" is what makes Whisper
    /// transliterate English into Cyrillic — precisely the failure this engine exists to repair.
    /// The prompt still anchors the domain, so detection does not drift to English on a Russian
    /// sentence that merely contains "GitHub".
    /// </para>
    /// <para>
    /// <b>Beam search instead of greedy.</b> Greedy decoding is the right default when a model runs
    /// on every dictation; this one no longer does. It is reached for a minority of sessions where
    /// getting the term right is the entire point, and beam search is the cheapest accuracy the
    /// decoder offers.
    /// </para>
    /// <para>
    /// <b>No context carried between segments.</b> Whisper's default is to feed each segment the
    /// previous one as context, which is how a single early mistake propagates through the rest of
    /// a long dictation.
    /// </para>
    /// </remarks>
    private static WhisperProcessor CreateProcessor(WhisperFactory factory, bool detectLanguage)
    {
        var builder = factory.CreateBuilder();
        builder = detectLanguage ? builder.WithLanguageDetection() : builder.WithLanguage("ru");
        builder = builder
            .WithThreads(WhisperThreads)
            .WithPrompt(TechnicalTermCatalog.WhisperPrompt)
            .WithNoContext();

        // Beam size is left at whisper.cpp's own default for this strategy rather than pinned:
        // the binding in this version exposes the strategy switch but not the size, and the
        // default is the value we would have set anyway.
        return builder.WithBeamSearchSamplingStrategy().Build();
    }

    /// <summary>
    /// Whisper is compute-bound and, unlike GigaAM, is not sharing the machine with a second decode
    /// any more. Half the logical cores was a hedge against that; three quarters is a better fit now,
    /// while still leaving room for the UI and the audio thread.
    /// </summary>
    private static int WhisperThreads => Math.Clamp(Environment.ProcessorCount * 3 / 4, 2, 12);

    private static async Task PrimeRuntimeAsync(
        WhisperFactory factory,
        CancellationToken cancellationToken)
    {
        await using var silence = new MemoryStream();
        using (var writer = new WaveFileWriter(
            new IgnoreDisposeStream(silence),
            new WaveFormat(16_000, 16, 1)))
        {
            writer.Write(new byte[3_200]);
        }
        silence.Position = 0;

        // The priming pass forces the language so it stays cheap: detection on pure silence is
        // wasted work, and the point here is only to initialize the native runtime.
        using var processor = CreateProcessor(factory, detectLanguage: false);
        await foreach (var _ in processor.ProcessAsync(silence, cancellationToken).ConfigureAwait(false))
        {
            // A short silent pass initializes the selected native GPU/CPU runtime.
        }
    }

    private static string GetProgressLabel(ModelTransferProgress value) => value.Stage switch
    {
        ModelTransferStage.Verifying => "Проверяю модель…",
        ModelTransferStage.Ready => "Модель готова",
        _ => "Загружаю модель…"
    };

    /// <summary>
    /// Releases the native model while keeping the service usable: the next warm-up rebuilds it.
    /// Whisper large-v3-turbo q5_0 holds roughly 600 MB resident, which is hard to justify in a
    /// tray utility that may go hours without a single mixed-language dictation.
    /// </summary>
    public bool TryUnload()
    {
        if (_disposed || _factory is null)
        {
            return false;
        }

        // Never block: an unload that waits is an unload that can deadlock against a decode.
        if (!_factoryLock.Wait(0))
        {
            return false;
        }

        try
        {
            if (_factory is null)
            {
                return false;
            }
            _factory.Dispose();
            _factory = null;
            AppLog.Write("Whisper unloaded after idle period");
            return true;
        }
        finally
        {
            _factoryLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Waits for the decode slot, exactly as the primary engine does. Without this, closing the
        // application while "Уточняю термины" is on screen released the native context underneath a
        // running ProcessAsync — an access violation, not a catchable exception — and left the decode
        // to fail on a disposed semaphore. A leak at exit is strictly better than a native crash.
        if (!_factoryLock.Wait(TimeSpan.FromSeconds(2)))
        {
            AppLog.Write("Whisper decode still running at shutdown; native factory left to process teardown");
            return;
        }

        try
        {
            _factory?.Dispose();
            _factory = null;
        }
        finally
        {
            _factoryLock.Release();
        }

        _factoryLock.Dispose();
        if (_ownsModelManager)
        {
            _modelManager.Dispose();
        }
    }
}
