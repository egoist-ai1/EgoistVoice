using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Egoist.Voice.Services;

/// <summary>
/// A continuously warm shared-mode WASAPI capture. Only a bounded pre-roll lives while idle;
/// accepted dictation stays in memory and is resampled exactly once after the release tail.
/// </summary>
public sealed class AudioCaptureService : IAudioCaptureService
{
    internal const int OutputSampleRate = 16_000;
    // Preserve a quiet short word that starts just before the push-to-talk trigger. The extra
    // 120 ms costs only about 46 KiB even for 48 kHz stereo float capture and adds no
    // release-to-text latency because the WASAPI stream remains continuously warm.
    internal static readonly TimeSpan PreRollDuration = TimeSpan.FromMilliseconds(320);
    internal static readonly TimeSpan ReleaseTailDuration = TimeSpan.FromMilliseconds(350);

    private readonly object _sync = new();
    private readonly VoiceSpectrumAnalyzer _spectrum = new();
    private readonly bool _persistCompletedTake;
    private readonly IMicrophoneDeviceCatalog _deviceCatalog;
    private readonly bool _ownsDeviceCatalog;
    private WasapiCapture? _capture;
    private MMDevice? _captureDevice;
    private WaveFormat? _captureFormat;
    private CaptureSessionBuffer? _buffer;
    private string? _selectedDeviceId;
    private string? _activeDeviceId;
    private bool _paused;
    private bool _stopRequested;
    private bool _disposed;
    private Exception? _monitoringFailure;
    private float _smoothedLevel;
    private float _smoothedBass;
    private float _smoothedMid;
    private float _smoothedTreble;
    private long _feedbackSuppressedUntilTimestamp;

    public event EventHandler<float>? LevelChanged;
    public event EventHandler<VoiceTimbreLevel>? TimbreChanged;
    public event EventHandler<float[]>? SamplesAvailable;
    public event EventHandler<AudioCaptureStateChangedEventArgs>? StateChanged;

    public AudioCaptureService(
        bool persistCompletedTake = false,
        string? captureDeviceId = null,
        bool startPaused = false)
        : this(
            new MicrophoneDeviceCatalog(),
            ownsDeviceCatalog: true,
            persistCompletedTake,
            captureDeviceId,
            startPaused)
    {
    }

