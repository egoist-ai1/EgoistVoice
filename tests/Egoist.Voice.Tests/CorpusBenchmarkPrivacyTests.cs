using System.IO;
using System.Text;
using System.Text.Json;
using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class CorpusBenchmarkPrivacyTests
{
    [Fact]
    public void Saved_report_contains_metrics_but_not_reference_hypothesis_or_paths()
    {
        const string referenceCanary = "REFERENCE-CANARY-CONTENT";
        const string hypothesisCanary = "HYPOTHESIS-CANARY-CONTENT C:\\Users\\Private\\voice.wav";
        var report = CorpusBenchmark.Summarize(
            "privacy-test",
            [new BenchmarkEntry("ru-clean/001", "ru-clean", referenceCanary, hypothesisCanary, 12, 14)]);
        var directory = TemporaryDirectory();
        var path = Path.Combine(directory, "report.json");
        try
        {
            CorpusBenchmark.Save(report, path);
            var json = File.ReadAllText(path);

            Assert.DoesNotContain(referenceCanary, json, StringComparison.Ordinal);
            Assert.DoesNotContain(hypothesisCanary, json, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\Users", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("aggregate-only-no-transcript", json, StringComparison.Ordinal);
            Assert.Contains("wordErrors", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Summary_reports_entity_split_command_punctuation_and_boundary_metrics()
    {
        var report = CorpusBenchmark.Summarize(
            "metric-test",
            [
                new BenchmarkEntry(
                    "translate-positive/001",
                    "translate-positive",
                    "Переведи на английский: Anthropic.",
                    "Переведи на английский: Anthropic.",
                    20,
                    25,
                    ExpectedEntities: ["Anthropic"],
                    TranslationCommandExpected: true),
                new BenchmarkEntry(
                    "translate-negative/001",
                    "translate-negative",
                    "Я закончил перевод вчера.",
                    "Я закончил перевод вчера.",
                    18,
                    22,
                    TranslationCommandExpected: false),
                new BenchmarkEntry(
                    "ru-en/001",
                    "ru-en",
                    "Anthropic готов.",
                    "Anth ropic готов.",
                    16,
                    20,
                    ExpectedEntities: ["Anthropic"]),
                new BenchmarkEntry(
                    "boundary-start/001",
                    "boundary-start",
                    "Шёпот слышен.",
                    "Шёпот слышен.",
                    15,
                    19,
                    Boundary: "start",
                    BoundaryTarget: "Шёпот")
            ]);

        Assert.Equal(2, report.EntitiesExpected);
        Assert.Equal(0.5, report.EntityAccuracy, precision: 6);
        Assert.Equal(1, report.SplitErrors);
        Assert.Equal(1, report.CommandPrecision, precision: 6);
        Assert.Equal(1, report.CommandRecall, precision: 6);
        Assert.Equal(1, report.BoundaryAccuracy, precision: 6);
        Assert.True(report.PunctuationF1 > 0.9);
    }

    [Fact]
    public void Repeated_fixture_summary_is_byte_stable_after_explicit_runtime_fields_are_fixed()
    {
        var entries = new[]
        {
            new BenchmarkEntry(
                "ru-clean/001",
                "ru-clean",
                "Проверка, один.",
                "Проверка, один.",
                12,
                15)
        };
        var first = CorpusBenchmark.Summarize("stable-fixture", entries) with
        {
            GeneratedUtc = DateTime.UnixEpoch
        };
        var second = CorpusBenchmark.Summarize("stable-fixture", entries) with
        {
            GeneratedUtc = DateTime.UnixEpoch
        };
        var directory = TemporaryDirectory();
        try
        {
            var firstPath = Path.Combine(directory, "first.json");
            var secondPath = Path.Combine(directory, "second.json");
            CorpusBenchmark.Save(first, firstPath);
            CorpusBenchmark.Save(second, secondPath);

            Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Captured_parameters_pin_the_current_offline_hybrid_pipeline()
    {
        var parameters = CorpusBenchmark.CaptureParameters();

        Assert.Equal("HybridTranscriptionService", parameters.Pipeline);
        Assert.Equal(16_000, parameters.InputSampleRateHz);
        Assert.True(parameters.GigaAmThreads > 0);
        Assert.False(parameters.GigaAmContextualBias);
        Assert.Null(parameters.GigaAmHotwordVersion);
        Assert.Null(parameters.GigaAmHotwordScore);
        Assert.True(parameters.WhisperThreads > 0);
        Assert.True(parameters.WhisperLanguageDetection);
        Assert.True(parameters.WhisperNoContext);
        Assert.False(parameters.MixedLanguageMode);
        Assert.Equal(BuiltInVocabulary.Version, parameters.EntityCatalogVersion);
        Assert.Equal("target-and-utterance/v1", parameters.EntityProfilePolicy);
        Assert.False(parameters.ModelDownloadAllowed);

        var hotwords = CorpusBenchmark.CaptureParameters(enableContextualBias: true);
        Assert.True(hotwords.GigaAmContextualBias);
        Assert.Equal(GigaAmHotwordResources.Version, hotwords.GigaAmHotwordVersion);
        Assert.Equal(GigaAmHotwordResources.GlobalScore, hotwords.GigaAmHotwordScore);
    }

    [Fact]
    public void Complete_private_corpus_gets_a_path_independent_content_hash()
    {
        var script = CorpusScript.Parse(
        [
            """{"kind":"schema","version":2,"privacy":"private-local-only"}""",
            """{"kind":"set","set":"ru-clean","title":"Обычная","hint":"","expectedCount":1}""",
            """{"kind":"line","id":"ru-clean/001","text":"Проверка","tags":["clean"]}"""
        ]);
        var first = TemporaryDirectory();
        var second = TemporaryDirectory();
        try
        {
            PrepareCorpus(first, script, fill: 7);
            PrepareCorpus(second, script, fill: 7);

            var firstInventory = CorpusBenchmark.ValidateAndFingerprint(
                first, script, CorpusBenchmark.LoadReferenceDocument(first));
            var secondInventory = CorpusBenchmark.ValidateAndFingerprint(
                second, script, CorpusBenchmark.LoadReferenceDocument(second));

            Assert.Equal(firstInventory.Sha256, secondInventory.Sha256);
            Assert.Equal(1, firstInventory.Clips);
            Assert.Equal(64, firstInventory.AudioBytes);

            File.WriteAllBytes(Path.Combine(second, "ru-clean", "001.wav"), Enumerable.Repeat((byte)8, 64).ToArray());
            var changed = CorpusBenchmark.ValidateAndFingerprint(
                second, script, CorpusBenchmark.LoadReferenceDocument(second));
            Assert.NotEqual(firstInventory.Sha256, changed.Sha256);
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void Reference_loader_rejects_audio_path_escape()
    {
        var directory = TemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, CorpusBenchmark.ReferenceFileName),
                """{"id":"ru-clean/001","audio":"../private.wav","text":"Проверка","tags":["clean"]}""");

            var exception = Assert.Throws<InvalidDataException>(() =>
                CorpusBenchmark.LoadReferenceDocument(directory));
            Assert.Contains("audio path", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void PrepareCorpus(string directory, CorpusScript script, byte fill)
    {
        File.WriteAllText(
            Path.Combine(directory, CorpusBenchmark.ReferenceFileName),
            script.BuildReference(_ => true),
            new UTF8Encoding(true));
        var audio = Path.Combine(directory, "ru-clean", "001.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(audio)!);
        File.WriteAllBytes(audio, Enumerable.Repeat(fill, 64).ToArray());
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "egoist-corpus-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
