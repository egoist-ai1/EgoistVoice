using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Egoist.Voice.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;

namespace Egoist.Voice.Services;

public sealed class GigaAmTranscriptionService : ITranscriptionEngine, ISampleTranscriptionService
{
    private const int SampleRate = 16_000;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly SemaphoreSlim _decodeLock = new(1, 1);
    private readonly IModelManager _modelManager;
    private readonly bool _ownsModelManager;
    private OfflineRecognizer? _recognizer;
    private volatile bool _disposed;

    public GigaAmTranscriptionService(IModelManager? modelManager = null)
    {
        _modelManager = modelManager ?? new ModelManager(ModelCatalog.CreateRequiredModels());
        _ownsModelManager = modelManager is null;
    }

    public string EngineName => "GigaAM";

    public async Task WarmUpAsync(IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
    {
        if (_recognizer is not null)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_recognizer is not null)
            {
                return;
            }

            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var descriptor in GigaDescriptors)
            {
                var transferProgress = progress is null
                    ? null
                    : new Progress<ModelTransferProgress>(value =>
                        progress.Report(new ModelProgress(GetProgressLabel(value), value.OverallPercentage)));
                paths[descriptor.Id] = await _modelManager.EnsureModelAsync(
                    descriptor,
                    transferProgress,
                    cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new ModelProgress("Запускаю GigaAM…", 100));
            var recognizer = await Task.Run(
                () => CreateRecognizer(
                    paths[ModelCatalog.GigaAmEncoder.Id],
                    paths[ModelCatalog.GigaAmDecoder.Id],
                    paths[ModelCatalog.GigaAmJoiner.Id],
                    paths[ModelCatalog.GigaAmTokens.Id]),
                cancellationToken).ConfigureAwait(false);

            // The first two ONNX Runtime invocations pay for graph optimization and
            // arena setup and run several times slower than steady state. Prime them
            // here so the first real dictation does not carry that cost.
            await Task.Run(() => PrimeRecognizer(recognizer), cancellationToken).ConfigureAwait(false);
            _recognizer = recognizer;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        IProgress<ModelProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await WarmUpAsync(progress, cancellationToken).ConfigureAwait(false);
        var samples = await Task.Run(
            () => AudioSampleReader.ReadMono16Khz(audioPath),
            cancellationToken).ConfigureAwait(false);

        await _decodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Native decoding blocks its thread. Without this hop the loop can run on
            // the WPF dispatcher, freezing the capsule and starving the low-level mouse
            // hook until Windows evicts it on LowLevelHooksTimeout.
            var text = await Task.Run(
                () => DecodeChunks(samples, progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new TranscriptionResult(text, stopwatch.Elapsed);
        }
        finally
        {
            _decodeLock.Release();
        }
    }

    /// <summary>
    /// Decodes audio already in memory. The file path exists because the recording is written to
    /// disk anyway; a live phrase has no file and should not acquire one just to be read back.
    /// </summary>
    public async Task<TranscriptionResult> TranscribeSamplesAsync(
        float[] samples,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        if (sampleRate != SampleRate)
        {
            throw new ArgumentException($"Ожидается {SampleRate} Гц, получено {sampleRate}.", nameof(sampleRate));
        }

        var stopwatch = Stopwatch.StartNew();
        await WarmUpAsync(null, cancellationToken).ConfigureAwait(false);
        await _decodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var text = await Task.Run(
                () => DecodeChunks(samples, null, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new TranscriptionResult(text, stopwatch.Elapsed);
        }
        finally
        {
            _decodeLock.Release();
        }
    }

    /// <summary>Above this, batching pays for the extra streams held in memory at once.</summary>
    private const int BatchDecodeThreshold = 2;

    private string DecodeChunks(
        float[] samples,
        IProgress<ModelProgress>? progress,
        CancellationToken cancellationToken)
    {
        var chunks = GigaAmAudioChunker.Split(samples, SampleRate);
        return chunks.Count >= BatchDecodeThreshold
            ? DecodeBatched(chunks, progress, cancellationToken)
            : DecodeSequentially(chunks, progress, cancellationToken);
    }

    private string DecodeSequentially(
        IReadOnlyList<GigaAmAudioChunk> chunks,
        IProgress<ModelProgress>? progress,
        CancellationToken cancellationToken)
    {
        var decoded = new List<DecodedAudioChunk>(chunks.Count);
        for (var index = 0; index < chunks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ModelProgress("Распознаю", index * 100d / chunks.Count));
            using var stream = _recognizer!.CreateStream();
            stream.AcceptWaveform(SampleRate, GetEngineSamples(chunks[index].Samples));
            _recognizer.Decode(stream);
            if (!string.IsNullOrWhiteSpace(stream.Result.Text))
            {
                decoded.Add(new DecodedAudioChunk(stream.Result.Text.Trim(), chunks[index].ParagraphBreakBefore));
            }
        }

        return TranscriptChunkJoiner.Join(decoded);
    }

    /// <summary>
    /// Number of chunks handed to the engine at once.
    /// </summary>
    /// <remarks>
    /// The batch used to be unbounded, which meant a thirty-minute recording built eighty-odd
    /// streams and pushed them through one encoder pass. sherpa pads a batch to its longest member,
    /// so activation memory grows linearly with batch size — that was the one place in the product
    /// where a long dictation could exhaust memory outright. Six keeps the encoder usefully busy
    /// while bounding the peak.
    /// </remarks>
    private const int MaxBatchSize = 6;

    /// <summary>
    /// Hands chunks of a long recording to the engine in bounded groups. The chunks are independent
    /// by construction — each gets a fresh stream — so the strict loop that decoded them one after
    /// another was serializing work the encoder can batch.
    /// </summary>
    private string DecodeBatched(
        IReadOnlyList<GigaAmAudioChunk> chunks,
        IProgress<ModelProgress>? progress,
        CancellationToken cancellationToken)
    {
        var decoded = new List<DecodedAudioChunk>(chunks.Count);

        for (var offset = 0; offset < chunks.Count; offset += MaxBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = Math.Min(MaxBatchSize, chunks.Count - offset);
            progress?.Report(new ModelProgress("Распознаю", offset * 100d / chunks.Count));
            DecodeBatch(chunks, offset, size, decoded, cancellationToken);
        }

        return TranscriptChunkJoiner.Join(decoded);
    }

    private void DecodeBatch(
        IReadOnlyList<GigaAmAudioChunk> chunks,
        int offset,
        int size,
        List<DecodedAudioChunk> decoded,
        CancellationToken cancellationToken)
    {
        var streams = new OfflineStream[size];
        try
        {
            for (var index = 0; index < size; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                streams[index] = _recognizer!.CreateStream();
                streams[index].AcceptWaveform(SampleRate, GetEngineSamples(chunks[offset + index].Samples));
            }

            _recognizer!.Decode(streams);

            for (var index = 0; index < size; index++)
            {
                var text = streams[index].Result.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    decoded.Add(new DecodedAudioChunk(text.Trim(), chunks[offset + index].ParagraphBreakBefore));
                }
            }
        }
        finally
        {
            foreach (var stream in streams)
            {
                stream?.Dispose();
            }
        }
    }

