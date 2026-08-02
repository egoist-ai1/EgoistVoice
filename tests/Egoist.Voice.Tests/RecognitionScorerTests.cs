using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class RecognitionScorerTests
{
    [Fact]
    public void Identical_transcripts_score_zero()
    {
        var score = RecognitionScorer.Score("привет как дела", "привет как дела");

        Assert.Equal(0, score.WordErrors);
        Assert.Equal(0, score.CharacterErrors);
        Assert.Equal(3, score.ReferenceWords);
    }

    [Fact]
    public void Punctuation_case_and_yo_do_not_count_as_errors_by_default()
    {
        // The engine's punctuation and capitalization are a formatting question, not a recognition
        // one. Scoring them would make every well-punctuated transcript look worse than a bare one.
        var score = RecognitionScorer.Score(
            "Проверь, пожалуйста, зелёный отчёт.",
            "проверь пожалуйста зеленый отчет");

        Assert.Equal(0, score.WordErrors);
    }

    [Theory]
    [InlineData("один два три", "один два", 1)]
    [InlineData("один два три", "один два три четыре", 1)]
    [InlineData("один два три", "один пять три", 1)]
    [InlineData("один два три", "", 3)]
    [InlineData("", "один два три", 3)]
    public void Word_errors_count_insertions_deletions_and_substitutions(
        string reference,
        string hypothesis,
        int expected) =>
        Assert.Equal(expected, RecognitionScorer.Score(reference, hypothesis).WordErrors);

    [Fact]
    public void Digit_expansion_makes_spoken_and_written_numbers_comparable()
    {
        var options = ScoringOptions.Default with { ExpandDigits = true };

        var score = RecognitionScorer.Score("встреча в пять", "встреча в 5", options);

        Assert.Equal(0, score.WordErrors);
    }

    [Fact]
    public void Aggregate_weights_by_length_not_by_clip()
    {
        // Averaging per-clip rates would let a three-word clip outweigh a two-minute one.
        var longClip = new RecognitionScore(100, 5, 500, 20);
        var shortClip = new RecognitionScore(2, 2, 10, 10);

        var total = RecognitionScorer.Aggregate([longClip, shortClip]);

        Assert.Equal(102, total.ReferenceWords);
        Assert.Equal(7, total.WordErrors);
        Assert.Equal(7 / 102d, total.WordErrorRate, precision: 6);
    }

    [Fact]
    public void Percentiles_use_nearest_rank()
    {
        var samples = Enumerable.Range(1, 100)
            .Select(value => TimeSpan.FromMilliseconds(value))
            .ToArray();

        Assert.Equal(50, LatencyStatistics.Median(samples).TotalMilliseconds);
        Assert.Equal(95, LatencyStatistics.Percentile(samples, 0.95).TotalMilliseconds);
        Assert.Equal(TimeSpan.Zero, LatencyStatistics.Percentile([], 0.95));
    }

    [Fact]
    public void Trace_reports_only_the_time_after_the_trigger_is_released()
    {
        var trace = new DictationTrace();
        trace.Mark(DictationStage.CaptureStarted);
        Thread.Sleep(15);
        trace.Mark(DictationStage.CaptureStopped);
        Thread.Sleep(15);
        trace.Mark(DictationStage.Delivered);

        Assert.True(trace.PerceivedLatency < trace.Total, "Воспринимаемая задержка не включает время записи.");
        Assert.True(trace.PerceivedLatency > TimeSpan.Zero);
        Assert.Equal(3, trace.Segments().Count);
        Assert.Contains("perceived=", trace.Format(), StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_comparison_reports_every_breach_at_once()
    {
        var baseline = Report(wer: 0.08, p95: 400, ("ru-clean", 0.05), ("ru-en-mixed", 0.12));
        var candidate = Report(wer: 0.10, p95: 600, ("ru-clean", 0.05), ("ru-en-mixed", 0.20));

        var breaches = CorpusBenchmark.CompareToBaseline(baseline, candidate);

        Assert.Equal(3, breaches.Count);
        Assert.Contains(breaches, breach => breach.Contains("ru-en-mixed", StringComparison.Ordinal));
        Assert.Contains(breaches, breach => breach.Contains("p95", StringComparison.Ordinal));
    }

    [Fact]
    public void Baseline_comparison_passes_on_improvement()
    {
        var baseline = Report(wer: 0.10, p95: 600, ("ru-clean", 0.10));
        var candidate = Report(wer: 0.07, p95: 300, ("ru-clean", 0.06));

        Assert.Empty(CorpusBenchmark.CompareToBaseline(baseline, candidate));
    }

    [Fact]
    public void Baseline_comparison_flags_a_disappearing_set()
    {
        var baseline = Report(wer: 0.08, p95: 400, ("ru-clean", 0.05), ("ru-numbers", 0.09));
        var candidate = Report(wer: 0.08, p95: 400, ("ru-clean", 0.05));

        var breaches = CorpusBenchmark.CompareToBaseline(baseline, candidate);

        Assert.Single(breaches);
        Assert.Contains("ru-numbers", breaches[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_comparison_rejects_missing_or_different_corpus_fingerprint()
    {
        var frozen = Report(wer: 0.08, p95: 400, ("ru-clean", 0.05));
        var missing = frozen with { Corpus = null };
        var different = frozen with
        {
            Corpus = frozen.Corpus! with { Sha256 = new string('b', 64) }
        };

        Assert.Contains(
            CorpusBenchmark.CompareToBaseline(frozen, missing),
            breach => breach.Contains("отсутствует", StringComparison.Ordinal));
        Assert.Contains(
            CorpusBenchmark.CompareToBaseline(frozen, different),
            breach => breach.Contains("не совпадает", StringComparison.Ordinal));
    }

    [Fact]
    public void Summary_groups_by_corpus_set_and_skips_failed_clips()
    {
        var entries = new[]
        {
            new BenchmarkEntry("ru-clean/001", "ru-clean", "один два три", "один два три", 100, 120),
            new BenchmarkEntry("ru-clean/002", "ru-clean", "четыре пять", "четыре шесть", 200, 220),
            new BenchmarkEntry("ru-noisy/001", "ru-noisy", "тест", string.Empty, 0, 0, "audio missing")
        };

        var report = CorpusBenchmark.Summarize("hybrid", entries);

        Assert.Equal(2, report.Sets.Count);
        Assert.Equal("ru-clean", report.Sets[0].Set);
        Assert.Equal(2, report.Sets[0].Clips);
        Assert.Equal("ru-noisy", report.Sets[1].Set);
        Assert.Equal(1, report.Sets[1].FailedClips);
        Assert.Equal(1 / 5d, report.WordErrorRate, precision: 6);
        Assert.Equal(3, report.Entries.Count);
    }

    private static BenchmarkReport Report(double wer, double p95, params (string Set, double Wer)[] sets) =>
        new(
            DateTime.UtcNow,
            "test",
            wer,
            wer / 4,
            p95 / 2,
            p95,
            sets.Select(set => new BenchmarkSetSummary(set.Set, 10, set.Wer, set.Wer / 4)).ToArray(),
            [],
            Corpus: new CorpusInventory(new string('a', 64), 10, 640, new string('c', 64)));
}
