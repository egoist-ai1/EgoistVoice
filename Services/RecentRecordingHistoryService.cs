using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Egoist.Voice.Services;

public enum RecentRecordingStatus
{
    Recognized,
    ProcessingFailed
}

public sealed record RecentRecording(
    string Id,
    DateTime CreatedUtc,
    TimeSpan Duration,
    RecentRecordingStatus Status,
    string FileName);

public sealed record RecentRecordingPlaybackChangedEventArgs(string? RecordingId, bool IsPlaying);

internal static class RecentRecordingPersistencePolicy
{
    internal static bool ShouldQueue(
        AudioCaptureResult? capture,
        bool cancellationRequested,
        bool enabled) =>
        enabled &&
        !cancellationRequested &&
        capture is { HasSpeech: true, Samples.Length: > 0, Duration: var duration } &&
        duration > TimeSpan.Zero;
}

internal enum RecentRecordingDurablePoint
{
    OldestAudioDeleted,
    PrunedManifestTemporaryWritten,
    PrunedManifestCommitted,
    TemporaryCreated,
    TemporaryFinalized,
    FinalPromoted,
    FinalManifestTemporaryWritten,
    ManifestCommitted
}

internal sealed class SimulatedHistoryCrashException : Exception
{
    internal SimulatedHistoryCrashException(RecentRecordingDurablePoint point)
        : base($"Simulated history crash at {point}.")
    {
    }
}

internal interface IRecentRecordingCodec
{
    void Encode(float[] samples, int sourceSampleRate, string outputPath);
    bool CanDecode(string path);
    bool LooksLikeContainer(string path);
    WaveStream OpenForPlayback(string path);
}

internal sealed class MediaFoundationRecentRecordingCodec : IRecentRecordingCodec
{
    private const int OutputSampleRate = 48_000;
    private const int Bitrate = 64_000;

    public void Encode(float[] samples, int sourceSampleRate, string outputPath)
    {
        if (samples.Length == 0 || sourceSampleRate <= 0)
        {
            throw new InvalidDataException("Recording contains no audio samples.");
        }

        var source = new FloatArraySampleProvider(samples, sourceSampleRate);
        ISampleProvider resampled = sourceSampleRate == OutputSampleRate
            ? source
            : new WdlResamplingSampleProvider(source, OutputSampleRate);
        var pcm16 = resampled.ToWaveProvider16();
        MediaFoundationEncoder.EncodeToAac(pcm16, outputPath, Bitrate);
    }

    public bool CanDecode(string path)
    {
        try
        {
            using var reader = new MediaFoundationReader(path);
            if (reader.TotalTime <= TimeSpan.Zero || reader.Length <= 0)
            {
                return false;
            }

            var probe = new byte[Math.Min(4096, (int)Math.Min(reader.Length, 4096))];
            return probe.Length > 0 && reader.Read(probe, 0, probe.Length) > 0;
        }
        catch
        {
            return false;
        }
    }

    public bool LooksLikeContainer(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 12)
            {
                return false;
            }
            Span<byte> header = stackalloc byte[12];
            return stream.Read(header) == header.Length &&
                   header[4] == (byte)'f' &&
                   header[5] == (byte)'t' &&
                   header[6] == (byte)'y' &&
                   header[7] == (byte)'p';
        }
        catch
        {
            return false;
        }
    }

    public WaveStream OpenForPlayback(string path) => new MediaFoundationReader(path);

    private sealed class FloatArraySampleProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;

        internal FloatArraySampleProvider(float[] samples, int sampleRate)
        {
            _samples = samples;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, _samples.Length - _position);
            if (available <= 0)
            {
                return 0;
            }

            Array.Copy(_samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}

/// <summary>
/// Owns a private, local-only rolling history. The channel deliberately accepts only one waiting
/// take total (queued or encoding): Media Foundation work never runs on the capture callback or UI
/// thread, while a stalled encoder cannot retain another completed float buffer in memory.
/// </summary>
public sealed class RecentRecordingHistoryService : IDisposable
{
    public const int Capacity = 3;