    private static void PrimeRecognizer(OfflineRecognizer recognizer)
    {
        try
        {
            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(SampleRate, new float[SampleRate / 10]);
            recognizer.Decode(stream);
        }
        catch (Exception exception)
        {
            // Priming is an optimization: a failure here must not block dictation.
            AppLog.Write("GigaAM priming pass failed", exception);
        }
    }

    private static float[] GetEngineSamples(ReadOnlyMemory<float> samples)
    {
        if (MemoryMarshal.TryGetArray(samples, out var segment) &&
            segment.Offset == 0 && segment.Count == segment.Array!.Length)
        {
            return segment.Array;
        }

        // Sherpa's managed API currently accepts only float[]. Keep chunk
        // planning zero-copy and materialize one bounded segment at decode time
        // instead of cloning every long-form chunk up front.
        return samples.ToArray();
    }

    private static OfflineRecognizer CreateRecognizer(
        string encoder,
        string decoder,
        string joiner,
        string tokens) => new(new OfflineRecognizerConfig
    {
        FeatConfig = new FeatureConfig { SampleRate = SampleRate, FeatureDim = 64 },
        ModelConfig = new OfflineModelConfig
        {
            Tokens = tokens,

            // Three quarters of the logical cores, not half. The old split assumed GigaAM and
            // Whisper decode simultaneously, which was true when the fallback ran unconditionally;
            // now the primary engine usually has the machine to itself. Two cores are deliberately
            // left for the UI thread and the audio callback — starving those trades a faster decode
            // for a stuttering capsule and dropped audio buffers.
            NumThreads = Math.Clamp(Environment.ProcessorCount * 3 / 4, 2, 12),
            Debug = 0,

            // CPU on purpose. The INT8 encoder runs at roughly 50× real time here, and an RNN-T
            // decoder is autoregressive — it gains far less from a GPU than a CTC model would,
            // while adding a provider that can fail to initialize on some drivers.
            Provider = "cpu",
            Transducer = new OfflineTransducerModelConfig
            {
                Encoder = encoder,
                Decoder = decoder,
                Joiner = joiner
            }
        },
        // Beam search rather than greedy. For a transducer this is the cheapest accuracy available:
        // a few percent relative WER for roughly five to ten percent of the decode time — and the
        // decode already runs at around fifty times real time, so that time is not felt. Four paths
        // is the point where the curve flattens; more costs time without buying anything.
        DecodingMethod = "modified_beam_search",
        MaxActivePaths = 4
    });

