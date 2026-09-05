using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class AppLogPrivacyTests
{
    [Fact]
    public void Hybrid_asr_logs_trigger_codes_but_never_detector_evidence()
    {
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Services", "HybridTranscriptionService.cs"));

        Assert.Contains("trigger={decision.Trigger}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("decision.Evidence", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sensitive_scope_is_nestable_and_flows_to_child_async_work()
    {
        Assert.False(AppLog.IsSensitiveDataSuppressed);

        using (AppLog.SuppressSensitiveData())
        {
            Assert.True(AppLog.IsSensitiveDataSuppressed);
            Assert.True(await Task.Run(() => AppLog.IsSensitiveDataSuppressed));

            using (AppLog.SuppressSensitiveData())
            {
                Assert.True(AppLog.IsSensitiveDataSuppressed);
            }

            Assert.True(AppLog.IsSensitiveDataSuppressed);
        }

        Assert.False(AppLog.IsSensitiveDataSuppressed);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Egoist.Voice.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