    private const string ManifestName = "history.json";
    private const string ManifestTemporaryName = "history.json.tmp";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _root;
    private readonly IRecentRecordingCodec _codec;
    private readonly Func<IWavePlayer> _playbackFactory;
    private readonly Action<RecentRecordingDurablePoint>? _failureInjector;
    private readonly Channel<PendingRecording> _queue;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _storageGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly object _playbackGate = new();
    private readonly object _generationGate = new();
    private readonly Task _worker;
    private List<RecentRecording> _items = [];
    private IWavePlayer? _playback;
    private WaveStream? _playbackReader;
    private string? _playingId;
    private string? _stateMessage;
    private long _generation;
    private long _playbackGeneration;
    private int _pendingCount;
    private volatile bool _disposed;

    public RecentRecordingHistoryService(string? root = null)
        : this(root, new MediaFoundationRecentRecordingCodec(), null, null)
    {
    }

    internal RecentRecordingHistoryService(
        string? root,
        IRecentRecordingCodec codec,
        Action<RecentRecordingDurablePoint>? failureInjector,
        Func<IWavePlayer>? playbackFactory = null)
    {
        _root = root ?? Path.Combine(
            Egoist.Voice.Core.VoiceRuntimeProfile.DataRoot,
            "History");
        _codec = codec;
        _failureInjector = failureInjector;
        _playbackFactory = playbackFactory ?? (() => new WaveOutEvent());
        _queue = Channel.CreateBounded<PendingRecording>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        RecoverAtStartup();
        _worker = Task.Run(ProcessQueueAsync);
    }

    public event EventHandler? Changed;
    public event EventHandler<RecentRecordingPlaybackChangedEventArgs>? PlaybackChanged;

    public string? StateMessage
    {
        get
        {
            lock (_stateGate)
            {
                return _stateMessage;
            }
        }
    }

    public string? PlayingRecordingId
    {
        get
        {
            lock (_playbackGate)
            {
                return _playingId;
            }
        }
    }

    public IReadOnlyList<RecentRecording> GetItems()
    {
        lock (_stateGate)
        {
            return _items.ToArray();
        }
    }