    private static IReadOnlyList<ModelDescriptor> GigaDescriptors =>
        [ModelCatalog.GigaAmEncoder, ModelCatalog.GigaAmDecoder, ModelCatalog.GigaAmJoiner, ModelCatalog.GigaAmTokens];

    private static string GetProgressLabel(ModelTransferProgress value) => value.Stage switch
    {
        ModelTransferStage.Verifying => "Проверяю GigaAM…",
        ModelTransferStage.Ready => "GigaAM готова",
        _ => "Загружаю GigaAM…"
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Releasing the native session underneath an in-flight Decode() crashes the process, and
        // Decode() is not cancellable mid-chunk. If the slot cannot be taken, the recognizer is
        // deliberately leaked to process teardown — a leak on exit is strictly better than a
        // native crash, and waiting longer would only move the crash later.
        if (!_decodeLock.Wait(TimeSpan.FromSeconds(2)))
        {
            AppLog.Write("GigaAM decode still running at shutdown; native recognizer left to process teardown");
            return;
        }

        try
        {
            _recognizer?.Dispose();
            _recognizer = null;
        }
        finally
        {
            _decodeLock.Release();
        }

        _decodeLock.Dispose();

        // The initialization lock is only released once warm-up leaves its finally block. If a
        // model download is still in flight, disposing it here would throw inside an unobserved
        // task; leave it to the GC instead.
        if (_initializationLock.CurrentCount == 1)
        {
            _initializationLock.Dispose();
        }

        if (_ownsModelManager)
        {
            _modelManager.Dispose();
        }
    }
}

internal static class AudioSampleReader
{
    internal static float[] ReadMono16Khz(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider provider = reader;
        if (reader.WaveFormat.Channels > 1)
        {
            provider = new StereoToMonoSampleProvider(provider);
        }
        if (provider.WaveFormat.SampleRate != 16_000)
        {
            provider = new WdlResamplingSampleProvider(provider, 16_000);
        }

        var expected = (int)Math.Min(int.MaxValue, Math.Ceiling(reader.TotalTime.TotalSeconds * 16_000) + 1024);
        var writer = new ArrayBufferWriter<float>(Math.Max(1024, expected));
        var buffer = ArrayPool<float>.Shared.Rent(16_000);
        try
        {
            int read;
            while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            {
                buffer.AsSpan(0, read).CopyTo(writer.GetSpan(read));
                writer.Advance(read);
            }
            return writer.WrittenSpan.ToArray();
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }
}

internal sealed record GigaAmAudioChunk(ReadOnlyMemory<float> Samples, bool ParagraphBreakBefore);
internal sealed record DecodedAudioChunk(string Text, bool ParagraphBreakBefore);

internal static class GigaAmAudioChunker
{
    private const int MaxSeconds = 22;
    private const int SearchSeconds = 4;
    private const int MinimumSilenceMilliseconds = 240;
    private const int ParagraphSilenceMilliseconds = 1050;
    private const int OverlapMilliseconds = 240;

