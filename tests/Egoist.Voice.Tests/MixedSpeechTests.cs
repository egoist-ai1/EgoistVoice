using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class MixedSpeechTests
{
    [Theory]
    [InlineData("Проверь пожалуйста последний отчёт и пришли его вечером.")]
    [InlineData("Встреча перенесена на четверг, всё в силе.")]
    [InlineData("")]
    public void Pure_russian_does_not_trigger_the_fallback(string transcript) =>
        Assert.False(new MixedSpeechDetector().Inspect(transcript, mixedModeRequested: false).NeedsFallback);

    [Fact]
    public void Latin_script_in_the_primary_output_triggers_the_fallback()
    {
        var decision = new MixedSpeechDetector().Inspect("Открой GitHub и запусти сборку.", false);

        Assert.Equal(MixedSpeechTrigger.LatinScript, decision.Trigger);
    }

    [Theory]
    [InlineData("Открой гитхаб и посмотри коммиты.")]
    [InlineData("Спроси у джемини про эту задачу.")]
    [InlineData("Запусти докер и проверь бэкенд.")]
    public void Fully_russified_terms_still_trigger_the_fallback(string transcript)
    {
        // This is the case the whole suspicion map exists for: the primary engine swallowed the
        // term completely, so a plain script check would see ordinary Russian and skip refinement.
        var decision = new MixedSpeechDetector().Inspect(transcript, false);

        Assert.Equal(MixedSpeechTrigger.RussifiedTerm, decision.Trigger);
        Assert.False(string.IsNullOrWhiteSpace(decision.Evidence));
    }

    [Fact]
    public void Russified_forms_match_inflected_endings()
    {
        var detector = new MixedSpeechDetector();

        Assert.True(detector.Inspect("Разверни это в докере на выходных.", false).NeedsFallback);
        Assert.True(detector.Inspect("Посмотри в гитхабе последний тег.", false).NeedsFallback);
    }

    [Fact]
    public void Explicit_mode_bypasses_detection_entirely()
    {
        var decision = new MixedSpeechDetector().Inspect("Совершенно обычная фраза.", mixedModeRequested: true);

        Assert.Equal(MixedSpeechTrigger.Requested, decision.Trigger);
    }

    [Fact]
    public void Vocabulary_derived_forms_extend_the_suspicion_map()
    {
        var plain = new MixedSpeechDetector();
        var extended = new MixedSpeechDetector(
            MixedSpeechDetector.DeriveRussifiedForms(["кубернетес", "графана"]));

        Assert.False(plain.Inspect("Разверни кубернетес на стенде.", false).NeedsFallback);
        Assert.True(extended.Inspect("Разверни кубернетес на стенде.", false).NeedsFallback);
    }

    [Theory]
    [InlineData("Docker")]
    [InlineData("к")]
    [InlineData("")]
    public void Derived_forms_reject_latin_and_too_short_entries(string spoken) =>
        Assert.Empty(MixedSpeechDetector.DeriveRussifiedForms([spoken]));

    [Fact]
    public async Task Fallback_is_skipped_for_pure_russian_speech()
    {
        var whisper = new CountingEngine("Whisper", "не должно понадобиться");
        using var service = CreateService(new CountingEngine("GigaAM", "Отправь письмо завтра утром."), whisper);
        await service.WarmUpAsync(null, CancellationToken.None);

        var result = await service.TranscribeAsync("audio.wav", null, CancellationToken.None);

        Assert.Equal("Отправь письмо завтра утром.", result.Text);
        Assert.Equal(0, whisper.TranscribeCalls);
    }

    [Fact]
    public async Task Fallback_runs_when_the_primary_output_shows_latin_script()
    {
        var whisper = new CountingEngine("Whisper", "Открой GitHub и запусти Docker.");
        using var service = CreateService(new CountingEngine("GigaAM", "Открой Github и запусти Doker."), whisper);
        await service.WarmUpAsync(null, CancellationToken.None);
        await WaitForWarmUpAsync(whisper);

        await service.TranscribeAsync("audio.wav", null, CancellationToken.None);

        Assert.Equal(1, whisper.TranscribeCalls);
    }

    [Fact]
    public async Task Explicit_mixed_mode_forces_the_fallback_for_plain_russian()
    {
        var whisper = new CountingEngine("Whisper", "Совершенно обычная фраза.");
        using var service = CreateService(new CountingEngine("GigaAM", "Совершенно обычная фраза."), whisper);
        service.MixedLanguageMode = true;
        await service.WarmUpAsync(null, CancellationToken.None);
        await WaitForWarmUpAsync(whisper);

        await service.TranscribeAsync("audio.wav", null, CancellationToken.None);

        Assert.Equal(1, whisper.TranscribeCalls);
    }

    [Fact]
    public async Task Refining_progress_is_reported_before_the_fallback_starts()
    {
        var whisper = new CountingEngine("Whisper", "Открой GitHub.");
        using var service = CreateService(new CountingEngine("GigaAM", "Открой Github."), whisper);
        await service.WarmUpAsync(null, CancellationToken.None);
        await WaitForWarmUpAsync(whisper);

        var labels = new List<string>();
        var progress = new Progress<ModelProgress>(value =>
        {
            lock (labels)
            {
                labels.Add(value.Label);
            }
        });

        await service.TranscribeAsync("audio.wav", progress, CancellationToken.None);
        await Task.Delay(50);

        lock (labels)
        {
            Assert.Contains(HybridTranscriptionService.RefiningLabel, labels);
        }
    }

    private static async Task WaitForWarmUpAsync(CountingEngine whisper)
    {
        for (var attempt = 0; attempt < 100 && whisper.WarmUpCalls == 0; attempt++)
        {
            await Task.Delay(10);
        }
    }

    private static HybridTranscriptionService CreateService(
        ITranscriptionEngine giga,
        ITranscriptionEngine whisper) =>
        new(new ReadyModelManager(), giga, whisper, new MixedLanguageTranscriptSelector());

    private sealed class CountingEngine(string name, string text) : ITranscriptionEngine
    {
        private int _transcribeCalls;
        private int _warmUpCalls;

        public string EngineName => name;
        public int TranscribeCalls => Volatile.Read(ref _transcribeCalls);
        public int WarmUpCalls => Volatile.Read(ref _warmUpCalls);

        public Task WarmUpAsync(IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _warmUpCalls);
            return Task.CompletedTask;
        }

        public Task<TranscriptionResult> TranscribeAsync(
            string audioPath,
            IProgress<ModelProgress>? progress,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _transcribeCalls);
            return Task.FromResult(new TranscriptionResult(text, TimeSpan.FromMilliseconds(50)));
        }

        public void Dispose() { }
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

        public Task<string> EnsureModelAsync(
            ModelDescriptor descriptor,
            IProgress<ModelTransferProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DownloadRequiredModelsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