    /// <summary>
    /// Queues one completed take without waiting for the encoder. False means that a previous take
    /// is queued or encoding; dictation still succeeds and the next take may be queued normally.
    /// </summary>
    public bool TryQueue(float[] samples, int sampleRate, TimeSpan duration, RecentRecordingStatus status)
    {
        if (_disposed || samples.Length == 0 || sampleRate <= 0 || duration <= TimeSpan.Zero)
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _pendingCount, 1, 0) != 0)
        {
            SetStateMessage("История занята предыдущей записью; диктовка продолжает работать.");
            return false;
        }

        var pending = new PendingRecording(
            samples,
            sampleRate,
            duration,
            status,
            DateTime.UtcNow,
            Volatile.Read(ref _generation));
        if (_queue.Writer.TryWrite(pending))
        {
            return true;
        }

        Interlocked.Exchange(ref _pendingCount, 0);
        SetStateMessage("История занята предыдущей записью; диктовка продолжает работать.");
        return false;
    }

    /// <summary>Cancels queued/in-flight persistence without deleting already committed files.</summary>
    public void CancelPending()
    {
        lock (_generationGate)
        {
            Interlocked.Increment(ref _generation);
        }
    }

    public Task<float[]> ReadSamplesForTranscriptionAsync(string recordingId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _storageGate.Wait(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var item = SnapshotItems().FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, recordingId, StringComparison.Ordinal));
                if (item is null)
                    throw new InvalidOperationException("Запись уже удалена. Выберите другую запись в истории.");
                var path = ResolveAudioPath(item.FileName);
                if (!File.Exists(path))
                {
                    RemoveMissingItem(item);
                    throw new InvalidOperationException("Файл записи отсутствует. Выберите другую запись в истории.");
                }
                // Finish decoding under the storage gate: deletion/pruning may proceed once ASR
                // owns this memory, without holding the history file open during inference.
                return AudioSampleReader.ReadMono16Khz(path, cancellationToken);
            }
            finally { _storageGate.Release(); }
        }, cancellationToken);

    public Task<bool> PlayAsync(string recordingId, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.FromResult(false);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        // Every request supersedes older queued playback. StopPlayback increments the same
        // generation synchronously, so start/pause/disable cannot be followed by a stale player
        // that was waiting behind an encode on the storage gate.
        var playbackGeneration = Interlocked.Increment(ref _playbackGeneration);
        return Task.Run(
            () => PlayCore(recordingId, playbackGeneration, cancellationToken),
            cancellationToken);
    }

    private bool PlayCore(
        string recordingId,
        long playbackGeneration,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return false;
        }

        _storageGate.Wait(cancellationToken);
        try
        {
            if (_disposed || playbackGeneration != Volatile.Read(ref _playbackGeneration))
            {
                return false;
            }

            var item = SnapshotItems().FirstOrDefault(candidate =>
                string.Equals(candidate.Id, recordingId, StringComparison.Ordinal));
            if (item is null)
            {
                SetStateMessage("Запись уже удалена. Список обновлён.");
                return false;
            }

            var path = ResolveAudioPath(item.FileName);
            if (!File.Exists(path))
            {
                RemoveMissingItem(item);
                SetStateMessage("Файл записи отсутствовал и удалён из истории.");
                return false;
            }

            lock (_playbackGate)
            {
                // This second check closes the race where StopPlayback invalidates the request
                // after the storage check but before reader/output initialization.
                if (_disposed || playbackGeneration != Volatile.Read(ref _playbackGeneration))
                {
                    return false;
                }

                if (string.Equals(_playingId, recordingId, StringComparison.Ordinal))
                {
                    StopPlaybackCore();
                    RaisePlaybackChanged(null, false);
                    return true;
                }

                StopPlaybackCore();
                try
                {
                    _playbackReader = _codec.OpenForPlayback(path);
                    _playback = _playbackFactory();
                    _playback.PlaybackStopped += OnPlaybackStopped;
                    _playback.Init(_playbackReader);
                    _playingId = recordingId;
                    _playback.Play();
                }
                catch
                {
                    StopPlaybackCore();
                    SetStateMessage("Не удалось воспроизвести запись. Её можно удалить или очистить историю.");
                    return false;
                }
            }

            SetStateMessage(null);
            RaisePlaybackChanged(recordingId, true);
            return true;
        }
        catch (Exception exception)
        {
            SetStateMessage(UserFacingFailure("Не удалось воспроизвести запись.", exception));
            AppLog.Write($"Recent recording playback failed: {exception.GetType().Name}");
            return false;
        }
        finally
        {
            _storageGate.Release();
        }
    }

    public void StopPlayback()
    {
        Interlocked.Increment(ref _playbackGeneration);
        string? stopped;
        lock (_playbackGate)
        {
            stopped = _playingId;
            StopPlaybackCore();
        }

        if (stopped is not null)
        {
            RaisePlaybackChanged(null, false);
        }
    }

    public Task DeleteAsync(string recordingId, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }
        CancelPending();
        StopPlaybackIf(recordingId);
        return Task.Run(() => DeleteCore(recordingId, cancellationToken), cancellationToken);
    }

    private void DeleteCore(string recordingId, CancellationToken cancellationToken)
    {
        StopPlaybackIf(recordingId);
        _storageGate.Wait(cancellationToken);
        try
        {
            if (_disposed)
            {
                return;
            }

            // A newer PlayAsync may have started after DeleteAsync's synchronous stop but before
            // this operation acquired storage. Stop again while storage is exclusively held.
            StopPlaybackIf(recordingId);

            var items = SnapshotItems();
            var item = items.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, recordingId, StringComparison.Ordinal));
            if (item is null)
            {
                return;
            }

            DeleteFileOrThrow(ResolveAudioPath(item.FileName));
            items.Remove(item);
            ReplaceItems(items);
            SaveManifest(items);
            SetStateMessage(null);
        }
        catch (Exception exception)
        {
            SetStateMessage(UserFacingFailure("Не удалось удалить запись.", exception));
            AppLog.Write($"Recent recording delete failed: {exception.GetType().Name}");
        }
        finally
        {
            _storageGate.Release();
            RaiseChanged();
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }
        CancelPending();
        StopPlayback();
        return Task.Run(() => ClearCore(cancellationToken), cancellationToken);
    }

    private void ClearCore(CancellationToken cancellationToken)
    {
        StopPlayback();
        _storageGate.Wait(cancellationToken);
        try
        {
            if (_disposed)
            {
                return;
            }


            // Pair the synchronous invalidation with a stop under exclusive storage ownership;
            // this catches a newer playback request that won the gate before ClearCore.
            StopPlayback();

            Directory.CreateDirectory(_root);
            foreach (var file in EnumerateAudioBearingFiles())
            {
                DeleteFileOrThrow(file);
            }

            ReplaceItems([]);
            SaveManifest([]);
            SetStateMessage("История очищена.");
        }
        catch (Exception exception)
        {
            SetStateMessage(UserFacingFailure("Не удалось полностью очистить историю.", exception));
            AppLog.Write($"Recent recording clear failed: {exception.GetType().Name}");
        }
        finally
        {
            _storageGate.Release();
            RaiseChanged();
        }
    }

    internal async Task WaitForIdleAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref _pendingCount) > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        if (Volatile.Read(ref _pendingCount) > 0)
        {
            throw new TimeoutException("Recent recording queue did not become idle.");
        }
    }

    internal int PhysicalAudioFileCount => Directory.Exists(_root)
        ? EnumerateAudioBearingFiles().Count()
        : 0;

    internal bool IsDisposed => _disposed;

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var pending in _queue.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                try
                {
                    if (pending.Generation == Volatile.Read(ref _generation))
                    {
                        await PersistAsync(pending, _lifetime.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (SimulatedHistoryCrashException)
                {
                    // Tests deliberately leave the exact durable state behind for next-start recovery.
                }
                catch (Exception exception)
                {
                    SetStateMessage(UserFacingFailure("Не удалось сохранить запись.", exception));
                    AppLog.Write($"Recent recording save failed: {exception.GetType().Name}");
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingCount);
                    RaiseChanged();
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task PersistAsync(PendingRecording pending, CancellationToken cancellationToken)
    {
        await _storageGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        string? promotedFinalPath = null;
        PrunedRecordingBackup? prunedBackup = null;
        var preserveCrashState = false;
        var committed = false;
        var rolledBack = false;
        try
        {
            Directory.CreateDirectory(_root);
            RecoverCore(reportRecovery: false);
            var items = SnapshotItems();

            // Cancellation and the destructive reservation are linearized together. The lock is
            // held only for bounded local file operations, never during Media Foundation encode.
            // If cancellation arrives during encode, the one pruned compressed file is restored
            // before the worker releases the storage gate.
            lock (_generationGate)
            {
                if (pending.Generation != Volatile.Read(ref _generation))
                {
                    return;
                }
                cancellationToken.ThrowIfCancellationRequested();

                while (PhysicalAudioFileCount >= Capacity || items.Count >= Capacity)
                {
                    var oldest = items.OrderBy(item => item.CreatedUtc).FirstOrDefault();
                    if (oldest is null)
                    {
                        var unindexed = EnumerateAudioBearingFiles()
                            .OrderBy(File.GetCreationTimeUtc)
                            .FirstOrDefault();
                        if (unindexed is null)
                        {
                            break;
                        }
                        DeleteFileOrThrow(unindexed);
                    }
                    else
                    {
                        StopPlaybackIf(oldest.Id);
                        var oldestPath = ResolveAudioPath(oldest.FileName);
                        prunedBackup = new PrunedRecordingBackup(
                            oldestPath,
                            File.ReadAllBytes(oldestPath),
                            [.. items]);
                        DeleteFileOrThrow(oldestPath);
                        items.Remove(oldest);
                    }
                    Checkpoint(RecentRecordingDurablePoint.OldestAudioDeleted);
                    ReplaceItems(items);
                    SaveManifest(items, RecentRecordingDurablePoint.PrunedManifestTemporaryWritten);
                    Checkpoint(RecentRecordingDurablePoint.PrunedManifestCommitted);
                }

                var id = Guid.NewGuid().ToString("N");
                var fileName = id + ".m4a";
                // Media Foundation selects/opens the MP4 container from the final extension.
                // Keeping .m4a last lets the temp be probed before atomic promotion.
                temporaryPath = ResolveAudioPath(id + ".tmp.m4a");
                using (File.Create(temporaryPath))
                {
                }
                Checkpoint(RecentRecordingDurablePoint.TemporaryCreated);
            }

            await Task.Run(
                () => _codec.Encode(pending.Samples, pending.SampleRate, temporaryPath),
                cancellationToken).ConfigureAwait(false);
            if (!_codec.CanDecode(temporaryPath))
            {
                throw new InvalidDataException("Encoded recording could not be reopened.");
            }
            Checkpoint(RecentRecordingDurablePoint.TemporaryFinalized);

            lock (_generationGate)
            {
                if (pending.Generation != Volatile.Read(ref _generation))
                {
                    DeleteFileOrThrow(temporaryPath);
                    temporaryPath = null;
                    if (prunedBackup is not null)
                    {
                        File.WriteAllBytes(prunedBackup.Path, prunedBackup.Bytes);
                        ReplaceItems(prunedBackup.OriginalItems);
                        SaveManifest(prunedBackup.OriginalItems);
                    }
                    rolledBack = true;
                    return;
                }

                var id = Path.GetFileName(temporaryPath)[..32];
                var fileName = id + ".m4a";
                var finalPath = ResolveAudioPath(fileName);
                File.Move(temporaryPath, finalPath);
                temporaryPath = null;
                promotedFinalPath = finalPath;
                Checkpoint(RecentRecordingDurablePoint.FinalPromoted);
                items.Insert(0, new RecentRecording(
                    id,
                    pending.CreatedUtc,
                    pending.Duration,
                    pending.Status,
                    fileName));
                SaveManifest(items, RecentRecordingDurablePoint.FinalManifestTemporaryWritten);
                ReplaceItems(items);
                SetStateMessage(null);
                committed = true;
                Checkpoint(RecentRecordingDurablePoint.ManifestCommitted);
            }
        }
        catch (SimulatedHistoryCrashException)
        {
            preserveCrashState = true;
            throw;
        }
        finally
        {
            try
            {
                if (!preserveCrashState && !committed && !rolledBack)
                {
                    lock (_generationGate)
                    {
                        if (temporaryPath is not null)
                        {
                            DeleteFileOrThrow(temporaryPath);
                        }
                        if (promotedFinalPath is not null)
                        {
                            DeleteFileOrThrow(promotedFinalPath);
                        }
                        if (prunedBackup is not null)
                        {
                            File.WriteAllBytes(prunedBackup.Path, prunedBackup.Bytes);
                            ReplaceItems(prunedBackup.OriginalItems);
                            SaveManifest(prunedBackup.OriginalItems);
                        }
                    }
                }
            }
            finally
            {
                _storageGate.Release();
            }
        }
    }

    private void RecoverAtStartup()
    {
        try
        {
            Directory.CreateDirectory(_root);
            RecoverCore(reportRecovery: true);
        }
        catch (Exception exception)
        {
            ReplaceItems([]);
            SetStateMessage(UserFacingFailure("История временно недоступна.", exception));
            AppLog.Write($"Recent recording startup recovery failed: {exception.GetType().Name}");
        }
    }

    private void RecoverCore(bool reportRecovery)
    {
        Directory.CreateDirectory(_root);
        DeleteFileOrThrow(Path.Combine(_root, ManifestTemporaryName));
        var changed = false;
        var manifestValid = TryLoadManifest(out var items);
        if (!manifestValid)
        {
            items = [];
            changed = true;
        }

        var valid = new List<RecentRecording>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.OrderByDescending(item => item.CreatedUtc))
        {
            if (valid.Count >= Capacity ||
                !Guid.TryParseExact(item.Id, "N", out _) ||
                !IsSafeAudioFileName(item.FileName) ||
                !string.Equals(item.FileName, item.Id + ".m4a", StringComparison.OrdinalIgnoreCase) ||
                item.CreatedUtc == default ||
                item.Duration <= TimeSpan.Zero ||
                !seenIds.Add(item.Id) ||
                !seenFiles.Add(item.FileName))
            {
                changed = true;
                continue;
            }

            var path = ResolveAudioPath(item.FileName);
            // Startup recovery must not destroy valid history merely because Media Foundation is
            // temporarily unavailable. A cheap MP4 signature rejects obvious corruption without
            // opening the codec; actual decoding is verified at encode and playback boundaries.
            if (!File.Exists(path) || !_codec.LooksLikeContainer(path))
            {
                DeleteFileOrThrow(path);
                changed = true;
                continue;
            }

            valid.Add(item);
        }

        var indexed = valid.Select(item => item.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateAudioBearingFiles().ToArray())
        {
            if (!indexed.Contains(Path.GetFileName(path)))
            {
                DeleteFileOrThrow(path);
                changed = true;
            }
        }

        while (valid.Count > Capacity)
        {
            var oldest = valid[^1];
            DeleteFileOrThrow(ResolveAudioPath(oldest.FileName));
            valid.RemoveAt(valid.Count - 1);
            changed = true;
        }

        if (changed || !File.Exists(ManifestPath))
        {
            SaveManifest(valid);
        }
        ReplaceItems(valid);
        if (reportRecovery && changed)
        {
            SetStateMessage("История восстановлена: незавершённые или повреждённые файлы удалены.");
        }
    }

    private bool TryLoadManifest(out List<RecentRecording> items)
    {
        items = [];
        try
        {
            if (!File.Exists(ManifestPath))
            {
                return true;
            }

            var manifest = JsonSerializer.Deserialize<HistoryManifest>(File.ReadAllText(ManifestPath), Json);
            if (manifest?.Version != 1 || manifest.Items is null)
            {
                return false;
            }

            items = manifest.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveManifest(
        IReadOnlyList<RecentRecording> items,
        RecentRecordingDurablePoint? temporaryWrittenCheckpoint = null)
    {
        Directory.CreateDirectory(_root);
        var temporary = Path.Combine(_root, ManifestTemporaryName);
        File.WriteAllText(temporary, JsonSerializer.Serialize(new HistoryManifest(1, items), Json));
        if (temporaryWrittenCheckpoint is { } checkpoint)
        {
            Checkpoint(checkpoint);
        }
        File.Move(temporary, ManifestPath, overwrite: true);
    }

    private void RemoveMissingItem(RecentRecording item)
    {
        var items = SnapshotItems();
        items.RemoveAll(candidate => string.Equals(candidate.Id, item.Id, StringComparison.Ordinal));
        SaveManifest(items);
        ReplaceItems(items);
        RaiseChanged();
    }

    private List<RecentRecording> SnapshotItems()
    {
        lock (_stateGate)
        {
            return [.. _items];
        }
    }

    private void ReplaceItems(IEnumerable<RecentRecording> items)
    {
        lock (_stateGate)
        {
            _items = items.OrderByDescending(item => item.CreatedUtc).Take(Capacity).ToList();
        }
    }

    private void SetStateMessage(string? message)
    {
        var changed = false;
        lock (_stateGate)
        {
            changed = !string.Equals(_stateMessage, message, StringComparison.Ordinal);
            _stateMessage = message;
        }
        if (changed)
        {
            RaiseChanged();
        }
    }

    private void StopPlaybackIf(string recordingId)
    {
        Interlocked.Increment(ref _playbackGeneration);
        lock (_playbackGate)
        {
            if (string.Equals(_playingId, recordingId, StringComparison.Ordinal))
            {
                StopPlaybackCore();
            }
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
    {
        lock (_playbackGate)
        {
            if (!ReferenceEquals(sender, _playback))
            {
                return;
            }
            StopPlaybackCore();
        }
        RaisePlaybackChanged(null, false);
    }

    private void StopPlaybackCore()
    {
        if (_playback is not null)
        {
            _playback.PlaybackStopped -= OnPlaybackStopped;
            _playback.Stop();
            _playback.Dispose();
            _playback = null;
        }
        _playbackReader?.Dispose();
        _playbackReader = null;
        _playingId = null;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void RaisePlaybackChanged(string? recordingId, bool isPlaying) =>
        PlaybackChanged?.Invoke(this, new RecentRecordingPlaybackChangedEventArgs(recordingId, isPlaying));

    private void Checkpoint(RecentRecordingDurablePoint point) => _failureInjector?.Invoke(point);

    private string ManifestPath => Path.Combine(_root, ManifestName);

    private string ResolveAudioPath(string fileName)
    {
        if (!IsSafeAudioFileName(fileName) && !IsSafeTemporaryAudioFileName(fileName))
        {
            throw new InvalidDataException("Unsafe history file name.");
        }
        return Path.Combine(_root, fileName);
    }

    private static bool IsSafeAudioFileName(string fileName) =>
        string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) &&
        fileName.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) &&
        fileName.Length == 36;

    private static bool IsSafeTemporaryAudioFileName(string fileName) =>
        string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) &&
        fileName.EndsWith(".tmp.m4a", StringComparison.OrdinalIgnoreCase) &&
        fileName.Length == 40;

    private IEnumerable<string> EnumerateAudioBearingFiles()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        return Directory.EnumerateFiles(_root, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals(ManifestTemporaryName, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // The caller will either surface a state message or retry this bounded recovery later.
        }
    }

    private static void DeleteFileOrThrow(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string UserFacingFailure(string fallback, Exception exception)
    {
        const int DiskFull = unchecked((int)0x80070070);
        const int HandleDiskFull = unchecked((int)0x80070027);
        return exception.HResult is DiskFull or HandleDiskFull
            ? "Недостаточно места для локальной истории. Диктовка продолжает работать."
            : exception is PlatformNotSupportedException or NotSupportedException
                ? "Кодек AAC недоступен. История отключена для этой записи; диктовка продолжает работать."
                : fallback + " Диктовка продолжает работать.";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        CancelPending();
        StopPlayback();
        _queue.Writer.TryComplete();
        _lifetime.Cancel();
        try
        {
            _worker.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }


        // Drain Play/Delete/Clear calls that passed their public disposed check before shutdown.
        // Each core checks _disposed again after acquiring this gate, so none can create a reader,
        // output device or file operation after this point. A second stop catches an operation that
        // had already initialized playback before Dispose began.
        _storageGate.Wait();
        try
        {
            StopPlayback();
        }
        finally
        {
            _storageGate.Release();
        }
        _lifetime.Dispose();
    }

    private sealed record HistoryManifest(int Version, IReadOnlyList<RecentRecording> Items);

    private sealed record PendingRecording(
        float[] Samples,
        int SampleRate,
        TimeSpan Duration,
        RecentRecordingStatus Status,
        DateTime CreatedUtc,
        long Generation);

    private sealed record PrunedRecordingBackup(
        string Path,
        byte[] Bytes,
        IReadOnlyList<RecentRecording> OriginalItems);
}