    internal AudioCaptureService(
        IMicrophoneDeviceCatalog deviceCatalog,
        bool ownsDeviceCatalog,
        bool persistCompletedTake,
        string? captureDeviceId,
        bool startPaused)
    {
        _deviceCatalog = deviceCatalog;
        _ownsDeviceCatalog = ownsDeviceCatalog;
        _persistCompletedTake = persistCompletedTake;
        _selectedDeviceId = MicrophoneSelectionPolicy.NormalizeDeviceId(captureDeviceId);
        _paused = startPaused;
        _deviceCatalog.DevicesChanged += OnDevicesChanged;
        if (_paused)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                StartMonitoringLocked();
            }
        }
        catch (Exception exception)
        {
            // App start remains recoverable when the default endpoint is temporarily unavailable.
            // Start() retries and turns the same concrete failure into the capsule state.
            _monitoringFailure = exception;
            AppLog.Write("WASAPI warm capture unavailable; will retry on trigger", exception);
        }
    }

    public AudioCaptureState GetState()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetStateLocked(GetDevicesSafe());
        }
    }

    public IReadOnlyList<MicrophoneDeviceInfo> GetCaptureDevices()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetDevicesSafe();
        }
    }

    public void SelectCaptureDevice(string? deviceId)
    {
        var normalized = MicrophoneSelectionPolicy.NormalizeDeviceId(deviceId);
        AudioCaptureStateChangedEventArgs? change;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var devices = GetDevicesSafe();
            if (!MicrophoneSelectionPolicy.IsAvailable(normalized, devices))
            {
                throw new MicrophoneUnavailableException(
                    normalized is null
                        ? "Системный микрофон сейчас недоступен."
                        : "Выбранный микрофон сейчас недоступен.");
            }

            var sameSelection = string.Equals(_selectedDeviceId, normalized, StringComparison.Ordinal);
            var expectedActiveId = ResolveActiveDeviceId(normalized, devices);
            if (sameSelection
                && (_paused || (_capture is not null
                    && string.Equals(_activeDeviceId, expectedActiveId, StringComparison.Ordinal))))
            {
                return;
            }

            var cancelled = _buffer?.IsSessionActive == true;
            DiscardSessionLocked(clearPreRoll: true);
            StopAndDisposeCaptureLocked();
            _selectedDeviceId = normalized;
            _monitoringFailure = null;
            if (!_paused)
            {
                StartMonitoringLocked();
            }
            change = new AudioCaptureStateChangedEventArgs(
                GetStateLocked(devices),
                AudioCaptureChangeKind.DeviceChanged,
                cancelled);
        }
        RaiseStateChanged(change);
    }

    public void PauseMonitoring()
    {
        AudioCaptureStateChangedEventArgs? change;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_paused)
            {
                return;
            }
            var cancelled = _buffer?.IsSessionActive == true;
            _paused = true;
            _monitoringFailure = null;
            DiscardSessionLocked(clearPreRoll: true);
            StopAndDisposeCaptureLocked();
            change = new AudioCaptureStateChangedEventArgs(
                GetStateLocked(GetDevicesSafe()),
                AudioCaptureChangeKind.Paused,
                cancelled);
        }
        RaiseStateChanged(change);
    }

    public void ResumeMonitoring()
    {
        AudioCaptureStateChangedEventArgs? change;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_paused && _capture is not null)
            {
                return;
            }
            var devices = GetDevicesSafe();
            if (!MicrophoneSelectionPolicy.IsAvailable(_selectedDeviceId, devices))
            {
                throw new MicrophoneUnavailableException(
                    _selectedDeviceId is null
                        ? "Системный микрофон сейчас недоступен."
                        : "Выбранный микрофон сейчас недоступен.");
            }

            _paused = false;
            _monitoringFailure = null;
            try
            {
                StartMonitoringLocked();
            }
            catch
            {
                _paused = true;
                StopAndDisposeCaptureLocked();
                throw;
            }
            change = new AudioCaptureStateChangedEventArgs(
                GetStateLocked(devices),
                AudioCaptureChangeKind.Resumed,
                ActiveTakeCancelled: false);
        }
        RaiseStateChanged(change);
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_buffer?.IsSessionActive == true)
            {
                throw new InvalidOperationException("Запись уже запущена.");
            }

            if (_paused)
            {
                throw new InvalidOperationException("Запись приостановлена.");
            }

            if (_capture is null)
            {
                StartMonitoringLocked();
            }

            if (_monitoringFailure is not null)
            {
                throw new InvalidOperationException("Микрофон недоступен.", _monitoringFailure);
            }

            var format = _captureFormat ?? throw new InvalidOperationException("Формат микрофона не определён.");
            _spectrum.Reset();
            (_buffer ?? throw new InvalidOperationException("Буфер микрофона не создан."))
                .Begin(Math.Max(format.AverageBytesPerSecond * 2, 4096));
            _stopRequested = false;
            _smoothedLevel = 0;
        }
    }

    /// <summary>
    /// Clears warm/session audio already exposed to a cue and drops callback buffers until its
    /// bounded acoustic tail has elapsed. Capture remains active, so UI and hotkey latency do not
    /// wait for speaker playback, but cue samples cannot enter the accepted ASR window.
    /// </summary>
    public void SuppressFeedbackAudio(TimeSpan duration)
    {
        lock (_sync)
        {
            if (_disposed || duration <= TimeSpan.Zero)
            {
                return;
            }

            // Bound feedback acoustic suppression to the actual sound tone duration (max 40 ms)
            // so human speech is never clipped or delayed.
            var effectiveMs = Math.Min(duration.TotalMilliseconds, 40d);
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var deadline = now + (long)Math.Ceiling(
                effectiveMs / 1000d * System.Diagnostics.Stopwatch.Frequency);
            _feedbackSuppressedUntilTimestamp = Math.Max(_feedbackSuppressedUntilTimestamp, deadline);
            _smoothedLevel = 0;
        }
    }

    public async Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_buffer?.IsSessionActive != true)
            {
                throw new InvalidOperationException("Запись не запущена.");
            }
            if (_stopRequested)
            {
                throw new InvalidOperationException("Остановка записи уже выполняется.");
            }
            _stopRequested = true;
        }

        try
        {
            // Preserve the release consonant/ending. WASAPI remains warm afterwards, so the next
            // trigger does not reopen the device or pay the first-buffer latency.
            await Task.Delay(ReleaseTailDuration, cancellationToken).ConfigureAwait(false);

            CapturedPcm completed;
            WaveFormat format;
            lock (_sync)
            {
                if (_monitoringFailure is not null)
                {
                    throw new InvalidOperationException("Микрофон отключился во время записи.", _monitoringFailure);
                }
                format = _captureFormat ?? throw new InvalidOperationException("Формат микрофона потерян.");
                completed = (_buffer ?? throw new OperationCanceledException(cancellationToken)).Complete();
                _stopRequested = false;
            }

            var preRollBytes = completed.PreRollBytes;
            var samples = await ConvertCompletedTakeAsync(completed, format, cancellationToken).ConfigureAwait(false);

            var preRollSamples = (int)Math.Min(
                samples.Length,
                Math.Round(preRollBytes / (double)Math.Max(1, format.AverageBytesPerSecond) * OutputSampleRate));
            var activity = Analyze(samples, preRollSamples);
            var path = _persistCompletedTake
                ? await PersistTakeAsync(samples, cancellationToken).ConfigureAwait(false)
                : null;

            var handler = SamplesAvailable;
            if (handler is not null && samples.Length > 0)
            {
                try
                {
                    handler(this, samples);
                }
                catch (Exception exception)
                {
                    AppLog.Write("Sample subscriber threw after capture", exception);
                }
            }

            return new AudioCaptureResult(
                path,
                samples,
                OutputSampleRate,
                activity.HasSpeech,
                activity.Duration,
                activity.DetectedSpeech,
                activity.PeakDecibels,
                DescribeRejection(activity.Rejection));
        }
        catch
        {
            lock (_sync)
            {
                DiscardSessionLocked();
            }
            throw;
        }
    }

    public Task<string?> CancelAsync()
    {
        lock (_sync)
        {
            DiscardSessionLocked();
        }
        return Task.FromResult<string?>(null);
    }

    private void StartMonitoringLocked()
    {
        if (_capture is not null)
        {
            return;
        }

        WasapiCapture? capture = null;
        MMDevice? device = null;
        try
        {
            device = _deviceCatalog.OpenCaptureDevice(_selectedDeviceId);
            capture = new WasapiCapture(device)
            {
                ShareMode = AudioClientShareMode.Shared
            };
            var format = capture.WaveFormat;
            var preRollBytes = AlignToBlock(
                (int)Math.Ceiling(format.AverageBytesPerSecond * PreRollDuration.TotalSeconds),
                format.BlockAlign);
            _captureFormat = format;
            _buffer = new CaptureSessionBuffer(preRollBytes, format.BlockAlign);
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            _capture = capture;
            _captureDevice = device;
            _activeDeviceId = device.ID;
            _monitoringFailure = null;
            capture.StartRecording();
            AppLog.Write(
                $"WASAPI microphone warm: rate={format.SampleRate}, bits={format.BitsPerSample}, channels={format.Channels}");
        }
        catch
        {
            if (capture is not null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                capture.Dispose();
            }
            device?.Dispose();
            _capture = null;
            _captureDevice = null;
            _activeDeviceId = null;
            _captureFormat = null;
            _buffer = null;
            throw;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        WaveFormat? format;
        bool measureSpectrum;
        lock (_sync)
        {
            // Unsubscribing cannot retract a callback that was already queued by the old WASAPI
            // endpoint. Identity is checked under the same lock as switch/clear so old samples
            // can neither enter the replacement buffer nor be measured with its format.
            if (!IsCurrentCaptureCallback(sender, _capture, _disposed, args.BytesRecorded))
            {
                return;
            }
            if (System.Diagnostics.Stopwatch.GetTimestamp() < _feedbackSuppressedUntilTimestamp)
            {
                return;
            }
            format = _captureFormat;
            measureSpectrum = _buffer?.IsSessionActive == true;
            _buffer?.Append(args.Buffer.AsSpan(0, args.BytesRecorded));
        }

        if (format is null || !PcmLevelMeter.TryMeasureTimbre(
            args.Buffer, args.BytesRecorded, format,
            out var rms, out var peak,
            out var rawBass, out var rawMid, out var rawTreble))
        {
            return;
        }

        var rmsLevel = DbToLevel(rms, -62, -14);
        var spectrum = measureSpectrum ? _spectrum.Measure(args.Buffer, args.BytesRecorded, format) : default;
        var peakLevel = DbToLevel(peak, -56, -7);
        var level = (float)Math.Clamp((rmsLevel * 0.76) + (peakLevel * 0.24), 0, 1);

        var bassLevel = (float)Math.Clamp(DbToLevel(rawBass, -60, -16), 0, 1);
        var midLevel = (float)Math.Clamp(DbToLevel(rawMid, -62, -18), 0, 1);
        var trebleLevel = (float)Math.Clamp(DbToLevel(rawTreble, -58, -12), 0, 1);

        float smoothedLevel;
        float smoothedBass;
        float smoothedMid;
        float smoothedTreble;
        lock (_sync)
        {
            // The endpoint may have changed while level calculation ran outside the lock.
            if (!IsCurrentCaptureCallback(sender, _capture, _disposed, args.BytesRecorded))
            {
                return;
            }
            var smoothing = level > _smoothedLevel ? 0.62f : 0.20f;
            _smoothedLevel += (level - _smoothedLevel) * smoothing;
            _smoothedBass += (bassLevel - _smoothedBass) * smoothing;
            _smoothedMid += (midLevel - _smoothedMid) * smoothing;
            _smoothedTreble += (trebleLevel - _smoothedTreble) * (level > _smoothedLevel ? 0.75f : 0.25f);

            smoothedLevel = _smoothedLevel;
            smoothedBass = _smoothedBass;
            smoothedMid = _smoothedMid;
            smoothedTreble = _smoothedTreble;
        }
        try
        {
            LevelChanged?.Invoke(this, smoothedLevel);
            TimbreChanged?.Invoke(this, new VoiceTimbreLevel(smoothedLevel, smoothedBass, smoothedMid, smoothedTreble)
                { Spectrum = spectrum });
        }
        catch (Exception exception)
        {
            // A UI observer must never terminate the WASAPI callback thread.
            AppLog.Write("Microphone level subscriber threw", exception);
        }
    }

    internal static bool IsCurrentCaptureCallback(
        object? sender,
        object? currentCapture,
        bool disposed,
        int bytesRecorded) =>
        !disposed
        && bytesRecorded > 0
        && currentCapture is not null
        && ReferenceEquals(sender, currentCapture);

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        AudioCaptureStateChangedEventArgs? change = null;
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(sender, _capture))
            {
                return;
            }
            var cancelled = _buffer?.IsSessionActive == true;
            _monitoringFailure = args.Exception ?? new InvalidOperationException("WASAPI capture stopped unexpectedly.");
            _paused = true;
            DiscardSessionLocked(clearPreRoll: true);
            DisposeCaptureLocked();
            change = new AudioCaptureStateChangedEventArgs(
                GetStateLocked(GetDevicesSafe()),
                AudioCaptureChangeKind.DeviceUnavailable,
                cancelled,
                "Микрофон отключён. Выберите доступное устройство и возобновите запись.");
        }
        RaiseStateChanged(change);
    }

    private void OnDevicesChanged(object? sender, EventArgs args)
    {
        AudioCaptureStateChangedEventArgs? change = null;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            var devices = GetDevicesSafe();
            var topologyAction = MicrophoneLifecyclePolicy.EvaluateTopologyChange(
                _selectedDeviceId,
                _paused,
                _activeDeviceId,
                devices);
            if (topologyAction == MicrophoneTopologyAction.PauseUnavailable)
            {
                var cancelled = _buffer?.IsSessionActive == true;
                _paused = true;
                _monitoringFailure = new MicrophoneUnavailableException(
                    _selectedDeviceId is null
                        ? "Системный микрофон сейчас недоступен."
                        : "Выбранный микрофон сейчас недоступен.");
                DiscardSessionLocked(clearPreRoll: true);
                StopAndDisposeCaptureLocked();
                change = new AudioCaptureStateChangedEventArgs(
                    GetStateLocked(devices),
                    AudioCaptureChangeKind.DeviceUnavailable,
                    cancelled,
                    "Микрофон отключён. Выберите доступное устройство и возобновите запись.");
            }
            else if (topologyAction == MicrophoneTopologyAction.RestartOnDefault)
            {
                var cancelled = _buffer?.IsSessionActive == true;
                DiscardSessionLocked(clearPreRoll: true);
                StopAndDisposeCaptureLocked();
                try
                {
                    StartMonitoringLocked();
                    change = new AudioCaptureStateChangedEventArgs(
                        GetStateLocked(devices),
                        AudioCaptureChangeKind.DefaultDeviceChanged,
                        cancelled);
                }
                catch (Exception exception)
                {
                    _paused = true;
                    _monitoringFailure = exception;
                    change = new AudioCaptureStateChangedEventArgs(
                        GetStateLocked(devices),
                        AudioCaptureChangeKind.DeviceUnavailable,
                        cancelled,
                        "Не удалось переключиться на системный микрофон. Выберите устройство вручную.");
                }
            }
            else
            {
                // Re-adding an explicitly selected endpoint never resumes capture silently.
                if (_paused && _monitoringFailure is MicrophoneUnavailableException)
                {
                    _monitoringFailure = null;
                }
                change = new AudioCaptureStateChangedEventArgs(
                    GetStateLocked(devices),
                    AudioCaptureChangeKind.InventoryChanged,
                    ActiveTakeCancelled: false);
            }
        }
        RaiseStateChanged(change);
    }

    private IReadOnlyList<MicrophoneDeviceInfo> GetDevicesSafe()
    {
        try
        {
            return _deviceCatalog.GetActiveDevices();
        }
        catch (Exception exception) when (exception is COMException or MicrophoneUnavailableException)
        {
            AppLog.Write("Could not enumerate capture endpoints", exception);
            return [];
        }
    }

    private AudioCaptureState GetStateLocked(IReadOnlyList<MicrophoneDeviceInfo> devices)
    {
        var available = MicrophoneSelectionPolicy.IsAvailable(_selectedDeviceId, devices);
        return new AudioCaptureState(
            _selectedDeviceId,
            MicrophoneSelectionPolicy.DisplayName(_selectedDeviceId, devices),
            _paused,
            _capture is not null && !_paused,
            available,
            !available || _monitoringFailure is not null ? "device-unavailable" : null);
    }

    private static string? ResolveActiveDeviceId(
        string? selectedDeviceId,
        IReadOnlyList<MicrophoneDeviceInfo> devices) =>
        selectedDeviceId ?? devices.FirstOrDefault(device => device.IsDefault)?.Id;

    private void RaiseStateChanged(AudioCaptureStateChangedEventArgs? change)
    {
        if (change is null)
        {
            return;
        }
        try
        {
            StateChanged?.Invoke(this, change);
        }
        catch (Exception exception)
        {
            AppLog.Write("Microphone state subscriber threw", exception);
        }
    }

    /// <summary>
    /// Uses the exact production speech gate for an already captured benchmark WAV. Exposed only
    /// inside the assembly so the offline harness can attribute a lost take without changing the
    /// interactive path or persisting another copy of the audio.
    /// </summary>
    internal static SpeechActivitySnapshot Analyze(float[] samples, int preRollSamples)
    {
        var detector = new SpeechActivityDetector();
        var noiseFloor = AudioSignalAnalyzer.EstimateNoiseFloorDb(samples, preRollSamples, OutputSampleRate);
        // Pre-roll can contain the very first word we intentionally preserved. Use the quieter
        // boundary as background evidence so that word cannot raise its own acceptance threshold.
        // This only calibrates session acceptance; every original sample still reaches ASR.
        var tailSamples = Math.Min(samples.Length, (int)(ReleaseTailDuration.TotalSeconds * OutputSampleRate));
        var tailFloor = AudioSignalAnalyzer.EstimateNoiseFloorDb(samples.AsSpan(samples.Length - tailSamples), OutputSampleRate);
        if (tailFloor is { } tail && (noiseFloor is null || tail < noiseFloor.Value))
        {
            noiseFloor = tail;
        }
        detector.Reset(noiseFloor);
        const int frameSamples = OutputSampleRate / 50; // 20 ms
        for (var offset = 0; offset < samples.Length; offset += frameSamples)
        {
            var count = Math.Min(frameSamples, samples.Length - offset);
            double sum = 0;
            double peak = 0;
            for (var index = 0; index < count; index++)
            {
                var sample = samples[offset + index];
                sum += sample * sample;
                peak = Math.Max(peak, Math.Abs(sample));
            }
            detector.Process(Math.Sqrt(sum / Math.Max(1, count)), peak, count * 1000d / OutputSampleRate);
        }
        return detector.Snapshot();
    }

    internal static async Task<float[]> ConvertCompletedTakeAsync(
        CapturedPcm completed, WaveFormat format, CancellationToken cancellationToken)
    {
        using (completed)
        {
            // Ownership covers cancellation before Task.Run starts as well as conversion failure.
            return await Task.Run(
                () => ConvertToMono16Khz(completed.Bytes, format), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static float[] ConvertToMono16Khz(ReadOnlyMemory<byte> raw, WaveFormat format)
    {
        if (raw.Length == 0)
        {
            return [];
        }

        var readableFormat = format.AsStandardWaveFormat();
        if (!MemoryMarshal.TryGetArray(raw, out var segment))
        {
            segment = new ArraySegment<byte>(raw.ToArray());
        }
        using var memory = new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false);
        using var source = new RawSourceWaveStream(memory, readableFormat);
        ISampleProvider provider = source.ToSampleProvider();
        if (provider.WaveFormat.Channels > 1)
        {
            provider = new DownmixToMonoSampleProvider(provider);
        }
        if (provider.WaveFormat.SampleRate != OutputSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, OutputSampleRate);
        }

        var expected = Math.Max(1024, (int)Math.Ceiling(
            raw.Length / (double)Math.Max(1, format.AverageBytesPerSecond) * OutputSampleRate) + 512);
        var output = new ArrayBufferWriter<float>(expected);
        var buffer = ArrayPool<float>.Shared.Rent(OutputSampleRate);
        try
        {
            int read;
            while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            {
                buffer.AsSpan(0, read).CopyTo(output.GetSpan(read));
                output.Advance(read);
            }
            return output.WrittenSpan.ToArray();
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static async Task<string> PersistTakeAsync(float[] samples, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(
            Egoist.Voice.Core.VoiceRuntimeProfile.DataRoot, "Temp");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"voice-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.wav");
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var writer = new WaveFileWriter(path, new WaveFormat(OutputSampleRate, 16, 1));
            var buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                var sampleOffset = 0;
                while (sampleOffset < samples.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = Math.Min(buffer.Length / 2, samples.Length - sampleOffset);
                    for (var index = 0; index < count; index++)
                    {
                        var pcm = (short)Math.Round(Math.Clamp(samples[sampleOffset + index], -1, 1) * short.MaxValue);
                        buffer[index * 2] = (byte)pcm;
                        buffer[(index * 2) + 1] = (byte)(pcm >> 8);
                    }
                    writer.Write(buffer, 0, count * 2);
                    sampleOffset += count;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }, cancellationToken).ConfigureAwait(false);
        return path;
    }

    internal static string? DescribeRejection(SpeechRejection rejection) => rejection switch
    {
        SpeechRejection.MicrophoneSilent => "Микрофон молчит",
        SpeechRejection.TooQuiet => "Слишком тихо",
        SpeechRejection.TooShort => "Слишком коротко",
        SpeechRejection.NoAudio => "Нет звука",
        _ => null
    };

    internal static float DbToLevel(double amplitude, double floorDb, double ceilingDb)
    {
        var decibels = 20 * Math.Log10(Math.Max(amplitude, 0.000001));
        return (float)Math.Clamp((decibels - floorDb) / (ceilingDb - floorDb), 0, 1);
    }

    private static int AlignToBlock(int bytes, int blockAlign) =>
        Math.Max(blockAlign, bytes - (bytes % Math.Max(1, blockAlign)));

    private void DiscardSessionLocked(bool clearPreRoll = false)
    {
        _buffer?.CancelSession();
        _spectrum.Reset();
        if (clearPreRoll)
        {
            _buffer?.Clear();
        }
        _stopRequested = false;
    }

    private void StopAndDisposeCaptureLocked()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try
            {
                _capture.StopRecording();
            }
            catch
            {
                // Dispose below is the final lifecycle boundary for this endpoint.
            }
        }
        DisposeCaptureLocked();
    }

    private void DisposeCaptureLocked()
    {
        if (_capture is null)
        {
            return;
        }
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
        _capture = null;
        _captureDevice?.Dispose();
        _captureDevice = null;
        _activeDeviceId = null;
        _captureFormat = null;
        _buffer?.Clear();
        _buffer = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _deviceCatalog.DevicesChanged -= OnDevicesChanged;
            DiscardSessionLocked(clearPreRoll: true);
            StopAndDisposeCaptureLocked();
            _buffer?.Clear();
            _buffer = null;
        }
        if (_ownsDeviceCatalog)
        {
            _deviceCatalog.Dispose();
        }
    }
}

