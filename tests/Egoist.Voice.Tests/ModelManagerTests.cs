using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class ModelManagerTests
{
    [Fact]
    public async Task Downloads_verifies_and_reports_model()
    {
        var content = Enumerable.Range(0, 64 * 1024).Select(index => (byte)(index % 251)).ToArray();
        var descriptor = CreateDescriptor("model-v2", content);
        var root = CreateTemporaryDirectory();
        try
        {
            using var manager = new ModelManager([descriptor], root, new RangeHandler(content));
            var stages = new List<ModelTransferStage>();
            manager.ProgressChanged += (_, progress) => stages.Add(progress.Stage);

            var path = await manager.EnsureModelAsync(descriptor, null, CancellationToken.None);

            Assert.Equal(content, await File.ReadAllBytesAsync(path));
            Assert.True(File.Exists(path + ".verified.json"));
            Assert.Contains(ModelTransferStage.Downloading, stages);
            Assert.Contains(ModelTransferStage.Verifying, stages);
            Assert.Equal(ModelTransferStage.Ready, stages[^1]);
            Assert.True(manager.AreAllModelsReady);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Resumes_partial_download_with_http_range()
    {
        var content = Enumerable.Range(0, 80 * 1024).Select(index => (byte)(index % 239)).ToArray();
        var descriptor = CreateDescriptor("resume-v1", content);
        var root = CreateTemporaryDirectory();
        try
        {
            var partial = Path.Combine(root, "Speech", descriptor.Id, descriptor.FileName + ".download");
            Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
            await File.WriteAllBytesAsync(partial, content[..20_000]);
            var handler = new RangeHandler(content);
            using var manager = new ModelManager([descriptor], root, handler);

            var path = await manager.EnsureModelAsync(descriptor, null, CancellationToken.None);

            Assert.Equal(20_000, handler.RequestedRangeStart);
            Assert.Equal(content, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Restarts_download_when_server_returns_mismatched_content_range()
    {
        var content = Enumerable.Range(0, 48 * 1024).Select(index => (byte)(index % 233)).ToArray();
        var descriptor = CreateDescriptor("range-recovery-v1", content);
        var root = CreateTemporaryDirectory();
        try
        {
            var partial = Path.Combine(root, "Speech", descriptor.Id, descriptor.FileName + ".download");
            Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
            await File.WriteAllBytesAsync(partial, content[..12_000]);
            var handler = new MismatchedRangeOnceHandler(content);
            using var manager = new ModelManager([descriptor], root, handler);

            var path = await manager.EnsureModelAsync(descriptor, null, CancellationToken.None);

            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(content, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Retries_transient_download_failures_and_keeps_partial_file()
    {
        var content = Enumerable.Range(0, 72 * 1024).Select(index => (byte)(index % 227)).ToArray();
        var descriptor = CreateDescriptor("retry-v1", content);
        var root = CreateTemporaryDirectory();
        try
        {
            var handler = new TransientFailureHandler(content, failures: 2);
            using var manager = new ModelManager([descriptor], root, handler);

            var path = await manager.EnsureModelAsync(descriptor, null, CancellationToken.None);

            Assert.Equal(3, handler.RequestCount);
            Assert.Equal(content, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(path + ".verified.json.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_bad_checksum_and_removes_corrupt_partial()
    {
        var expected = Enumerable.Repeat((byte)7, 32 * 1024).ToArray();
        var corrupt = Enumerable.Repeat((byte)8, expected.Length).ToArray();
        var descriptor = CreateDescriptor("bad-v1", expected);
        var root = CreateTemporaryDirectory();
        try
        {
            using var manager = new ModelManager([descriptor], root, new RangeHandler(corrupt));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                manager.EnsureModelAsync(descriptor, null, CancellationToken.None));
            var partial = Path.Combine(root, "Speech", descriptor.Id, descriptor.FileName + ".download");
            Assert.False(File.Exists(partial));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Preserves_all_components_of_current_model_set_and_removes_superseded_versions()
    {
        var firstContent = new byte[] { 1, 2, 3, 4 };
        var secondContent = new byte[] { 5, 6, 7, 8 };
        var first = CreateDescriptor("giga-encoder-v1", firstContent, "encoder.onnx");
        var second = CreateDescriptor("giga-decoder-v1", secondContent, "decoder.onnx");
        var root = CreateTemporaryDirectory();
        var stale = Path.Combine(root, "Speech", "giga-encoder-v0");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "old.onnx"), "old");
        try
        {
            using var manager = new ModelManager([first, second], root, new MultiModelHandler(
                new Dictionary<string, byte[]>
                {
                    [first.FileName] = firstContent,
                    [second.FileName] = secondContent
                }));

            var firstPath = await manager.EnsureModelAsync(first, null, CancellationToken.None);
            var secondPath = await manager.EnsureModelAsync(second, null, CancellationToken.None);

            Assert.True(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));
            Assert.False(Directory.Exists(stale));
            Assert.True(manager.AreAllModelsReady);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ModelDescriptor CreateDescriptor(string id, byte[] content, string fileName = "test.gguf") => new(
        id,
        "Test Model",
        ModelKind.Speech,
        new Uri($"https://models.invalid/{fileName}"),
        fileName,
        content.Length,
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

    [Fact]
    public void Removes_unsupported_legacy_model_directories()
    {
        var content = new byte[] { 1, 2, 3 };
        var descriptor = CreateDescriptor("speech-v1", content);
        var root = CreateTemporaryDirectory();
        var legacyFile = Path.Combine(root, "Language", "qwen3-v1", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyFile)!);
        File.WriteAllBytes(legacyFile, content);
        try
        {
            using var manager = new ModelManager([descriptor], root, new RangeHandler(content));

            Assert.False(Directory.Exists(Path.Combine(root, "Language")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Offline_mode_never_starts_a_model_download()
    {
        var content = new byte[] { 1, 2, 3, 4 };
        var descriptor = CreateDescriptor("offline-v1", content);
        var root = CreateTemporaryDirectory();
        var handler = new RangeHandler(content);
        try
        {
            using var manager = new ModelManager([descriptor], root, handler, allowDownload: false);

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                manager.EnsureModelAsync(descriptor, null, CancellationToken.None));

            Assert.Contains("offline-only", exception.Message, StringComparison.Ordinal);
            Assert.Null(handler.RequestedRangeStart);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "egoist-voice-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RangeHandler(byte[] content) : HttpMessageHandler
    {
        public long? RequestedRangeStart { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedRangeStart = request.Headers.Range?.Ranges.Single().From;
            var start = checked((int)(RequestedRangeStart ?? 0));
            var response = new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content[start..])
            };
            response.Content.Headers.ContentLength = content.Length - start;
            if (start > 0)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, content.Length - 1, content.Length);
            }
            return Task.FromResult(response);
        }
    }

    private sealed class MultiModelHandler(IReadOnlyDictionary<string, byte[]> models) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            var content = models[name];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class TransientFailureHandler(byte[] content, int failures) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount <= failures)
            {
                throw new HttpRequestException("temporary network failure");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class MismatchedRangeOnceHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                var requestedStart = checked((int)(request.Headers.Range?.Ranges.Single().From ?? 0));
                var wrongStart = requestedStart + 1;
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(content[wrongStart..])
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(wrongStart, content.Length - 1, content.Length);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }
}
