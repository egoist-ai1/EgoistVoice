using System.Text.Json;
using Egoist.Translation.Client;
using Egoist.Translation.Contracts;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class TranslatorClientTests
{
    [Fact]
    public async Task SharedPipeSuccessMapsVoiceRequestWithoutLeakingToHttp()
    {
        var gateway = new FakeGateway();
        using var client = new TranslatorClient(gateway);

        var result = await client.TranslateAsync(
            "Привет",
            "English",
            _ => { },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Hello", result.Text);
        Assert.NotNull(gateway.LastRequest);
        Assert.Equal("auto", gateway.LastRequest.SourceLanguage);
        Assert.Equal("en", gateway.LastRequest.TargetLanguage);
        Assert.Equal(RequestPriority.Interactive, gateway.LastRequest.Priority);
        Assert.Equal(TranslationProfile.NaturalMessage, gateway.LastRequest.Profile);
    }

    [Fact]
    public async Task EngineFailureNeverMasqueradesOriginalAsTranslation()
    {
        var gateway = new FakeGateway
        {
            Failure = new TranslationEngineException(new ProtocolError
            {
                Code = ProtocolErrorCode.EngineMissing,
                Message = "missing",
                Retryable = true,
            }),
        };
        using var client = new TranslatorClient(gateway);

        var result = await client.TranslateAsync("Секретный текст", "English", _ => { }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Text);
        Assert.Equal(TranslationFailureKind.EngineMissing, result.Failure);
    }

    [Fact]
    public async Task LanguageOutsidePinnedOfflineTierIsRejectedBeforeFraming()
    {
        var gateway = new FakeGateway();
        using var client = new TranslatorClient(gateway);

        var result = await client.TranslateAsync("Привет", "Kazakh", _ => { }, CancellationToken.None);

        Assert.Equal(TranslationFailureKind.UnsupportedLanguage, result.Failure);
        Assert.Equal(0, gateway.EnsureCalls);
        Assert.Null(gateway.LastRequest);
    }

    [Fact]
    public async Task CallerCancellationRemainsAuthoritative()
    {
        var gateway = new FakeGateway { Failure = new OperationCanceledException() };
        using var client = new TranslatorClient(gateway);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.TranslateAsync("Привет", "English", _ => { }, cancellation.Token));
    }

    [Fact]
    public void SharedHostPointerRejectsTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "egoist-voice-pointer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "current.json"), """
                {
                  "schemaVersion": 1,
                  "generationId": "..",
                  "relativePath": "generations/..",
                  "createdAtUtc": "2026-08-06T00:00:00Z",
                  "hostVersion": "1.0.0",
                  "hostPayloadManifestSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                }
                """);

            Assert.Null(SharedTranslationEngineGateway.ResolveCurrentSharedHostDirectory(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VendoredClientManifestMatchesExactBinaries()
    {
        var root = RepositoryRoot();
        var vendor = Path.Combine(root, "vendor", "translation-client", "1.0.0");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(vendor, "manifest.json")));

        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.Combine(vendor, "net8.0", entry.GetProperty("name").GetString()!);
            Assert.Equal(entry.GetProperty("bytes").GetInt64(), new FileInfo(path).Length);
            Assert.Equal(
                entry.GetProperty("sha256").GetString(),
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }

        var source = File.ReadAllText(Path.Combine(root, "Services", "TranslatorClient.cs"));
        Assert.DoesNotContain("47821", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/v1/chat/completions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var gateway = new FakeGateway();
        var client = new TranslatorClient(gateway);

        client.Dispose();
        client.Dispose();

        Assert.Equal(1, gateway.DisposeCalls);
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

    private sealed class FakeGateway : ITranslationEngineGateway
    {
        public Exception? Failure { get; init; }
        public TranslationRequest? LastRequest { get; private set; }
        public int EnsureCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public Task<EngineStatusSnapshot> EnsureAvailableAsync(CancellationToken cancellationToken)
        {
            EnsureCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                return Task.FromException<EngineStatusSnapshot>(Failure);
            }

            return Task.FromResult(new EngineStatusSnapshot
            {
                State = EngineState.Ready,
                Message = "ready",
                OfflineReady = true,
                QueueDepth = 0,
                CacheEntries = 0,
            });
        }

        public Task<TranslationResult> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                return Task.FromException<TranslationResult>(Failure);
            }

            return Task.FromResult(new TranslationResult
            {
                Text = "Hello",
                DetectedLanguage = "ru",
                DetectionConfidence = 1,
                Model = new ModelIdentity
                {
                    Id = "hy-mt2-1.8b-q8_0",
                    Repository = "tencent/HY-MT2-1.8B-GGUF",
                    Revision = "pinned",
                    FileName = "Hy-MT2-1.8B-Q8_0.gguf",
                    Bytes = 1,
                    Sha256 = "5c3fe0b1408a5ceb0143184ef247b11b579c525f4b02b060e6c851bb76fef1a4",
                    Quantization = "Q8_0",
                    PromptProfile = "hy-mt2-official-v1",
                },
                Profile = request.Profile,
                ChunkCount = 1,
                QueueMilliseconds = 0,
                InferenceMilliseconds = 1,
                TotalMilliseconds = 1,
                FromCache = false,
            });
        }

        public void Dispose() => DisposeCalls++;
    }
}