internal sealed class CapturedPcm(byte[] buffer, int length, int preRollBytes) : IDisposable
{
    private byte[]? _buffer = buffer;

    internal ReadOnlyMemory<byte> Bytes =>
        (_buffer ?? throw new ObjectDisposedException(nameof(CapturedPcm))).AsMemory(0, length);
    internal int PreRollBytes { get; } = preRollBytes;

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref _buffer, null);
        if (owned is not null)
        {
            Array.Clear(owned, 0, length);
        }
    }
}

internal sealed class CaptureSessionBuffer
{
    private readonly PcmByteRingBuffer _preRoll;
    private MemoryStream? _session;
    private int _sessionPreRollBytes;

    internal CaptureSessionBuffer(int preRollCapacity, int blockAlign) =>
        _preRoll = new PcmByteRingBuffer(preRollCapacity, blockAlign);

    internal bool IsSessionActive => _session is not null;

    internal void Begin(int initialCapacity)
    {
        if (_session is not null)
        {
            throw new InvalidOperationException("Session already active.");
        }
        var prefix = _preRoll.Snapshot();
        _session = new MemoryStream(Math.Max(initialCapacity, prefix.Length + 4096));
        _session.Write(prefix);
        _sessionPreRollBytes = prefix.Length;
    }

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        _session?.Write(bytes);
        _preRoll.Write(bytes);
    }

    internal CapturedPcm Complete()
    {
        var session = _session ?? throw new InvalidOperationException("Session is not active.");
        var result = new CapturedPcm(session.GetBuffer(), checked((int)session.Length), _sessionPreRollBytes);
        // The completed take now owns this buffer; clearing it here would erase the ASR input.
        DisposeSession(clear: false);
        return result;
    }

    internal void CancelSession() => DisposeSession(clear: true);

    internal void Clear()
    {
        DisposeSession(clear: true);
        _preRoll.Clear();
    }

    internal void DiscardAudioPreservingSession()
    {
        var wasActive = _session is not null;
        DisposeSession(clear: true);
        _preRoll.Clear();
        if (wasActive)
        {
            _session = new MemoryStream(4096);
        }
    }

    private void DisposeSession(bool clear)
    {
        if (_session is not null)
        {
            if (clear && _session.TryGetBuffer(out var buffer))
            {
                buffer.AsSpan(0, (int)_session.Length).Clear();
            }
            _session.Dispose();
        }
        _session = null;
        _sessionPreRollBytes = 0;
    }
}

