using System.IO;
using Egoist.Voice.Services;
using NAudio.Wave;

namespace Egoist.Voice.Tests;

public sealed class RecentRecordingHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "EgoistVoiceHistoryTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Failed_recording_decodes_for_retry_and_survives_deletion_in_memory()
    {
        using var service = new RecentRecordingHistoryService(_root);
        Assert.True(service.TryQueue(CodecSamples(1), 16_000, TimeSpan.FromSeconds(0.5), RecentRecordingStatus.ProcessingFailed));
        await service.WaitForIdleAsync(TimeSpan.FromSeconds(3));
        var item = Assert.Single(service.GetItems());
        var decoded = await service.ReadSamplesForTranscriptionAsync(item.Id);
        Assert.InRange(decoded.Length, 7_500, 10_000);
        Assert.Contains(decoded, sample => Math.Abs(sample) > 0.02f);
        await service.DeleteAsync(item.Id);
        Assert.Empty(service.GetItems());
        Assert.Contains(decoded, sample => Math.Abs(sample) > 0.02f);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadSamplesForTranscriptionAsync(item.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadSamplesForTranscriptionAsync("../outside.m4a"));
    }

    [Fact]
    public async Task Cancelled_history_retry_preserves_recording()
    {
        using var service = new RecentRecordingHistoryService(_root);
        Assert.True(service.TryQueue(CodecSamples(1), 16_000, TimeSpan.FromSeconds(0.5), RecentRecordingStatus.ProcessingFailed));
        await service.WaitForIdleAsync(TimeSpan.FromSeconds(3));
        var item = Assert.Single(service.GetItems());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadSamplesForTranscriptionAsync(item.Id, cancellation.Token));
        Assert.Single(service.GetItems());
        Assert.Equal(1, service.PhysicalAudioFileCount);
    }

    [Fact]
    public async Task Ten_completed_takes_restart_as_exact_latest_three()
    {
        using (var service = new RecentRecordingHistoryService(_root))
        {
            for (var index = 1; index <= 10; index++)
            {
                Assert.True(service.TryQueue(
                    CodecSamples(index),
                    16_000,
                    TimeSpan.FromSeconds(index),
                    index == 10 ? RecentRecordingStatus.ProcessingFailed : RecentRecordingStatus.Recognized));
                await service.WaitForIdleAsync(TimeSpan.FromSeconds(3));
                Assert.InRange(service.PhysicalAudioFileCount, 0, RecentRecordingHistoryService.Capacity);
            }
        }

        using var restarted = new RecentRecordingHistoryService(_root);
        var latest = restarted.GetItems();
        Assert.Equal(3, latest.Count);
        Assert.Equal(new[] { 10d, 9d, 8d }, latest.Select(item => item.Duration.TotalSeconds));
        Assert.Equal(RecentRecordingStatus.ProcessingFailed, latest[0].Status);
        Assert.Equal(3, restarted.PhysicalAudioFileCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Every_durable_crash_point_keeps_physical_limit_and_recovers(int crashPointValue)
    {
        var crashPoint = (RecentRecordingDurablePoint)crashPointValue;
        using (var seed = CreateService())
        {
            for (var index = 1; index <= 3; index++)
            {
                Assert.True(seed.TryQueue(Samples(index), 16_000, TimeSpan.FromSeconds(index), RecentRecordingStatus.Recognized));
                await seed.WaitForIdleAsync(TimeSpan.FromSeconds(3));
            }
        }

        using (var crashed = new RecentRecordingHistoryService(
                   _root,
                   new FakeCodec(),
                   point =>
                   {
                       if (point == crashPoint)
                       {
                           throw new SimulatedHistoryCrashException(point);
                       }
                   }))
        {
            Assert.True(crashed.TryQueue(Samples(4), 16_000, TimeSpan.FromSeconds(4), RecentRecordingStatus.Recognized));
            await crashed.WaitForIdleAsync(TimeSpan.FromSeconds(3));
            Assert.InRange(crashed.PhysicalAudioFileCount, 0, RecentRecordingHistoryService.Capacity);
        }

        using var recovered = CreateService();
        Assert.InRange(recovered.PhysicalAudioFileCount, 0, RecentRecordingHistoryService.Capacity);
        Assert.Equal(recovered.PhysicalAudioFileCount, recovered.GetItems().Count);
    }

    [Fact]
    public async Task Delete_and_clear_release_the_physical_files()
    {
        using var service = CreateService();
        for (var index = 1; index <= 3; index++)
        {
            Assert.True(service.TryQueue(Samples(index), 16_000, TimeSpan.FromSeconds(index), RecentRecordingStatus.Recognized));
            await service.WaitForIdleAsync(TimeSpan.FromSeconds(3));
        }

        await service.DeleteAsync(service.GetItems()[1].Id);
        Assert.Equal(2, service.GetItems().Count);
        Assert.Equal(2, service.PhysicalAudioFileCount);

        await service.ClearAsync();
        Assert.Empty(service.GetItems());
        Assert.Equal(0, service.PhysicalAudioFileCount);
    }

    [Fact]
    public async Task Playback_toggle_delete_and_clear_release_reader_and_output_handles()
    {
        var outputs = new List<FakeWavePlayer>();
        using var service = new RecentRecordingHistoryService(
            _root,
            new FakeCodec(),
            null,
            () =>
            {
                var output = new FakeWavePlayer();
                outputs.Add(output);
                return output;
            });
        Assert.True(service.TryQueue(Samples(1), 16_000, TimeSpan.FromSeconds(1), RecentRecordingStatus.Recognized));
        await service.WaitForIdleAsync(TimeSpan.FromSeconds(3));
        var id = service.GetItems().Single().Id;

        Assert.True(await service.PlayAsync(id));
        Assert.Equal(id, service.PlayingRecordingId);
        Assert.True(outputs[0].Played);

        Assert.True(await service.PlayAsync(id));
        Assert.Null(service.PlayingRecordingId);
        Assert.True(outputs[0].Stopped);
        Assert.True(outputs[0].Disposed);

        Assert.True(await service.PlayAsync(id));
        await service.DeleteAsync(id);
        Assert.Null(service.PlayingRecordingId);
        Assert.True(outputs[1].Stopped);
        Assert.True(outputs[1].Disposed);
        Assert.Equal(0, service.PhysicalAudioFileCount);
    }

    [Fact]
    public void Startup_removes_corrupt_manifest_stale_temp_and_unindexed_audio()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "history.json"), "not-json");
        File.WriteAllBytes(Path.Combine(_root, "orphan.m4a"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_root, "interrupted.m4a.tmp"), [4, 5, 6]);

        using var service = CreateService();

        Assert.Empty(service.GetItems());
        Assert.Equal(0, service.PhysicalAudioFileCount);
        Assert.Contains("восстановлена", service.StateMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Manifest_contains_only_bounded_non_content_metadata()
    {
        using var service = CreateService();
        Assert.True(service.TryQueue(Samples(2), 16_000, TimeSpan.FromSeconds(2), RecentRecordingStatus.Recognized));
        await service.WaitForIdleAsync(TimeSpan.FromSeconds(3));

        var manifest = File.ReadAllText(Path.Combine(_root, "history.json"));
        foreach (var forbidden in new[]
                 {
                     "transcript", "targetApplication", "window", "device", "translation",
                     "audioPath", "audioContent"
                 })
        {
            Assert.DoesNotContain(forbidden, manifest, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain(".wav", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duration", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", manifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_codec_and_disk_full_are_fail_open_and_leave_no_audio()
    {
        using (var missingCodec = new RecentRecordingHistoryService(
                   Path.Combine(_root, "codec"),
                   new ThrowingCodec(new PlatformNotSupportedException()),
                   null))
        {
            Assert.True(missingCodec.TryQueue(Samples(1), 16_000, TimeSpan.FromSeconds(1), RecentRecordingStatus.Recognized));
            await missingCodec.WaitForIdleAsync(TimeSpan.FromSeconds(3));
            Assert.Empty(missingCodec.GetItems());
            Assert.Equal(0, missingCodec.PhysicalAudioFileCount);
            Assert.Contains("кодек", missingCodec.StateMessage, StringComparison.OrdinalIgnoreCase);
        }

        using var diskFull = new RecentRecordingHistoryService(
            Path.Combine(_root, "disk"),
            new ThrowingCodec(new DiskFullIOException()),
            null);
        Assert.True(diskFull.TryQueue(Samples(1), 16_000, TimeSpan.FromSeconds(1), RecentRecordingStatus.Recognized));
        await diskFull.WaitForIdleAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(diskFull.GetItems());
        Assert.Equal(0, diskFull.PhysicalAudioFileCount);
        Assert.Contains("места", diskFull.StateMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Encoder_unavailable_does_not_delete_an_existing_valid_history()
    {
        using (var seed = CreateService())
        {
            Assert.True(seed.TryQueue(Samples(1), 16_000, TimeSpan.FromSeconds(1), RecentRecordingStatus.Recognized));
            await seed.WaitForIdleAsync(TimeSpan.FromSeconds(3));
        }

        using var unavailable = new RecentRecordingHistoryService(
            _root,
            new UnavailableEncoderCodec(),
            null);
        Assert.Single(unavailable.GetItems());
        Assert.True(unavailable.TryQueue(Samples(2), 16_000, TimeSpan.FromSeconds(2), RecentRecordingStatus.Recognized));
        await unavailable.WaitForIdleAsync(TimeSpan.FromSeconds(3));

        Assert.Single(unavailable.GetItems());
        Assert.Equal(1, unavailable.PhysicalAudioFileCount);
        Assert.Contains("кодек", unavailable.StateMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disabling_history_while_encoding_discards_the_inflight_take()
    {
        using var codec = new BlockingCodec();
        using var service = new RecentRecordingHistoryService(_root, codec, null);
        Assert.True(service.TryQueue(Samples(1), 16_000, TimeSpan.FromSeconds(1), RecentRecordingStatus.Recognized));
        Assert.True(codec.Started.Wait(TimeSpan.FromSeconds(3)));
        Assert.False(service.TryQueue(Samples(2), 16_000, TimeSpan.FromSeconds(2), RecentRecordingStatus.Recognized));

        service.CancelPending();
        codec.Release.Set();
        await service.WaitForIdleAsync(TimeSpan.FromSeconds(3));

        Assert.Empty(service.GetItems());
        Assert.Equal(0, service.PhysicalAudioFileCount);
    }

    [Fact]
    public async Task Disabling_during_a_full_capacity_encode_restores_all_committed_items()
    {
        using (var seed = CreateService())
        {
            for (var index = 1; index <= 3; index++)
            {
                Assert.True(seed.TryQueue(Samples(index), 16_000, TimeSpan.FromSeconds(index), RecentRecordingStatus.Recognized));
                await seed.WaitForIdleAsync(TimeSpan.FromSeconds(3));
            }
        }

        using var codec = new BlockingCodec();
        using (var service = new RecentRecordingHistoryService(_root, codec, null))
        {
            Assert.True(service.TryQueue(Samples(4), 16_000, TimeSpan.FromSeconds(4), RecentRecordingStatus.Recognized));
            Assert.True(codec.Started.Wait(TimeSpan.FromSeconds(3)));

            service.CancelPending();
            codec.Release.Set();
            await service.WaitForIdleAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(new[] { 3d, 2d, 1d }, service.GetItems().Select(item => item.Duration.TotalSeconds));
            Assert.Equal(3, service.PhysicalAudioFileCount);
        }

        using var restarted = CreateService();
        Assert.Equal(new[] { 3d, 2d, 1d }, restarted.GetItems().Select(item => item.Duration.TotalSeconds));
        Assert.Equal(3, restarted.PhysicalAudioFileCount);
    }

    [Fact]
    public async Task Playback_codec_and_output_initialization_never_run_on_the_calling_ui_thread()
    {
        var codec = new FakeCodec();
        using var service = new RecentRecordingHistoryService(
            _root,
            codec,
            null,
            () => new FakeWavePlayer());
        Assert.True(service.TryQueue(Samples(1), 16_000, TimeSpan.FromSeconds(1), RecentRecordingStatus.Recognized));
        await service.WaitForIdleAsync(TimeSpan.FromSeconds(3));
        var id = service.GetItems().Single().Id;
        var callingThreadId = -1;
        Exception? failure = null;
        var played = false;
        var uiThread = new Thread(() =>
        {
            callingThreadId = Environment.CurrentManagedThreadId;
            try
            {
                played = service.PlayAsync(id).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        uiThread.Start();
        Assert.True(uiThread.Join(TimeSpan.FromSeconds(3)));

        Assert.Null(failure);
        Assert.True(played);
        Assert.NotEqual(callingThreadId, codec.OpenThreadId);
        service.StopPlayback();
    }

    [Fact]
    public async Task Exit_drains_playback_waiting_behind_a_blocked_encode_without_opening_new_handles()
    {
        using (var seed = CreateService())
        {
            for (var index = 1; index <= 3; index++)
            {
                Assert.True(seed.TryQueue(Samples(index), 16_000, TimeSpan.FromSeconds(index), RecentRecordingStatus.Recognized));
                await seed.WaitForIdleAsync(TimeSpan.FromSeconds(3));
            }
        }

        using var codec = new BlockingCodec();
        var outputs = new List<FakeWavePlayer>();
        var service = new RecentRecordingHistoryService(
            _root,
            codec,
            null,
            () =>
            {
                var output = new FakeWavePlayer();
                outputs.Add(output);
                return output;
            });
        Assert.True(service.TryQueue(Samples(4), 16_000, TimeSpan.FromSeconds(4), RecentRecordingStatus.Recognized));
        Assert.True(codec.Started.Wait(TimeSpan.FromSeconds(3)));
        var playback = service.PlayAsync(service.GetItems().First().Id);
        var disposing = Task.Run(service.Dispose);
        Assert.True(SpinWait.SpinUntil(() => service.IsDisposed, TimeSpan.FromSeconds(3)));

        codec.Release.Set();
        await disposing.WaitAsync(TimeSpan.FromSeconds(3));
        var played = await playback.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(played);
        Assert.Empty(outputs);
        Assert.Null(service.PlayingRecordingId);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("delete")]
    [InlineData("clear")]
    public async Task Stop_delete_and_clear_fence_playback_waiting_behind_a_blocked_encode(string action)
    {
        using (var seed = CreateService())
        {
            for (var index = 1; index <= 3; index++)
            {
                Assert.True(seed.TryQueue(Samples(index), 16_000, TimeSpan.FromSeconds(index), RecentRecordingStatus.Recognized));
                await seed.WaitForIdleAsync(TimeSpan.FromSeconds(3));
            }
        }

        using var codec = new BlockingCodec();
        var outputs = new List<FakeWavePlayer>();
        using var service = new RecentRecordingHistoryService(
            _root,
            codec,
            null,
            () =>
            {
                var output = new FakeWavePlayer();
                outputs.Add(output);
                return output;
            });
        Assert.True(service.TryQueue(Samples(4), 16_000, TimeSpan.FromSeconds(4), RecentRecordingStatus.Recognized));
        Assert.True(codec.Started.Wait(TimeSpan.FromSeconds(3)));
        var recordingId = service.GetItems().First().Id;
        var playback = service.PlayAsync(recordingId);
        Task operation = action switch
        {
            "delete" => service.DeleteAsync(recordingId),
            "clear" => service.ClearAsync(),
            _ => Task.CompletedTask
        };
        if (action == "stop")
        {
            // Main-window start/pause and settings-disable all use this same synchronous fence.
            service.StopPlayback();
        }

        codec.Release.Set();
        await operation.WaitAsync(TimeSpan.FromSeconds(3));
        var played = await playback.WaitAsync(TimeSpan.FromSeconds(3));
        await service.WaitForIdleAsync(TimeSpan.FromSeconds(3));

        Assert.False(played);
        Assert.Empty(outputs);
        Assert.Null(service.PlayingRecordingId);
        if (action == "delete")
        {
            Assert.DoesNotContain(service.GetItems(), item => item.Id == recordingId);
        }
        else if (action == "clear")
        {
            Assert.Empty(service.GetItems());
            Assert.Equal(0, service.PhysicalAudioFileCount);
        }
    }

    [Fact]
    public void Persistence_policy_rejects_silence_cancel_pause_and_disabled_history()
    {
        var accepted = Capture(hasSpeech: true);
        var silence = Capture(hasSpeech: false);

        Assert.True(RecentRecordingPersistencePolicy.ShouldQueue(accepted, false, true));
        Assert.False(RecentRecordingPersistencePolicy.ShouldQueue(silence, false, true));
        Assert.False(RecentRecordingPersistencePolicy.ShouldQueue(accepted, true, true));
        Assert.False(RecentRecordingPersistencePolicy.ShouldQueue(accepted, false, false));
        Assert.False(RecentRecordingPersistencePolicy.ShouldQueue(null, false, true));
    }

    [Fact]
    public void Production_codec_finalizes_bounded_m4a_and_reopens_it_after_new_instance()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "codec-spike.m4a");
        var codec = new MediaFoundationRecentRecordingCodec();
        var samples = Enumerable.Range(0, 16_000)
            .Select(index => (float)(0.15 * Math.Sin(2 * Math.PI * 440 * index / 16_000d)))
            .ToArray();

        codec.Encode(samples, 16_000, path);

        Assert.InRange(new FileInfo(path).Length, 1_024, 20_000);
        var restartedCodec = new MediaFoundationRecentRecordingCodec();
        Assert.True(restartedCodec.LooksLikeContainer(path));
        Assert.True(restartedCodec.CanDecode(path));
        using var reader = restartedCodec.OpenForPlayback(path);
        var buffer = new byte[4096];
        Assert.True(reader.Read(buffer, 0, buffer.Length) > 0);
        Assert.Equal(48_000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
    }

    [Fact]
    public void History_implementation_has_no_network_or_upload_path()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services", "RecentRecordingHistoryService.cs"));

        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WebRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", source, StringComparison.OrdinalIgnoreCase);
        foreach (var logLine in source.Split('\n').Where(line => line.Contains("AppLog.Write", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("path", logLine, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fileName", logLine, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recordingId", logLine, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(", exception", logLine, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Disabling_from_settings_stops_playback_and_uses_a_non_destructive_default()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "SettingsWindow.xaml.cs"));
        var start = source.IndexOf("private async void HistorySave_OnChanged", StringComparison.Ordinal);
        var end = source.IndexOf("private void SaveHistorySetting", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var handler = source[start..end];

        Assert.Contains("MessageBoxResult.No", handler, StringComparison.Ordinal);
        Assert.Contains("_recentRecordings.StopPlayback();", handler, StringComparison.Ordinal);
        Assert.Contains("_recentRecordings.CancelPending();", handler, StringComparison.Ordinal);
        Assert.True(
            handler.IndexOf("StopPlayback", StringComparison.Ordinal) <
            handler.IndexOf("ClearAsync", StringComparison.Ordinal));
    }

    private RecentRecordingHistoryService CreateService() => new(_root, new FakeCodec(), null);

    private static float[] Samples(int marker) => Enumerable.Repeat(marker / 20f, 160).ToArray();

    private static float[] CodecSamples(int marker) => Enumerable.Range(0, 8_000)
        .Select(index => (float)(0.12 * Math.Sin(2 * Math.PI * (320 + marker) * index / 16_000d)))
        .ToArray();

    private static AudioCaptureResult Capture(bool hasSpeech) => new(
        null,
        Samples(1),
        16_000,
        hasSpeech,
        TimeSpan.FromSeconds(1),
        hasSpeech ? TimeSpan.FromSeconds(0.8) : TimeSpan.Zero,
        -12);

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Egoist.Voice.csproj")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeCodec : IRecentRecordingCodec
    {
        internal int OpenThreadId { get; private set; } = -1;

        public void Encode(float[] samples, int sourceSampleRate, string outputPath) =>
            File.WriteAllBytes(outputPath, [0x45, 0x56, 0x48, (byte)Math.Clamp(samples[0] * 20, 0, 255)]);

        public bool CanDecode(string path) =>
            File.Exists(path) && File.ReadAllBytes(path) is [0x45, 0x56, 0x48, _];

        public bool LooksLikeContainer(string path) => CanDecode(path);

        public WaveStream OpenForPlayback(string path)
        {
            OpenThreadId = Environment.CurrentManagedThreadId;
            return new RawSourceWaveStream(
                new MemoryStream(new byte[3200], writable: false),
                new WaveFormat(16_000, 16, 1));
        }
    }

    private sealed class ThrowingCodec(Exception exception) : IRecentRecordingCodec
    {
        public void Encode(float[] samples, int sourceSampleRate, string outputPath) => throw exception;
        public bool CanDecode(string path) => false;
        public bool LooksLikeContainer(string path) => false;
        public WaveStream OpenForPlayback(string path) => throw exception;
    }

    private sealed class UnavailableEncoderCodec : IRecentRecordingCodec
    {
        private readonly FakeCodec _existing = new();

        public void Encode(float[] samples, int sourceSampleRate, string outputPath) =>
            throw new PlatformNotSupportedException();

        public bool CanDecode(string path) => _existing.CanDecode(path);
        public bool LooksLikeContainer(string path) => _existing.LooksLikeContainer(path);
        public WaveStream OpenForPlayback(string path) => _existing.OpenForPlayback(path);
    }

    private sealed class DiskFullIOException : IOException
    {
        internal DiskFullIOException()
        {
            HResult = unchecked((int)0x80070070);
        }
    }

    private sealed class BlockingCodec : IRecentRecordingCodec, IDisposable
    {
        internal ManualResetEventSlim Started { get; } = new(false);
        internal ManualResetEventSlim Release { get; } = new(false);

        public void Encode(float[] samples, int sourceSampleRate, string outputPath)
        {
            Started.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(3)))
            {
                throw new TimeoutException("Blocking codec was not released.");
            }
            File.WriteAllBytes(outputPath, [0x45, 0x56, 0x48, 1]);
        }

        public bool CanDecode(string path) => File.Exists(path);

        public bool LooksLikeContainer(string path) => File.Exists(path);

        public WaveStream OpenForPlayback(string path) => new RawSourceWaveStream(
            new MemoryStream(new byte[3200], writable: false),
            new WaveFormat(16_000, 16, 1));

        public void Dispose()
        {
            Started.Dispose();
            Release.Dispose();
        }
    }

    private sealed class FakeWavePlayer : IWavePlayer
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public float Volume { get; set; } = 1;
        public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
        public WaveFormat OutputWaveFormat { get; private set; } = new(16_000, 16, 1);
        internal bool Played { get; private set; }
        internal bool Stopped { get; private set; }
        internal bool Disposed { get; private set; }

        public void Init(IWaveProvider waveProvider)
        {
            OutputWaveFormat = waveProvider.WaveFormat;
        }

        public void Play()
        {
            Played = true;
            PlaybackState = PlaybackState.Playing;
        }

        public void Pause() => PlaybackState = PlaybackState.Paused;

        public void Stop()
        {
            Stopped = true;
            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs(null));
        }

        public void Dispose()
        {
            Disposed = true;
            PlaybackStopped = null;
        }
    }
}
