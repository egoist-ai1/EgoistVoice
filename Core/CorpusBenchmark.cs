using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egoist.Voice.Core;

/// <summary>One recorded clip and its hand-checked reference transcript.</summary>
public sealed record CorpusEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("audio")] string Audio,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags = null)
{
    /// <summary>Leading path segment of the id, e.g. <c>ru-en-mixed</c> for <c>ru-en-mixed/004</c>.</summary>
    /// <remarks>
    /// JsonIgnore обязателен: значение выводится из <see cref="Id"/>, и без этого сериализатор
    /// дописывал его в reference.jsonl четвёртым полем. Файл вычитывается глазами, а лишнее поле,
    /// которое к тому же может разойтись с id, — приглашение поправить не то.
    /// </remarks>
    [JsonIgnore]
    public string Set
    {
        get
        {
            var separator = Id.IndexOf('/');
            return separator > 0 ? Id[..separator] : "default";
        }
    }
}

public sealed record BenchmarkEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("set")] string Set,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("hypothesis")] string Hypothesis,
    [property: JsonPropertyName("perceivedMs")] double PerceivedMs,
    [property: JsonPropertyName("totalMs")] double TotalMs,
    [property: JsonPropertyName("error")] string? Error = null);

public sealed record BenchmarkSetSummary(
    [property: JsonPropertyName("set")] string Set,
    [property: JsonPropertyName("clips")] int Clips,
    [property: JsonPropertyName("wer")] double WordErrorRate,
    [property: JsonPropertyName("cer")] double CharacterErrorRate);

public sealed record BenchmarkReport(
    [property: JsonPropertyName("generatedUtc")] DateTime GeneratedUtc,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("wer")] double WordErrorRate,
    [property: JsonPropertyName("cer")] double CharacterErrorRate,
    [property: JsonPropertyName("p50Ms")] double MedianMs,
    [property: JsonPropertyName("p95Ms")] double P95Ms,
    [property: JsonPropertyName("sets")] IReadOnlyList<BenchmarkSetSummary> Sets,
    [property: JsonPropertyName("entries")] IReadOnlyList<BenchmarkEntry> Entries);

public static class CorpusBenchmark
{
    public const string ReferenceFileName = "reference.jsonl";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// JSON Lines rather than a single array: the file is edited by hand while transcripts are
    /// proof-read, and a one-line-per-clip format keeps diffs readable and merges survivable.
    /// </summary>
    public static IReadOnlyList<CorpusEntry> LoadReferences(string corpusDirectory)
    {
        var path = Path.Combine(corpusDirectory, ReferenceFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<CorpusEntry>();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<CorpusEntry>(trimmed, Json);
            if (entry is not null && !string.IsNullOrWhiteSpace(entry.Id))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    public static BenchmarkReport Summarize(
        string label,
        IReadOnlyList<BenchmarkEntry> entries,
        ScoringOptions? options = null)
    {
        options ??= ScoringOptions.Default;
        var scored = entries.Where(entry => entry.Error is null).ToArray();
        var overall = RecognitionScorer.Aggregate(
            scored.Select(entry => RecognitionScorer.Score(entry.Reference, entry.Hypothesis, options)));

        var sets = scored
            .GroupBy(entry => entry.Set, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var score = RecognitionScorer.Aggregate(
                    group.Select(entry => RecognitionScorer.Score(entry.Reference, entry.Hypothesis, options)));
                return new BenchmarkSetSummary(
                    group.Key,
                    group.Count(),
                    score.WordErrorRate,
                    score.CharacterErrorRate);
            })
            .ToArray();

        var latencies = scored
            .Select(entry => TimeSpan.FromMilliseconds(entry.PerceivedMs))
            .ToArray();

        return new BenchmarkReport(
            DateTime.UtcNow,
            label,
            overall.WordErrorRate,
            overall.CharacterErrorRate,
            LatencyStatistics.Median(latencies).TotalMilliseconds,
            LatencyStatistics.Percentile(latencies, 0.95).TotalMilliseconds,
            sets,
            entries);
    }

    public static void Save(BenchmarkReport report, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(report, Json));
    }

    public static BenchmarkReport? Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<BenchmarkReport>(File.ReadAllText(path), Json) : null;

    /// <summary>
    /// A regression gate that reports every breach at once. Failing on the first one hides the
    /// others and turns one investigation into three.
    /// </summary>
    public static IReadOnlyList<string> CompareToBaseline(
        BenchmarkReport baseline,
        BenchmarkReport candidate,
        double maxWordErrorRateIncrease = 0.005,
        double maxLatencyIncreaseRatio = 0.15)
    {
        var breaches = new List<string>();
        var werDelta = candidate.WordErrorRate - baseline.WordErrorRate;
        if (werDelta > maxWordErrorRateIncrease)
        {
            breaches.Add(
                $"WER вырос на {werDelta * 100:0.00} п.п. " +
                $"({baseline.WordErrorRate * 100:0.00}% → {candidate.WordErrorRate * 100:0.00}%), " +
                $"допуск {maxWordErrorRateIncrease * 100:0.00} п.п.");
        }

        foreach (var baselineSet in baseline.Sets)
        {
            var candidateSet = candidate.Sets.FirstOrDefault(set => set.Set == baselineSet.Set);
            if (candidateSet is null)
            {
                breaches.Add($"Набор «{baselineSet.Set}» отсутствует в новом прогоне.");
                continue;
            }

            var setDelta = candidateSet.WordErrorRate - baselineSet.WordErrorRate;
            if (setDelta > maxWordErrorRateIncrease)
            {
                breaches.Add(
                    $"Набор «{baselineSet.Set}»: WER вырос на {setDelta * 100:0.00} п.п. " +
                    $"({baselineSet.WordErrorRate * 100:0.00}% → {candidateSet.WordErrorRate * 100:0.00}%).");
            }
        }

        if (baseline.P95Ms > 0)
        {
            var latencyRatio = (candidate.P95Ms - baseline.P95Ms) / baseline.P95Ms;
            if (latencyRatio > maxLatencyIncreaseRatio)
            {
                breaches.Add(
                    $"p95 latency вырос на {latencyRatio * 100:0.0}% " +
                    $"({baseline.P95Ms:0} мс → {candidate.P95Ms:0} мс), допуск {maxLatencyIncreaseRatio * 100:0}%.");
            }
        }

        return breaches;
    }
}
