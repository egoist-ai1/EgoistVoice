using System.Buffers;
using System.IO;
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
    internal static readonly TimeSpan PreRollDuration = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan ReleaseTailDuration = TimeSpan.FromMilliseconds(350);

    private readonly object _sync = new();
    private readonly bool _persistCompletedTake;
    private WasapiCapture? _capture;
    private WaveFormat? _captureFormat;
    private CaptureSessionBuffer? _buffer;
    private bool _stopRequested;
    private bool _disposed;
    private Exception? _monitoringFailure;
    private float _smoothedLevel;

    public event EventHandler<float>? LevelChanged;
    public event EventHandler<float[]>? SamplesAvailable;

    public AudioCaptureService(bool persistCompletedTake = false)
    {
        _persistCompletedTake = persistCompletedTake;
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

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_buffer?.IsSessionActive == true)
            {
                throw new InvalidOperationException("Запись уже запущена.");
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
            (_buffer ?? throw new InvalidOperationException("Буфер микрофона не создан."))
                .Begin(Math.Max(format.AverageBytesPerSecond * 2, 4096));
            _stopRequested = false;
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

            byte[] raw;
            int preRollBytes;
            WaveFormat format;
            lock (_sync)
            {
                if (_monitoringFailure is not null)
                {
                    throw new InvalidOperationException("Микрофон отключился во время записи.", _monitoringFailure);
                }
                var completed = (_buffer ?? throw new OperationCanceledException(cancellationToken)).Complete();
                raw = completed.Bytes;
                preRollBytes = completed.PreRollBytes;
                format = _captureFormat ?? throw new InvalidOperationException("Формат микрофона потерян.");
                _stopRequested = false;
            }

            var samples = await Task.Run(
                () => ConvertToMono16Khz(raw, format), cancellationToken).ConfigureAwait(false);
            Array.Clear(raw);

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
        try
        {
            capture = new WasapiCapture
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
            _capture = null;
            _captureFormat = null;
            _buffer = null;
            throw;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        WaveFormat? format;
        lock (_sync)
        {
            if (_disposed || args.BytesRecorded <= 0)
            {
                return;
            }
            format = _captureFormat;
            _buffer?.Append(args.Buffer.AsSpan(0, args.BytesRecorded));
        }

        if (format is null || !PcmLevelMeter.TryMeasure(args.Buffer, args.BytesRecorded, format, out var rms, out var peak))
        {
            return;
        }

        var rmsLevel = DbToLevel(rms, -62, -14);
        var peakLevel = DbToLevel(peak, -56, -7);
        var level = (float)Math.Clamp((rmsLevel * 0.76) + (peakLevel * 0.24), 0, 1);
        var smoothing = level > _smoothedLevel ? 0.62f : 0.20f;
        _smoothedLevel += (level - _smoothedLevel) * smoothing;
        LevelChanged?.Invoke(this, _smoothedLevel);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _monitoringFailure = args.Exception ?? new InvalidOperationException("WASAPI capture stopped unexpectedly.");
            DisposeCaptureLocked();
        }
    }

    private static SpeechActivitySnapshot Analyze(float[] samples, int preRollSamples)
    {
        var detector = new SpeechActivityDetector();
        var noiseFloor = AudioSignalAnalyzer.EstimateNoiseFloorDb(samples, preRollSamples, OutputSampleRate);
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

    internal static float[] ConvertToMono16Khz(byte[] raw, WaveFormat format)
    {
        if (raw.Length == 0)
        {
            return [];
        }

        var readableFormat = format.AsStandardWaveFormat();
        using var memory = new MemoryStream(raw, writable: false);
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
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EgoistVoice", "Temp");
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

    private void DiscardSessionLocked()
    {
        _buffer?.CancelSession();
        _stopRequested = false;
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
            DiscardSessionLocked();
            if (_capture is not null)
            {
                try
                {
                    _capture.StopRecording();
                }
                catch
                {
                    // Dispose below is the final shutdown boundary.
                }
            }
            DisposeCaptureLocked();
            _buffer?.Clear();
            _buffer = null;
        }
    }
}

internal sealed record CapturedPcm(byte[] Bytes, int PreRollBytes);

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
        var result = new CapturedPcm(session.ToArray(), _sessionPreRollBytes);
        DisposeSession(clear: true);
        return result;
    }

    internal void CancelSession() => DisposeSession(clear: true);

    internal void Clear()
    {
        DisposeSession(clear: true);
        _preRoll.Clear();
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
        var frameSize = Math.Max(1, sampleRate / 100); // 10 ms gives enough observations in 200 ms.
        if (preRollSamples < frameSize * 4)
        {
            return null;
        }

        var levels = new List<double>(preRollSamples / frameSize);
        for (var offset = 0; offset + frameSize <= preRollSamples; offset += frameSize)
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
    internal static bool TryMeasure(byte[] buffer, int bytesRecorded, WaveFormat format, out double rms, out double peak)
    {
        rms = 0;
        peak = 0;
        var readable = format.AsStandardWaveFormat();
        var bytesPerSample = readable.BitsPerSample / 8;
        if (bytesPerSample <= 0 || bytesRecorded < bytesPerSample)
        {
            return false;
        }

        double sum = 0;
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
            sum += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
            count++;
        }
        if (count == 0)
        {
            return false;
        }
        rms = Math.Sqrt(sum / count);
        return true;
    }
}
