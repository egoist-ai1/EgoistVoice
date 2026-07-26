using System.Runtime.InteropServices;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

/// <summary>
/// Covers the phase-0 corrections to segmentation, chunk joining and candidate selection.
/// Each test states the defect it pins down, because every one of them passed silently before.
/// </summary>
public sealed class TranscriptionPipelineTests
{
    private const int SampleRate = 100;
    private const int MaxChunkSamples = 22 * SampleRate;

    [Fact]
    public void Chunker_overlaps_every_boundary_including_detected_pauses()
    {
        // Previously overlap applied only to the hard-cut fallback: when a pause was found the
        // next chunk started exactly where the previous ended, and the engine lost the word
        // straddling the boundary.
        var samples = LoudSignal(5_000);
        Silence(samples, 1_900, 100);

        var chunks = GigaAmAudioChunker.Split(samples, SampleRate);

        Assert.True(chunks.Count >= 2);
        var first = SegmentOf(chunks[0]);
        var second = SegmentOf(chunks[1]);
        Assert.True(
            second.Offset < first.Offset + first.Count,
            $"Второй фрагмент должен перекрывать первый: начало {second.Offset}, конец первого {first.Offset + first.Count}.");
    }

    [Fact]
    public void Chunker_prefers_the_longest_pause_over_the_rightmost_one()
    {
        // The old comparison kept whichever candidate sat closest to the 22-second limit, so a
        // short breath won over a real sentence break earlier in the window.
        var samples = LoudSignal(5_000);
        Silence(samples, 1_850, 150);
        Silence(samples, 2_100, 40);

        var chunks = GigaAmAudioChunker.Split(samples, SampleRate);
        var first = SegmentOf(chunks[0]);

        Assert.InRange(first.Count, 1_850, 2_000);
    }

    [Fact]
    public void Silence_threshold_keeps_the_absolute_floor_in_a_quiet_recording()
    {
        var samples = new float[1_000];

        Assert.Equal(
            GigaAmAudioChunker.MinimumSilenceRms,
            GigaAmAudioChunker.EstimateSilenceThreshold(samples, 2),
            precision: 6);
    }

    [Fact]
    public void Silence_threshold_rises_with_the_noise_floor_but_never_swallows_speech()
    {
        var quiet = Constant(1_000, 0.01f);
        var noisy = Constant(1_000, 0.2f);

        var adapted = GigaAmAudioChunker.EstimateSilenceThreshold(quiet, 2);
        var capped = GigaAmAudioChunker.EstimateSilenceThreshold(noisy, 2);

        Assert.True(adapted > GigaAmAudioChunker.MinimumSilenceRms, "Порог должен подниматься над абсолютным полом.");
        Assert.True(adapted < GigaAmAudioChunker.MaximumSilenceRms, "Тихая запись не должна упираться в потолок.");
        Assert.Equal(GigaAmAudioChunker.MaximumSilenceRms, capped, precision: 6);
    }

    [Fact]
    public void Chunk_joiner_deduplicates_boundaries_that_carry_punctuation()
    {
        // GigaAM v3 e2e emits punctuation, so the previous ordinal comparison never matched on a
        // real boundary ("три," != "три") and every long dictation kept duplicated words.
        var joined = TranscriptChunkJoiner.Join([
            new DecodedAudioChunk("раз два три,", false),
            new DecodedAudioChunk("Три, четыре пять", false)]);

        Assert.Equal("раз два три, четыре пять", joined);
    }

    [Fact]
    public void Chunk_joiner_keeps_a_phrase_the_speaker_actually_repeated()
    {
        // The overlap search used to run against the whole accumulated transcript, so a genuine
        // repetition matched a suffix from two chunks back and was deleted as a duplicate.
        var joined = TranscriptChunkJoiner.Join([
            new DecodedAudioChunk("привет мир друг", false),
            new DecodedAudioChunk("друг новый", false),
            new DecodedAudioChunk("мир друг новый", false)]);

        Assert.Equal("привет мир друг новый мир друг новый", joined);
    }