internal sealed class PcmByteRingBuffer
{
    private readonly byte[] _buffer;
    private readonly int _blockAlign;
    private int _writeOffset;
    private int _count;

    internal PcmByteRingBuffer(int capacity, int blockAlign)
    {
        _blockAlign = Math.Max(1, blockAlign);
        capacity -= capacity % _blockAlign;
        _buffer = new byte[Math.Max(_blockAlign, capacity)];
    }

    internal int Count => _count;

    internal void Write(ReadOnlySpan<byte> bytes)
    {
        var alignedLength = bytes.Length - (bytes.Length % _blockAlign);
        if (alignedLength <= 0)
        {
            return;
        }
        bytes = bytes[..alignedLength];
        if (bytes.Length >= _buffer.Length)
        {
            bytes[^_buffer.Length..].CopyTo(_buffer);
            _writeOffset = 0;
            _count = _buffer.Length;
            return;
        }

        var first = Math.Min(bytes.Length, _buffer.Length - _writeOffset);
        bytes[..first].CopyTo(_buffer.AsSpan(_writeOffset));
        bytes[first..].CopyTo(_buffer);
        _writeOffset = (_writeOffset + bytes.Length) % _buffer.Length;
        _count = Math.Min(_buffer.Length, _count + bytes.Length);
    }

