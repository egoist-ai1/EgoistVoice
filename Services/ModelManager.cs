using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Egoist.Voice.Services;

public sealed class ModelManager : IModelManager
{
    private const long DiskSafetyBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly Dictionary<string, Task<string>> _downloads = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _httpClient;
    private readonly string _modelsRoot;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public ModelManager(
        IReadOnlyList<ModelDescriptor> requiredModels,
        string? modelsRoot = null,
        HttpMessageHandler? httpHandler = null)
    {
        RequiredModels = requiredModels;
        _modelsRoot = modelsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EgoistVoice",
            "Models");
        CleanupUnsupportedModelKinds();
        _httpClient = httpHandler is null ? new HttpClient() : new HttpClient(httpHandler, disposeHandler: true);
        _httpClient.Timeout = TimeSpan.FromHours(6);
        _ownsHttpClient = true;
    }

    public event EventHandler<ModelTransferProgress>? ProgressChanged;
    public IReadOnlyList<ModelDescriptor> RequiredModels { get; }
    /// <summary>
    /// Latched once true. The uncached form stats five files, reads five marker files and parses
    /// five JSON documents — acceptable during start-up, but it also sat in the dictation hot path
    /// and in the download progress callback, which fires several times per second.
    /// </summary>
    public bool AreAllModelsReady =>
        _allModelsReady || (_allModelsReady = RequiredModels.All(IsMarkerValid));

    private volatile bool _allModelsReady;
    public ModelTransferProgress? CurrentProgress { get; private set; }

    /// <summary>
    /// Hops onto the thread pool once, at the boundary. Every await inside this class then resumes
    /// without a synchronization context, which matters because the callers reach it from the UI
    /// thread: hashing a half-gigabyte model would otherwise post several hundred continuations
    /// into the dispatcher queue.
    /// </summary>
    public Task<string> EnsureModelAsync(
        ModelDescriptor descriptor,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() => EnsureModelCoreAsync(descriptor, progress, cancellationToken), cancellationToken);
    }

    private async Task<string> EnsureModelCoreAsync(
        ModelDescriptor descriptor,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        EventHandler<ModelTransferProgress>? handler = progress is null
            ? null
            : (_, value) =>
            {
                if (value.ModelName == descriptor.DisplayName)
                {
                    progress.Report(value);
                }
            };
        if (handler is not null)
        {
            ProgressChanged += handler;
        }

        Task<string> task;
        lock (_sync)
        {
            if (!_downloads.TryGetValue(descriptor.Id, out task!))
            {
                task = EnsureCoreAsync(descriptor, _lifetime.Token);
                _downloads[descriptor.Id] = task;
                _ = RemoveFailedTaskAsync(descriptor.Id, task);
            }
        }

        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        finally
        {
            if (handler is not null)
            {
                ProgressChanged -= handler;
            }
        }
    }

    public async Task DownloadRequiredModelsAsync(CancellationToken cancellationToken)
    {
        foreach (var descriptor in RequiredModels)
        {
            await EnsureModelAsync(descriptor, null, cancellationToken);
        }
    }

    private async Task<string> EnsureCoreAsync(ModelDescriptor descriptor, CancellationToken cancellationToken)
    {
        var finalPath = GetModelPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        TryMigrateLegacyWhisper(descriptor, finalPath);

        if (await IsVerifiedAsync(descriptor, finalPath, cancellationToken))
        {
            CleanupSupersededModels(descriptor, finalPath);
            Report(descriptor, ModelTransferStage.Ready, descriptor.SizeBytes, 100, force: true);
            return finalPath;
        }

        var partialPath = finalPath + ".download";
        var existingBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (existingBytes > descriptor.SizeBytes)
        {
            File.Delete(partialPath);
            existingBytes = 0;
        }
        EnsureDiskSpace(finalPath, descriptor.SizeBytes - existingBytes);

        try
        {
            await DownloadWithRetryAsync(descriptor, partialPath, cancellationToken);
            if (new FileInfo(partialPath).Length != descriptor.SizeBytes)
            {
                throw new InvalidDataException("Скачанная модель имеет неверный размер.");
            }

            try
            {
                await VerifyHashAsync(descriptor, partialPath, cancellationToken);
            }
            catch (InvalidDataException)
            {
                File.Delete(partialPath);
                throw;
            }
            File.Move(partialPath, finalPath, true);
            await WriteMarkerAsync(descriptor, finalPath, cancellationToken);
            CleanupSupersededModels(descriptor, finalPath);
            Report(descriptor, ModelTransferStage.Ready, descriptor.SizeBytes, 100, force: true);
            return finalPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Report(descriptor, ModelTransferStage.Failed, existingBytes, 0, force: true, error: exception.Message);
            throw;
        }
    }

    private async Task DownloadWithRetryAsync(
        ModelDescriptor descriptor,
        string partialPath,
        CancellationToken cancellationToken)
    {
        const int attempts = 3;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var existingBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            try
            {
                await DownloadAsync(descriptor, partialPath, existingBytes, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                attempt < attempts && exception is HttpRequestException or IOException or TaskCanceledException)
            {
                AppLog.Write($"Model download retry {attempt}/{attempts}: {descriptor.Id}", exception);
                await Task.Delay(TimeSpan.FromMilliseconds(350 * (1 << (attempt - 1))), cancellationToken);
            }
        }
    }

    private async Task DownloadAsync(
        ModelDescriptor descriptor,
        string partialPath,
        long existingBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, descriptor.DownloadUri);
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existingBytes == descriptor.SizeBytes)
        {
            return;
        }
        response.EnsureSuccessStatusCode();

        var append = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (append)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.From != existingBytes || range.Length is not null && range.Length != descriptor.SizeBytes)
            {
                File.Delete(partialPath);
                throw new HttpRequestException("Сервер вернул некорректный диапазон модели; загрузка будет начата заново.");
            }
        }
        if (!append)
        {
            existingBytes = 0;
        }

        var mode = append ? FileMode.Append : FileMode.Create;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            partialPath,
            mode,
            FileAccess.Write,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var received = existingBytes;
            var stopwatch = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            var lastBytes = received;
            var smoothedSpeed = 0d;
            Report(descriptor, ModelTransferStage.Downloading, received, Percent(received, descriptor.SizeBytes), force: true);

            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, 1024 * 1024), cancellationToken);
                if (read == 0)
                {
                    break;
                }
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;

                var elapsed = stopwatch.Elapsed;
                if (elapsed - lastReport < TimeSpan.FromMilliseconds(200))
                {
                    continue;
                }

                var seconds = (elapsed - lastReport).TotalSeconds;
                var instantaneous = seconds > 0 ? (received - lastBytes) / seconds : 0;
                smoothedSpeed = smoothedSpeed <= 0 ? instantaneous : (smoothedSpeed * 0.72) + (instantaneous * 0.28);
                TimeSpan? eta = smoothedSpeed > 1 && received < descriptor.SizeBytes
                    ? TimeSpan.FromSeconds((descriptor.SizeBytes - received) / smoothedSpeed)
                    : null;
                Report(
                    descriptor,
                    ModelTransferStage.Downloading,
                    received,
                    Percent(received, descriptor.SizeBytes),
                    force: true,
                    speed: smoothedSpeed,
                    eta: eta);
                lastReport = elapsed;
                lastBytes = received;
            }
            await destination.FlushAsync(cancellationToken);
            Report(descriptor, ModelTransferStage.Downloading, received, 100, force: true, speed: smoothedSpeed);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<bool> IsVerifiedAsync(
        ModelDescriptor descriptor,
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != descriptor.SizeBytes)
        {
            return false;
        }

        var markerPath = GetMarkerPath(path);
        try
        {
            if (File.Exists(markerPath))
            {
                var marker = JsonSerializer.Deserialize<VerificationMarker>(
                    await File.ReadAllTextAsync(markerPath, cancellationToken),
                    JsonOptions);
                if (marker is not null && marker.Id == descriptor.Id && marker.SizeBytes == descriptor.SizeBytes &&
                    string.Equals(marker.Sha256, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // A stale marker is repaired after a full checksum pass.
        }

        await VerifyHashAsync(descriptor, path, cancellationToken);
        await WriteMarkerAsync(descriptor, path, cancellationToken);
        return true;
    }

    private async Task VerifyHashAsync(ModelDescriptor descriptor, string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4 * 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
        try
        {
            long processed = 0;
            var lastReport = Stopwatch.StartNew();
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, 4 * 1024 * 1024), cancellationToken);
                if (read == 0)
                {
                    break;
                }
                sha.AppendData(buffer, 0, read);
                processed += read;
                if (lastReport.Elapsed >= TimeSpan.FromMilliseconds(200))
                {
                    Report(descriptor, ModelTransferStage.Verifying, descriptor.SizeBytes, Percent(processed, descriptor.SizeBytes), force: true);
                    lastReport.Restart();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var actual = Convert.ToHexString(sha.GetHashAndReset());
        if (!actual.Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Контрольная сумма модели не совпадает с официальной.");
        }
        Report(descriptor, ModelTransferStage.Verifying, descriptor.SizeBytes, 100, force: true);
    }

    private async Task WriteMarkerAsync(ModelDescriptor descriptor, string path, CancellationToken cancellationToken)
    {
        var marker = new VerificationMarker(descriptor.Id, descriptor.SizeBytes, descriptor.Sha256);
        var markerPath = GetMarkerPath(path);
        var temporaryPath = markerPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(marker, JsonOptions), cancellationToken);
        File.Move(temporaryPath, markerPath, true);
    }

    private void Report(
        ModelDescriptor descriptor,
        ModelTransferStage stage,
        long bytes,
        double percentage,
        bool force,
        double speed = 0,
        TimeSpan? eta = null,
        string? error = null)
    {
        _ = force;
        var index = Math.Max(0, RequiredModels.IndexOf(descriptor));
        var totalBytes = RequiredModels.Sum(model => model.SizeBytes);
        long overallBytes = 0;
        for (var modelIndex = 0; modelIndex < RequiredModels.Count; modelIndex++)
        {
            var model = RequiredModels[modelIndex];
            if (modelIndex < index || IsMarkerValid(model))
            {
                overallBytes += model.SizeBytes;
            }
            else if (modelIndex == index)
            {
                overallBytes += Math.Clamp(bytes, 0, model.SizeBytes);
            }
        }

        var value = new ModelTransferProgress(
            descriptor.DisplayName,
            index + 1,
            RequiredModels.Count,
            stage,
            Math.Clamp(bytes, 0, descriptor.SizeBytes),
            descriptor.SizeBytes,
            Math.Clamp(percentage, 0, 100),
            Percent(overallBytes, totalBytes),
            speed,
            eta,
            error);
        CurrentProgress = value;
        ProgressChanged?.Invoke(this, value);
    }

    private bool IsMarkerValid(ModelDescriptor descriptor)
    {
        var path = GetModelPath(descriptor);
        if (!File.Exists(path) || new FileInfo(path).Length != descriptor.SizeBytes || !File.Exists(GetMarkerPath(path)))
        {
            return false;
        }
        try
        {
            var marker = JsonSerializer.Deserialize<VerificationMarker>(File.ReadAllText(GetMarkerPath(path)), JsonOptions);
            return marker is not null && marker.Id == descriptor.Id && marker.SizeBytes == descriptor.SizeBytes &&
                   marker.Sha256.Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return false;
        }
    }

    private string GetModelPath(ModelDescriptor descriptor) => Path.Combine(
        _modelsRoot,
        descriptor.Kind.ToString(),
        descriptor.Id,
        descriptor.FileName);

    private static string GetMarkerPath(string modelPath) => modelPath + ".verified.json";

    private void TryMigrateLegacyWhisper(ModelDescriptor descriptor, string finalPath)
    {
        if (descriptor.Kind != ModelKind.Speech || File.Exists(finalPath))
        {
            return;
        }
        var legacy = Path.Combine(_modelsRoot, descriptor.FileName);
        if (File.Exists(legacy) && new FileInfo(legacy).Length == descriptor.SizeBytes)
        {
            File.Move(legacy, finalPath);
        }
    }

    private static void EnsureDiskSpace(string path, long requiredBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))!;
        var available = new DriveInfo(root).AvailableFreeSpace;
        if (available < requiredBytes + DiskSafetyBytes)
        {
            var neededGb = (requiredBytes + DiskSafetyBytes) / 1024d / 1024d / 1024d;
            throw new IOException($"Недостаточно места. Освободите минимум {neededGb:0.0} ГБ.");
        }
    }

    private void CleanupSupersededModels(ModelDescriptor descriptor, string activePath)
    {
        var kindRoot = Path.Combine(_modelsRoot, descriptor.Kind.ToString());
        if (!Directory.Exists(kindRoot))
        {
            return;
        }
        _ = activePath;
        var activeDirectories = RequiredModels
            .Where(model => model.Kind == descriptor.Kind)
            .Select(model => Path.GetFullPath(Path.GetDirectoryName(GetModelPath(model))!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(kindRoot))
        {
            if (!activeDirectories.Contains(Path.GetFullPath(directory)))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException exception)
                {
                    AppLog.Write($"Superseded model cleanup deferred: {Path.GetFileName(directory)}", exception);
                }
            }
        }
    }

    private void CleanupUnsupportedModelKinds()
    {
        if (!Directory.Exists(_modelsRoot))
        {
            return;
        }

        var supportedKinds = RequiredModels
            .Select(model => model.Kind.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(_modelsRoot))
        {
            if (supportedKinds.Contains(Path.GetFileName(directory)))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
                AppLog.Write($"Removed unsupported legacy model directory: {Path.GetFileName(directory)}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write($"Legacy model cleanup deferred: {Path.GetFileName(directory)}", exception);
            }
        }
    }

    private async Task RemoveFailedTaskAsync(string id, Task<string> task)
    {
        try
        {
            await task;
        }
        catch
        {
            lock (_sync)
            {
                if (_downloads.TryGetValue(id, out var current) && ReferenceEquals(current, task))
                {
                    _downloads.Remove(id);
                }
            }
        }
    }

    private static double Percent(long value, long total) => total <= 0 ? 0 : value * 100d / total;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed record VerificationMarker(string Id, long SizeBytes, string Sha256);
}

internal static class ModelDescriptorListExtensions
{
    internal static int IndexOf(this IReadOnlyList<ModelDescriptor> models, ModelDescriptor descriptor)
    {
        for (var index = 0; index < models.Count; index++)
        {
            if (models[index].Id == descriptor.Id)
            {
                return index;
            }
        }
        return -1;
    }
}
