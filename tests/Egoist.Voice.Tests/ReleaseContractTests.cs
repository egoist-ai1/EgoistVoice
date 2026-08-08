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
    public void InstallerOwnsOnlyItsSharedEngineMarker()
    {
        var installer = File.ReadAllText(Path.Combine(RepositoryRoot(), "installer", "EgoistVoice.iss"));

        Assert.Contains("invoke-engine-bootstrap.ps1", installer, StringComparison.Ordinal);
        Assert.Contains("-Action InstallOwner -OwnerId egoist-voice", installer, StringComparison.Ordinal);
        Assert.Contains("-Action RemoveOwner -OwnerId egoist-voice -CleanupIfLast", installer, StringComparison.Ordinal);
        Assert.Contains("engine-bundle-manifest.json", installer, StringComparison.Ordinal);
        Assert.Contains("offline-pack", installer, StringComparison.Ordinal);
        Assert.Contains("DiskSpanning=yes", installer, StringComparison.Ordinal);
        Assert.Contains("DiskSliceSize=2100000000", installer, StringComparison.Ordinal);
        Assert.Contains("function QuotePowerShellArgument", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("-File \"\"", installer, StringComparison.Ordinal);
        Assert.Contains("ScaleDivisor", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("AvailableWidth * Current", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("Current * 100", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("egoist-translator.owner.json", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("DelTree(ExpandConstant('{localappdata}\\EGOIST\\TranslationEngine", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildConsumesOnlyThePinnedFullEngineBundle()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "build-installer.ps1"));

        Assert.Contains("engine-bundle-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("hy-mt2-1.8b-q8_0", script, StringComparison.Ordinal);
        Assert.Contains("llama-b10219-vulkan-win-x64", script, StringComparison.Ordinal);
        Assert.Contains("checksum mismatch", script, StringComparison.Ordinal);
        Assert.Contains("embedded-inno-bootstrap", script, StringComparison.Ordinal);
        Assert.Contains("New-EgoistVoiceSingleFile.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Test-EgoistVoiceSingleFile.ps1", script, StringComparison.Ordinal);
        Assert.Contains("embeddedPayloadFiles", script, StringComparison.Ordinal);
        Assert.Contains("AssemblyFileVersion", script, StringComparison.Ordinal);
        Assert.Contains("bootstrapFileVersion", script, StringComparison.Ordinal);
        Assert.Contains("bootstrapProductVersion", script, StringComparison.Ordinal);
        Assert.Contains("FileVersionInfo", script, StringComparison.Ordinal);
        Assert.DoesNotContain("deliveryMode = \"inno-disk-spanning\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/latest", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VoiceDeliveryIsOneIntegrityCheckedExeWithABoundedBootstrapWindow()
    {
        var root = RepositoryRoot();
        var bootstrap = File.ReadAllText(Path.Combine(root, "installer", "EgoistVoiceBootstrap.cs"));
        var packer = File.ReadAllText(Path.Combine(root, "scripts", "New-EgoistVoiceSingleFile.ps1"));
        var verifier = File.ReadAllText(Path.Combine(root, "scripts", "Test-EgoistVoiceSingleFile.ps1"));

        Assert.Contains("EGOISTVOICEPKG01", bootstrap, StringComparison.Ordinal);
        Assert.Contains("VerifyPayload", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ExtractPayload", bootstrap, StringComparison.Ordinal);
        Assert.Contains("AvailableFreeSpace", bootstrap, StringComparison.Ordinal);
        Assert.Contains("FormBorderStyle.FixedDialog", bootstrap, StringComparison.Ordinal);
        Assert.Contains("FormStartPosition.CenterScreen", bootstrap, StringComparison.Ordinal);
        Assert.Contains("MaximizeBox = false", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("FormWindowState.Maximized", bootstrap, StringComparison.Ordinal);
        Assert.Contains("EGOISTVOICEPKG01", packer, StringComparison.Ordinal);
        Assert.Contains("TransformBlock", packer, StringComparison.Ordinal);
        Assert.Contains("Embedded payload checksum mismatch", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void WebBootstrapPinsGitHubPayloadAndFailsClosedBeforeInstall()
    {
        var root = RepositoryRoot();
        var bootstrap = File.ReadAllText(Path.Combine(root, "installer", "EgoistVoiceWebBootstrap.cs"));
        var manifest = File.ReadAllText(Path.Combine(root, "installer", "EgoistVoiceWebBootstrap.Manifest.cs"));
        var builder = File.ReadAllText(Path.Combine(root, "scripts", "Build-EgoistVoiceWebInstaller.ps1"));
        var networkFixture = File.ReadAllText(Path.Combine(root, "scripts", "Test-EgoistVoiceWebInstaller.ps1"));

        Assert.Contains("https://github.com/egoist-ai1/EgoistVoice/releases/download/v2.2.0-preview.1/", manifest, StringComparison.Ordinal);
        Assert.Contains("2098236672L", manifest, StringComparison.Ordinal);
        Assert.Contains("1091700448L", manifest, StringComparison.Ordinal);
        Assert.Contains("691f5e60b76b7595dc8752a2264bccda04ebb9d9dbc5e1257eb9a300846b2a02", manifest, StringComparison.Ordinal);
        Assert.Contains("6d7cada0e16fcec94899811d30fd386d816945f8185e62a3c20aab05b468c74c", manifest, StringComparison.Ordinal);

        Assert.Contains("request.AddRange(existing)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ValidateDownloadResponse", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ComputeHash(finalPath", bootstrap, StringComparison.Ordinal);
        Assert.Contains("VerifyDirectory", bootstrap, StringComparison.Ordinal);
        Assert.Contains("LaunchInnerInstaller", bootstrap, StringComparison.Ordinal);
        Assert.Contains("if (ExitCode == 0 && downloaded)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("--offline", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("/releases/latest", bootstrap, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://github.com", bootstrap, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("EgoistVoice-2.2.0-preview.1.release.json", builder, StringComparison.Ordinal);
        Assert.Contains("sourceRevision", builder, StringComparison.Ordinal);
        Assert.Contains("sourceTreeDirty", builder, StringComparison.Ordinal);
        Assert.Contains("sha256BeforeLaunch", builder, StringComparison.Ordinal);
        Assert.Contains("bytes=$half-", networkFixture, StringComparison.Ordinal);
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