    internal byte[] Snapshot()
    {
        var result = new byte[_count];
        if (_count == 0)
        {
            return result;
        }
        var start = (_writeOffset - _count + _buffer.Length) % _buffer.Length;
        var first = Math.Min(_count, _buffer.Length - start);
        _buffer.AsSpan(start, first).CopyTo(result);
        _buffer.AsSpan(0, _count - first).CopyTo(result.AsSpan(first));
        return result;
    }

    internal void Clear()
    {
        Array.Clear(_buffer);
        _writeOffset = 0;
        _count = 0;
    }
}

internal static class AudioSignalAnalyzer
{
    internal static double? EstimateNoiseFloorDb(float[] samples, int preRollSamples, int sampleRate)
    {
        preRollSamples = Math.Clamp(preRollSamples, 0, samples.Length);
        return EstimateNoiseFloorDb(samples.AsSpan(0, preRollSamples), sampleRate);
    }

    internal static double? EstimateNoiseFloorDb(ReadOnlySpan<float> samples, int sampleRate)
    {
        var frameSize = Math.Max(1, sampleRate / 100);
        if (samples.Length < frameSize * 4)
        {
            return null;
        }

        var levels = new List<double>(samples.Length / frameSize);
        for (var offset = 0; offset + frameSize <= samples.Length; offset += frameSize)
        {
            double sum = 0;
            for (var index = 0; index < frameSize; index++)
            {
                var sample = samples[offset + index];
                sum += sample * sample;
            }
            levels.Add(SpeechActivityDetector.AmplitudeToDecibels(Math.Sqrt(sum / frameSize)));
        }
        levels.Sort();
        return levels[Math.Min(levels.Count - 1, (int)Math.Floor(levels.Count * 0.25))];
    }
}

