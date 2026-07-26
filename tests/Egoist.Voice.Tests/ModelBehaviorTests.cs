using Egoist.Voice.Core;
using Egoist.Voice.Services;
using System.Runtime.InteropServices;

namespace Egoist.Voice.Tests;

public sealed class ModelBehaviorTests
{
    [Theory]
    [InlineData("Распознаю")]
    [InlineData("Распознаю 2/3")]
    [InlineData("  распознаю 48%")]
    public void RecognitionProgressPolicyHidesEngineChunkDetails(string label)
    {
        Assert.False(RecognitionProgressPolicy.ShouldRenderEngineProgress(label));
    }

    [Theory]
    [InlineData("Запускаю GigaAM…")]
    [InlineData("Проверяю модель")]
    [InlineData("Загружаю 42%")]
    public void RecognitionProgressPolicyKeepsModelLifecycleProgress(string label)
    {
        Assert.True(RecognitionProgressPolicy.ShouldRenderEngineProgress(label));
    }

    [Fact]
    public void Requires_gigaam_primary_and_whisper_mixed_language_fallback()
    {
        var models = ModelCatalog.CreateRequiredModels();

        Assert.Equal(5, models.Count);
        Assert.All(models, model => Assert.Equal(ModelKind.Speech, model.Kind));
        Assert.Equal(ModelCatalog.GigaAmEncoder.Id, models[0].Id);
        Assert.Contains(ModelCatalog.GigaAmTokens, models);
        Assert.Equal(ModelCatalog.Whisper.Id, models[^1].Id);
    }

    [Fact]
    public void HybridSelectorKeepsGigaAmForPureRussian()
    {
        var selected = new MixedLanguageTranscriptSelector().Select(
            "У лукоморья дуб зелёный.",
            "У лукоморья дуб зеленый.");

        Assert.Equal("GigaAM", selected.Engine);
        Assert.Contains("зелёный", selected.Text);
    }

    [Fact]
    public void HybridSelectorUsesWhisperWhenItPreservesTechnicalEnglish()
    {
        var selected = new MixedLanguageTranscriptSelector().Select(
            "Сегодня я открыл гит-оп, написал APND на TiperScript и запустил Daker.",
            "Сегодня я открыл GitHub, написал API endpoint на TypeScript и запустил Docker.");

        Assert.Equal("Whisper", selected.Engine);
        Assert.Contains("GitHub", selected.Text);
        Assert.Contains("Docker", selected.Text);
    }

    [Fact]
    public void HybridSelectorUsesWhisperForPredominantlyEnglishSpeech()
    {
        var selected = new MixedLanguageTranscriptSelector().Select(
            "зис из э лонг инглиш сентенс",
            "This is a long English sentence about reliable transcription.");

        Assert.Equal("Whisper", selected.Engine);
    }

    [Fact]
    public void HybridSelectorDoesNotSwitchForOneAccidentalLatinToken()
    {
        var selected = new MixedLanguageTranscriptSelector().Select(
            "Открой проект и запусти сборку.",
            "Открой project и запусти сборку.");

        Assert.Equal("GigaAM", selected.Engine);
    }

    [Fact]
    public void HybridSelectorDoesNotTreatFragmentOfMultiwordTermAsTechnicalEvidence()
    {
        var selected = new MixedLanguageTranscriptSelector().Select(
            "Проверь этот код и запусти сборку.",
            "Проверь этот Code и запусти сборку.");

        Assert.Equal("GigaAM", selected.Engine);
    }

    [Fact]
    public void HybridSelectorRecognizesCompleteMultiwordTechnicalTerm()
    {
        var selected = new MixedLanguageTranscriptSelector().Select(
            "Открой вижуал студио код и запусти проект.",
            "Открой Visual Studio Code и запусти проект.");

        Assert.Equal("Whisper", selected.Engine);
    }

    [Fact]
    public void HybridSelectorPreservesCommonEnglishBrandsInsideRussianSpeech()
    {
        var selected = new MixedLanguageTranscriptSelector().Select(
            "Я сравнил энвидиа с аэмдэ и открыл гугл хром на виндовс.",
            "Я сравнил NVIDIA с AMD и открыл Google Chrome на Windows.");

        Assert.Equal("Whisper", selected.Engine);
        Assert.Contains("NVIDIA", selected.Text);
        Assert.Contains("Google Chrome", selected.Text);
    }

    [Fact]
    public void GigaAmChunkerKeepsAllSamplesAndLimitsChunkLength()
    {
        const int sampleRate = 100;
        var samples = Enumerable.Range(0, 5_500).Select(value => (float)value).ToArray();

        var chunks = GigaAmAudioChunker.Split(samples, sampleRate);

        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Samples.Length, 1, 22 * sampleRate));
        Assert.All(chunks, chunk =>
        {
            Assert.True(MemoryMarshal.TryGetArray(chunk.Samples, out var segment));
            Assert.Same(samples, segment.Array);
        });
    }

    [Fact]
    public void ChunkJoinerRemovesOnlyExactBoundaryDuplication()
    {
        var joined = TranscriptChunkJoiner.Join([
            new DecodedAudioChunk("один два три", false),
            new DecodedAudioChunk("два три четыре", false)]);

        Assert.Equal("один два три четыре", joined);
    }

    [Fact]
    public void ChunkJoinerStartsParagraphOnlyAfterConfirmedLongPause()
    {
        var joined = TranscriptChunkJoiner.Join([
            new DecodedAudioChunk("первая мысль", false),
            new DecodedAudioChunk("вторая мысль", true)]);

        Assert.Equal($"первая мысль{Environment.NewLine}{Environment.NewLine}вторая мысль", joined);
    }

    [Fact]
    public void Formats_compact_and_detailed_progress()
    {
        var progress = new ModelTransferProgress(
            "Whisper Large v3 Turbo", 1, 1, ModelTransferStage.Downloading,
            245_000_000, 574_041_195, 42.7, 42.7, 38_000_000, TimeSpan.FromSeconds(9));

        Assert.Contains("43%", ModelProgressFormatter.Capsule(progress));
        Assert.Contains("38 МБ/с", ModelProgressFormatter.Detail(progress));
        Assert.Contains("~0:09", ModelProgressFormatter.Detail(progress));
        Assert.True(ModelProgressFormatter.TrayTooltip(progress).Length <= 63);
    }
}
