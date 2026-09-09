using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class HybridTranscriptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fast_russian_dictation_does_not_warm_unused_whisper(bool memoryAudio)
    {
        var whisper = new FakeEngine("Whisper", Result("unused"));
        using var service = CreateService(new FakeEngine("GigaAM", Result("готово")), whisper);
        service.FastModeNoWhisperRefinement = true;
        await service.WarmUpAsync(null, CancellationToken.None);

        var result = memoryAudio
            ? await service.TranscribeSamplesAsync([0.01f, -0.01f], 16_000, CancellationToken.None)
            : await service.TranscribeAsync("synthetic.wav", null, CancellationToken.None);
        await Task.Delay(20); // Give any accidentally scheduled background warmup a chance to run.

        Assert.Equal("готово", result.Text);
        Assert.Equal(0, whisper.WarmUpCalls);
        Assert.Equal(0, whisper.TranscribeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fast_mode_still_loads_fallback_on_primary_failure(bool memoryAudio)
    {
        var whisper = new FakeEngine("Whisper", Result("восстановлено"));
        using var service = CreateService(new FakeEngine("GigaAM", error: new InvalidOperationException("synthetic")), whisper);
        service.FastModeNoWhisperRefinement = true;
        var samples = new float[] { 0.01f, -0.01f };

        var result = memoryAudio
            ? await service.TranscribeSamplesAsync(samples, 16_000, CancellationToken.None)
            : await service.TranscribeAsync("synthetic.wav", null, CancellationToken.None);

        Assert.Equal("восстановлено", result.Text);
        Assert.Equal(1, whisper.WarmUpCalls);
        Assert.Equal(1, whisper.TranscribeCalls);
        if (memoryAudio) Assert.Same(samples, whisper.LastSamples);
    }

    [Fact]
    public async Task Explicit_mixed_mode_keeps_whisper_warmup_even_if_fast_flag_is_set()
    {
        var whisper = new FakeEngine("Whisper", Result("готово"));
        using var service = CreateService(new FakeEngine("GigaAM", Result("готово")), whisper);
        service.FastModeNoWhisperRefinement = true;
        await service.WarmUpAsync(null, CancellationToken.None);
        service.MixedLanguageMode = true;

        await service.WarmUpAsync(null, CancellationToken.None);
        await whisper.WarmUpStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, whisper.WarmUpCalls);
    }

    [Fact]
    public async Task Returns_gigaam_when_whisper_transcription_fails()
    {
        using var service = CreateService(
            new FakeEngine("GigaAM", Result("русский текст")),
            new FakeEngine("Whisper", error: new InvalidOperationException("native failure")));

        var result = await service.TranscribeAsync("audio.wav", null, CancellationToken.None);

        Assert.Equal("русский текст", result.Text);
    }

    [Fact]
    public async Task Returns_whisper_when_gigaam_transcription_fails()
    {
        using var service = CreateService(
            new FakeEngine("GigaAM", error: new InvalidOperationException("onnx failure")),
            new FakeEngine("Whisper", Result("mixed English text")));

        var result = await service.TranscribeAsync("audio.wav", null, CancellationToken.None);

        Assert.Equal("mixed English text", result.Text);
    }

    [Fact]
    public async Task Reports_failure_only_when_both_engines_fail()
    {
        using var service = CreateService(
            new FakeEngine("GigaAM", error: new InvalidOperationException("giga")),
            new FakeEngine("Whisper", error: new InvalidOperationException("whisper")));

        var error = await Assert.ThrowsAsync<AggregateException>(() =>
            service.TranscribeAsync("audio.wav", null, CancellationToken.None));

        Assert.Equal(2, error.InnerExceptions.Count);
    }

    [Fact]
    public async Task Propagates_cancellation_without_converting_it_to_engine_failure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var service = CreateService(
            new FakeEngine("GigaAM", Result("unused")),
            new FakeEngine("Whisper", Result("unused")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TranscribeAsync("audio.wav", null, cancellation.Token));
    }

    [Fact]
    public async Task Benchmark_observation_exposes_raw_candidates_without_changing_selection()
    {
        using var service = CreateService(
            new FakeEngine("GigaAM", Result("Открой гитхаб.")),
            new FakeEngine("Whisper", Result("Открой GitHub.")));
        await service.WarmUpAsync(null, CancellationToken.None);
        await Task.Delay(20);

        var observation = await service.TranscribeObservedAsync("audio.wav", CancellationToken.None);

        Assert.Equal("Открой гитхаб.", observation.Primary!.Text);
        Assert.Equal("Открой GitHub.", observation.Fallback!.Text);
        Assert.Equal(observation.Result.Text, observation.SelectedEngine == "Whisper"
            ? observation.Fallback.Text
            : observation.Primary.Text);
        Assert.Equal(MixedSpeechTrigger.RussifiedTerm, observation.FallbackTrigger);
        Assert.True(observation.FallbackRan);
    }

    [Fact]
    public async Task Dispose_cancels_background_whisper_warmup_before_releasing_engine()
    {
        var whisper = new BlockingWarmupEngine();
        var service = CreateService(new FakeEngine("GigaAM", Result("готово")), whisper);
        await service.WarmUpAsync(null, CancellationToken.None);
        await whisper.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        service.Dispose();

        Assert.True(await whisper.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(whisper.Disposed);
    }

    [Fact]
    public async Task Dispose_defers_native_release_when_warmup_has_not_stopped_yet()
    {
        var whisper = new SlowStoppingWarmupEngine();
        var service = CreateService(new FakeEngine("GigaAM", Result("готово")), whisper);
        await service.WarmUpAsync(null, CancellationToken.None);
        await whisper.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        service.Dispose();

        Assert.False(whisper.Disposed);
        whisper.Release.TrySetResult(true);
        await whisper.Finished.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);
        Assert.True(whisper.Disposed);
    }

    private static HybridTranscriptionService CreateService(ITranscriptionEngine giga, ITranscriptionEngine whisper) =>
        new(new ReadyModelManager(), giga, whisper, new MixedLanguageTranscriptSelector());

    private static TranscriptionResult Result(string text) => new(text, TimeSpan.FromMilliseconds(100));

    private sealed class FakeEngine(
        string name,
        TranscriptionResult? result = null,
        Exception? error = null) : ITranscriptionEngine, ISampleTranscriptionService
    {
        public string EngineName => name;
        internal int WarmUpCalls;
        internal int TranscribeCalls;
        internal float[]? LastSamples;
        internal TaskCompletionSource<bool> WarmUpStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WarmUpAsync(IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref WarmUpCalls);
            WarmUpStarted.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task<TranscriptionResult> TranscribeSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken)
        {
            LastSamples = samples;
            return TranscribeAsync("synthetic.wav", null, cancellationToken);
        }

        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath,
            IProgress<ModelProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref TranscribeCalls);
            return error is null
                ? Task.FromResult(result!)
                : Task.FromException<TranscriptionResult>(error);
        }

        public void Dispose() { }
    }

    private sealed class BlockingWarmupEngine : ITranscriptionEngine
    {
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }
        public string EngineName => "Whisper";

        public async Task WarmUpAsync(IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult(true);
                throw;
            }
        }

        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath,
            IProgress<ModelProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Dispose() => Disposed = true;
    }

    private sealed class SlowStoppingWarmupEngine : ITranscriptionEngine
    {
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }
        public string EngineName => "Whisper";

        public async Task WarmUpAsync(IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Release.Task;
            Finished.TrySetResult(true);
        }

        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath,
            IProgress<ModelProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Dispose() => Disposed = true;
    }

    private sealed class ReadyModelManager : IModelManager
    {
        public event EventHandler<ModelTransferProgress>? ProgressChanged
        {
            add { }
            remove { }
        }
        public IReadOnlyList<ModelDescriptor> RequiredModels => [];
        public bool AreAllModelsReady => true;
        public ModelTransferProgress? CurrentProgress => null;
        public Task<string> EnsureModelAsync(ModelDescriptor descriptor, IProgress<ModelTransferProgress>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DownloadRequiredModelsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