    /// <summary>Absolute noise floor (~-40.9 dBFS). The adaptive threshold never drops below it.</summary>
    internal const double MinimumSilenceRms = 0.009;

    /// <summary>
    /// Upper bound (~-30.5 dBFS) for the adaptive threshold. Without it a loud recording would
    /// classify ordinary speech as silence and the splitter would cut mid-word everywhere.
    /// </summary>
    internal const double MaximumSilenceRms = 0.03;

    private const double NoiseFloorPercentile = 0.15;
    private const double NoiseFloorHeadroom = 1.8;

    internal static IReadOnlyList<GigaAmAudioChunk> Split(float[] samples, int sampleRate)
    {
        var maxSamples = MaxSeconds * sampleRate;
        if (samples.Length <= maxSamples)
        {
            return [new GigaAmAudioChunk(samples.AsMemory(), false)];
        }

        var window = Math.Max(1, sampleRate * 20 / 1000);
        var silenceRms = EstimateSilenceThreshold(samples, window);
        var overlap = sampleRate * OverlapMilliseconds / 1000;
        var result = new List<GigaAmAudioChunk>();
        var start = 0;
        var paragraphBefore = false;
        while (start < samples.Length)
        {
            var hardEnd = Math.Min(samples.Length, start + maxSamples);
            var silence = hardEnd == samples.Length
                ? null
                : FindSilenceBoundary(samples, start, hardEnd, sampleRate, window, silenceRms);
            var end = silence?.Boundary ?? hardEnd;
            if (end <= start)
            {
                end = hardEnd;
            }
            result.Add(new GigaAmAudioChunk(samples.AsMemory(start, end - start), paragraphBefore));
            if (end == samples.Length)
            {
                break;
            }
            paragraphBefore = silence?.DurationMilliseconds >= ParagraphSilenceMilliseconds;

            // Overlap applies to every boundary, not only to the hard-cut fallback: a
            // detected pause still sits inside a breath, and the engine loses the word
            // straddling it when the next chunk starts exactly where the previous ended.
            start = Math.Max(start + 1, end - overlap);
        }
        return result;
    }

    /// <summary>
    /// Derives the silence threshold from the recording itself instead of a fixed constant.
    /// A quiet room keeps the absolute floor; a noisy one raises it so pauses are still found.
    /// </summary>
    internal static double EstimateSilenceThreshold(float[] samples, int window)
    {
        var windowCount = samples.Length / window;
        if (windowCount < 8)
        {
            return MinimumSilenceRms;
        }

        var energies = new double[windowCount];
        for (var index = 0; index < windowCount; index++)
        {
            energies[index] = WindowRms(samples, index * window, window);
        }

        Array.Sort(energies);
        var floor = energies[(int)(windowCount * NoiseFloorPercentile)];
        return Math.Clamp(floor * NoiseFloorHeadroom, MinimumSilenceRms, MaximumSilenceRms);
    }

    private static double WindowRms(float[] samples, int offset, int window)
    {
        var sum = 0d;
        for (var index = offset; index < offset + window; index++)
        {
            sum += samples[index] * samples[index];
        }
        return Math.Sqrt(sum / window);
    }