    [Fact]
    public void Selector_rejects_a_whisper_run_that_loops_on_itself()
    {
        var selected = new MixedLanguageTranscriptSelector().Select(
            "Продолжение следует.",
            "Продолжение следует далее. Продолжение следует далее. Продолжение следует далее.");

        Assert.Equal("GigaAM", selected.Engine);
    }

    [Fact]
    public void Selector_rejects_english_output_several_times_longer_than_the_russian_one()
    {
        // credibleEnglish used to bypass the length check entirely, so runaway decoding won
        // unconditionally as long as it contained enough Latin letters.
        var selected = new MixedLanguageTranscriptSelector().Select(
            "Привет.",
            "Hello there my friend this is a very long hallucinated English sentence that never happened.");

        Assert.Equal("GigaAM", selected.Engine);
    }

    [Fact]
    public void Chunk_joiner_closes_the_window_after_a_fully_duplicated_chunk()
    {
        // A chunk that contributes nothing leaves no boundary to match against. Treating that as
        // "no limit" would quietly restore the whole-transcript search this fix removed.
        var joined = TranscriptChunkJoiner.Join([
            new DecodedAudioChunk("альфа бета гамма", false),
            new DecodedAudioChunk("бета гамма", false),
            new DecodedAudioChunk("альфа бета гамма", false)]);

        Assert.Equal("альфа бета гамма альфа бета гамма", joined);
    }

    [Fact]
    public void Foreground_description_resolves_the_current_process_without_module_enumeration()
    {
        var described = GameForegroundPolicy.Describe((uint)Environment.ProcessId);

        Assert.False(string.IsNullOrWhiteSpace(described.ProcessName));
        Assert.False(string.IsNullOrWhiteSpace(described.ExecutablePath));
        Assert.EndsWith(".exe", described.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Foreground_description_survives_a_process_id_that_no_longer_exists()
    {
        var described = GameForegroundPolicy.Describe(uint.MaxValue - 4);

        Assert.Equal(string.Empty, described.ProcessName);
        Assert.False(described.IsGame);
    }

    [Fact]
    public void Gigaam_service_dispose_is_idempotent()
    {
        var service = new GigaAmTranscriptionService(new IdleModelManager());

        service.Dispose();
        service.Dispose();
    }

    [Theory]
    [InlineData("один два три четыре пять шесть семь восемь девять", false)]
    [InlineData("спасибо за внимание спасибо за внимание спасибо за внимание спасибо за внимание", true)]
    [InlineData("короткая фраза", false)]
    public void Repeated_ngram_detector_fires_only_on_consecutive_repetition(string text, bool expected) =>
        Assert.Equal(expected, MixedLanguageTranscriptSelector.HasRepeatedNgram(text));

    private static float[] LoudSignal(int length) => Constant(length, 0.5f);

    private static float[] Constant(int length, float amplitude)
    {
        var samples = new float[length];
        for (var index = 0; index < length; index++)
        {
            samples[index] = index % 2 == 0 ? amplitude : -amplitude;
        }
        return samples;
    }

    private static void Silence(float[] samples, int offset, int length) =>
        samples.AsSpan(offset, length).Clear();

    private static ArraySegment<float> SegmentOf(GigaAmAudioChunk chunk)
    {
        Assert.True(MemoryMarshal.TryGetArray(chunk.Samples, out var segment));
        Assert.InRange(segment.Count, 1, MaxChunkSamples);
        return segment;
    }

    /// <summary>A manager that is never asked for anything: the test only exercises teardown.</summary>
    private sealed class IdleModelManager : IModelManager
    {
        public event EventHandler<ModelTransferProgress>? ProgressChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<ModelDescriptor> RequiredModels => [];
        public bool AreAllModelsReady => true;
        public ModelTransferProgress? CurrentProgress => null;

        public Task<string> EnsureModelAsync(
            ModelDescriptor descriptor,
            IProgress<ModelTransferProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DownloadRequiredModelsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