internal sealed class DownmixToMonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _sourceBuffer = [];

    internal DownmixToMonoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var required = checked(count * _channels);
        if (_sourceBuffer.Length < required)
        {
            _sourceBuffer = new float[required];
        }
        var read = _source.Read(_sourceBuffer, 0, required);
        var frames = read / _channels;
        for (var frame = 0; frame < frames; frame++)
        {
            double sum = 0;
            var sourceOffset = frame * _channels;
            for (var channel = 0; channel < _channels; channel++)
            {
                sum += _sourceBuffer[sourceOffset + channel];
            }
            buffer[offset + frame] = (float)(sum / _channels);
        }
        return frames;
    }
}

internal static class PcmLevelMeter
{
    internal static bool TryMeasure(byte[] buffer, int bytesRecorded, WaveFormat format, out double rms, out double peak) =>
        TryMeasureTimbre(buffer, bytesRecorded, format, out rms, out peak, out _, out _, out _);

    internal static bool TryMeasureTimbre(
        byte[] buffer,
        int bytesRecorded,
        WaveFormat format,
        out double rms,
        out double peak,
        out double bass,
        out double mid,
        out double treble)
    {
        rms = 0;
        peak = 0;
        bass = 0;
        mid = 0;
        treble = 0;
        var readable = format.AsStandardWaveFormat();
        var bytesPerSample = readable.BitsPerSample / 8;
        if (bytesPerSample <= 0 || bytesRecorded < bytesPerSample)
        {
            return false;
        }

        var sampleRate = readable.SampleRate > 0 ? readable.SampleRate : 16000;
        // 1-pole low-pass cutoff at ~300 Hz
        var alphaLow = Math.Clamp(2 * Math.PI * 300 / sampleRate, 0.01, 0.4);
        // 1-pole high-pass cutoff at ~2800 Hz
        var alphaHigh = Math.Clamp(2 * Math.PI * 2800 / sampleRate, 0.1, 0.85);

        double lowState = 0;
        double highState = 0;
        double sum = 0;
        double bassSum = 0;
        double midSum = 0;
        double trebleSum = 0;
        var count = 0;

        for (var offset = 0; offset + bytesPerSample <= bytesRecorded; offset += bytesPerSample)
        {
            double sample;
            if (readable.Encoding == WaveFormatEncoding.IeeeFloat && readable.BitsPerSample == 32)
            {
                sample = BitConverter.ToSingle(buffer, offset);
            }
            else if (readable.Encoding == WaveFormatEncoding.Pcm && readable.BitsPerSample == 16)
            {
                sample = BitConverter.ToInt16(buffer, offset) / 32768d;
            }
            else if (readable.Encoding == WaveFormatEncoding.Pcm && readable.BitsPerSample == 24)
            {
                var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                sample = value / 8388608d;
            }
            else if (readable.Encoding == WaveFormatEncoding.Pcm && readable.BitsPerSample == 32)
            {
                sample = BitConverter.ToInt32(buffer, offset) / 2147483648d;
            }
            else
            {
                return false;
            }

            if (!double.IsFinite(sample))
            {
                continue;
            }

            lowState += alphaLow * (sample - lowState);
            highState += alphaHigh * (sample - highState);
            var highSample = sample - highState;
            var midSample = highState - lowState;

            sum += sample * sample;
            bassSum += lowState * lowState;
            midSum += midSample * midSample;
            trebleSum += highSample * highSample;
            peak = Math.Max(peak, Math.Abs(sample));
            count++;
        }
        if (count == 0)
        {
            return false;
        }
        rms = Math.Sqrt(sum / count);
        bass = Math.Sqrt(bassSum / count);
        mid = Math.Sqrt(midSum / count);
        treble = Math.Sqrt(trebleSum / count);
        return true;
    }
}
