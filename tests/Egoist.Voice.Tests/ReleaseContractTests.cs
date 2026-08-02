using System.Text.Json;
using System.Text.RegularExpressions;

namespace Egoist.Voice.Tests;

public sealed class ReleaseContractTests
{
    [Fact]
    public void InstallerScripts_DeriveTheirDefaultVersionFromTheProject()
    {
        var root = RepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "Egoist.Voice.csproj"));
        var version = Regex.Match(project, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(version));

        foreach (var relativePath in new[]
                 {
                     "scripts/build-installer.ps1",
                     "scripts/test-installer.ps1",
                     "scripts/full-release-smoke.ps1"
                 })
        {
            var script = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.Contains("[string]$Version = \"\"", script, StringComparison.Ordinal);
            Assert.Contains("<Version>([^<]+)</Version>", script, StringComparison.Ordinal);
            Assert.DoesNotContain("[string]$Version = \"2.0.0\"", script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CorpusSchemasAndRunner_KeepTheOfflinePrivacyContract()
    {
        var root = RepositoryRoot();
        foreach (var name in new[]
                 {
                     "corpus-script-v2.schema.json",
                     "corpus-reference-v2.schema.json",
                     "benchmark-report-v2.schema.json"
                 })
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "corpus", name)));
            Assert.Equal(JsonValueKind.Object, schema.RootElement.ValueKind);
            Assert.True(schema.RootElement.TryGetProperty("$schema", out _));
            Assert.True(schema.RootElement.TryGetProperty("$id", out _));
        }

        var runner = File.ReadAllText(Path.Combine(root, "scripts", "run-corpus-baseline.ps1"));
        Assert.Contains("[switch]$Record", runner, StringComparison.Ordinal);
        Assert.Contains("--corpus-record", runner, StringComparison.Ordinal);
        Assert.Contains("--corpus-benchmark", runner, StringComparison.Ordinal);
        Assert.Contains("$DecoderMode", runner, StringComparison.Ordinal);
        Assert.Contains(".candidate", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy-Item", runner, StringComparison.OrdinalIgnoreCase);

        var app = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
        Assert.Contains("allowModelDownload: false", app, StringComparison.Ordinal);
        Assert.Contains("AppLog.SuppressSensitiveData()", app, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Egoist.Voice.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Egoist.Voice.sln not found");
    }
}