    private static SilenceBoundary? FindSilenceBoundary(
        float[] samples,
        int start,
        int hardEnd,
        int sampleRate,
        int window,
        double silenceRms)
    {
        var searchStart = Math.Max(start + sampleRate, hardEnd - (SearchSeconds * sampleRate));
        var runStart = -1;
        SilenceBoundary? best = null;
        for (var candidate = searchStart; candidate <= hardEnd - window; candidate += window)
        {
            if (WindowRms(samples, candidate, window) <= silenceRms)
            {
                runStart = runStart < 0 ? candidate : runStart;
                continue;
            }
            best = SelectBoundary(runStart, candidate, sampleRate, best);
            runStart = -1;
        }
        return SelectBoundary(runStart, hardEnd, sampleRate, best);
    }

    private static SilenceBoundary? SelectBoundary(int runStart, int runEnd, int sampleRate, SilenceBoundary? current)
    {
        if (runStart < 0)
        {
            return current;
        }
        var durationMs = (runEnd - runStart) * 1000d / sampleRate;
        if (durationMs < MinimumSilenceMilliseconds)
        {
            return current;
        }
        var candidate = new SilenceBoundary(runStart + ((runEnd - runStart) / 2), durationMs);

        // Prefer the longest confirmed pause, not the one closest to the hard limit:
        // a longer pause is a more reliable sentence boundary for the decoder.
        return current is null || candidate.DurationMilliseconds > current.DurationMilliseconds
            ? candidate
            : current;
    }

    private sealed record SilenceBoundary(int Boundary, double DurationMilliseconds);
}

internal static class TranscriptChunkJoiner
{
    private const int MaxOverlapWords = 12;

    internal static string Join(IReadOnlyList<DecodedAudioChunk> chunks)
    {
        var result = new List<string>();
        int? previousChunkWordCount = null;
        foreach (var chunk in chunks)
        {
            var words = chunk.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var overlap = FindBoundaryOverlap(result, previousChunkWordCount, words);
            if (chunk.ParagraphBreakBefore && result.Count > 0)
            {
                result[^1] += Environment.NewLine + Environment.NewLine;
            }
            var appended = words.Length - overlap;
            result.AddRange(words.Skip(overlap));
            previousChunkWordCount = appended;
        }
        return string.Join(" ", result).Replace(Environment.NewLine + Environment.NewLine + " ", Environment.NewLine + Environment.NewLine);
    }

    /// <summary>
    /// Compares the tail of the previous chunk with the head of the next one. Comparison runs on
    /// normalized tokens because GigaAM v3 e2e emits punctuation, which made the previous exact
    /// match fail on every real boundary ("три," != "три"). The window is limited to the previous
    /// chunk so a phrase repeated later in the dictation cannot be swallowed.
    /// </summary>
    private static int FindBoundaryOverlap(
        IReadOnlyList<string> existing,
        int? previousChunkWordCount,
        IReadOnlyList<string> next)
    {
        // null means "no previous chunk yet"; 0 means the previous chunk contributed nothing, in
        // which case there is no boundary to deduplicate against and the window must stay closed
        // rather than silently widening to the whole transcript.
        var available = previousChunkWordCount is { } contributed
            ? Math.Min(contributed, existing.Count)
            : existing.Count;
        var max = Math.Min(MaxOverlapWords, Math.Min(available, next.Count));
        for (var count = max; count > 0; count--)
        {
            var matches = true;
            for (var index = 0; index < count; index++)
            {
                if (!TokensMatch(existing[existing.Count - count + index], next[index]))
                {
                    matches = false;
                    break;
                }
            }
            if (matches) return count;
        }
        return 0;
    }

    private static bool TokensMatch(string left, string right)
    {
        var normalizedLeft = Normalize(left);
        return normalizedLeft.Length > 0 &&
            string.Equals(normalizedLeft, Normalize(right), StringComparison.Ordinal);
    }

    private static string Normalize(string token)
    {
        Span<char> buffer = token.Length <= 64 ? stackalloc char[token.Length] : new char[token.Length];
        var length = 0;
        foreach (var character in token)
        {
            if (!char.IsLetterOrDigit(character))
            {
                continue;
            }
            var lowered = char.ToLowerInvariant(character);
            buffer[length++] = lowered is 'ё' ? 'е' : lowered;
        }
        return new string(buffer[..length]);
    }
}
