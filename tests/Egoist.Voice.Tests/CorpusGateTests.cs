using System.IO;
using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

/// <summary>
/// The regression gate over a real corpus. Both the corpus and the benchmark reports live outside
/// version control — the recordings are the user's own voice — so every test here is a no-op when
/// the artefacts are absent. A gate that blocks a clean checkout gets disabled, and a disabled gate
/// protects nothing.
/// </summary>
public sealed class CorpusGateTests
{
    [Fact]
    public void Latest_benchmark_does_not_regress_against_the_baseline()
    {
        var baseline = CorpusBenchmark.Load(ArtifactPath("baseline.json"));
        var latest = CorpusBenchmark.Load(ArtifactPath("latest.json"));
        if (baseline is null || latest is null)
        {
            return;
        }

        var breaches = CorpusBenchmark.CompareToBaseline(baseline, latest);

        Assert.True(
            breaches.Count == 0,
            $"Регрессия против {baseline.Label} от {baseline.GeneratedUtc:u}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, breaches.Select(breach => "  • " + breach)));
    }

    [Fact]
    public void Corpus_references_are_well_formed()
    {
        var corpus = CorpusDirectory();
        var references = CorpusBenchmark.LoadReferences(corpus);
        if (references.Count == 0)
        {
            return;
        }

        var duplicates = references
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.True(duplicates.Length == 0, $"Дублирующиеся id: {string.Join(", ", duplicates)}");

        var missingText = references.Where(entry => string.IsNullOrWhiteSpace(entry.Text)).ToArray();
        Assert.True(
            missingText.Length == 0,
            $"Пустой эталон у: {string.Join(", ", missingText.Select(entry => entry.Id))}");

        var missingAudio = references
            .Where(entry => !File.Exists(Path.Combine(corpus, entry.Audio)))
            .Select(entry => entry.Audio)
            .ToArray();
        Assert.True(missingAudio.Length == 0, $"Нет аудио: {string.Join(", ", missingAudio)}");
    }

    private static string CorpusDirectory() => ResolveFromRepositoryRoot(Path.Combine("tests", "corpus"));

    private static string ArtifactPath(string fileName) =>
        ResolveFromRepositoryRoot(Path.Combine("artifacts", "bench", fileName));

    /// <summary>Walks up from the test binary until the solution file appears.</summary>
    private static string ResolveFromRepositoryRoot(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Egoist.Voice.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? AppContext.BaseDirectory, relativePath);
    }
}
