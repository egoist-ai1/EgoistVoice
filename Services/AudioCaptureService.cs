using System.IO;
using NAudio.Wave;

namespace Egoist.Voice.Services;

public sealed class AudioCaptureService : IAudioCaptureService
{
    private readonly object _sync = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private TaskCompletionSource? _stopped;
    private string? _path;
    private float _smoothedLevel;
    private readonly SpeechActivityDetector _speechActivity = new();

    public event EventHandler<float>? LevelChanged;
    public event EventHandler<float[]>? SamplesAvailable;

    public AudioCaptureService()
    {
        // The stale-file sweep used to run inside Start(), which is reached synchronously from
        // the mouse hook callback. Directory enumeration and deletes there cost the whole system
        // its mouse responsiveness and count against LowLevelHooksTimeout.
        var directory = TemporaryDirectory;
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                DeleteStaleRecordings(directory);
            }
            catch (Exception exception)
            {
                AppLog.Write("Stale recording sweep failed", exception);
            }
        });
    }

    private static string TemporaryDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EgoistVoice",
        "Temp");

    public void Start()
    {
        lock (_sync)
        {
            if (_waveIn is not null)
            {
                throw new InvalidOperationException("Запись уже запущена.");
            }

            var directory = TemporaryDirectory;
            Directory.CreateDirectory(directory);

            _path = Path.Combine(directory, $"voice-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.wav");
            _smoothedLevel = 0;
            _speechActivity.Reset();
            try
            {
                _waveIn = new WaveInEvent
                {
                    // WAVE_MAPPER follows the current Windows default input device.
                    DeviceNumber = -1,
                    WaveFormat = new WaveFormat(16_000, 16, 1),
                    BufferMilliseconds = 32,
                    NumberOfBuffers = 3
                };
                _writer = new WaveFileWriter(_path, _waveIn.WaveFormat);
                _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                AppLog.Write($"Opening default microphone: devices={WaveIn.DeviceCount}, format=16000Hz/16bit/mono");
                _waveIn.StartRecording();
            }
            catch
            {
                var failedPath = _path;
                CleanupCaptureLocked();
                _stopped?.TrySetCanceled();
                _stopped = null;
                _path = null;
                TryDeleteFile(failedPath);
                throw;
            }
        }
    }

    public async Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken)
    {
        WaveInEvent waveIn;
        Task stoppedTask;
        string path;

        lock (_sync)
        {
            waveIn = _waveIn ?? throw new InvalidOperationException("Запись не запущена.");
            stoppedTask = _stopped?.Task ?? Task.CompletedTask;
            path = _path ?? throw new InvalidOperationException("Файл записи не создан.");
            waveIn.StopRecording();
        }

        await stoppedTask.WaitAsync(cancellationToken);
        var activity = _speechActivity.Snapshot();
        return new AudioCaptureResult(
            path,
            activity.HasSpeech,
            activity.Duration,
            activity.DetectedSpeech,
            activity.PeakDecibels,
            DescribeRejection(activity.Rejection));
    }

    /// <summary>
    /// A discarded session used to disappear without a word. These messages are short on purpose —
    /// they go into the capsule, which has room for two or three words.
    /// </summary>
    internal static string? DescribeRejection(SpeechRejection rejection) => rejection switch
    {
        SpeechRejection.MicrophoneSilent => "Микрофон молчит",
        SpeechRejection.TooQuiet => "Слишком тихо",
        SpeechRejection.TooShort => "Слишком коротко",
        SpeechRejection.NoAudio => "Нет звука",
        _ => null
    };

    public async Task<string?> CancelAsync()
    {
        WaveInEvent? waveIn;
        Task stoppedTask;
        string? path;
        lock (_sync)
        {
            waveIn = _waveIn;
            stoppedTask = _stopped?.Task ?? Task.CompletedTask;
            path = _path;
            waveIn?.StopRecording();
        }

        try
        {
            // Bounded: RecordingStopped is raised by the driver, and a device unplugged mid-session
            // may never raise it. An unbounded wait here parks the caller's finally block forever.
            await stoppedTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            AppLog.Write("Audio device did not report a clean stop; discarding recording anyway");
        }

        return path;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        lock (_sync)
        {
            _writer?.Write(args.Buffer, 0, args.BytesRecorded);
        }

        PublishSamples(args);

        double sum = 0;
        double peak = 0;
        var sampleCount = args.BytesRecorded / 2;
        for (var index = 0; index < args.BytesRecorded; index += 2)
        {
            var sample = BitConverter.ToInt16(args.Buffer, index) / 32768f;
            sum += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }
        var rms = sampleCount == 0 ? 0 : Math.Sqrt(sum / sampleCount);
        var durationMilliseconds = sampleCount * 1000d / 16_000d;
        _speechActivity.Process(rms, peak, durationMilliseconds);
        var rmsLevel = DbToLevel(rms, -58, -14);
        var peakLevel = DbToLevel(peak, -52, -7);
        var level = (float)Math.Clamp((rmsLevel * 0.78) + (peakLevel * 0.22), 0, 1);
        var smoothing = level > _smoothedLevel ? 0.62f : 0.20f;
        _smoothedLevel += (level - _smoothedLevel) * smoothing;
        LevelChanged?.Invoke(this, _smoothedLevel);
    }

    /// <summary>
    /// Hands the raw PCM to whoever is listening, converted to float once here rather than by every
    /// subscriber. Only raised when someone subscribed: the conversion and the copy are pure waste
    /// when live transcription is off, which is the default.
    /// </summary>
    private void PublishSamples(WaveInEventArgs args)
    {
        var handler = SamplesAvailable;
        if (handler is null || args.BytesRecorded < 2)
        {
            return;
        }

        var count = args.BytesRecorded / 2;
        var samples = new float[count];
        for (var index = 0; index < count; index++)
        {
            samples[index] = BitConverter.ToInt16(args.Buffer, index * 2) / 32768f;
        }

        try
        {
            handler(this, samples);
        }
        catch (Exception exception)
        {
            // A failing subscriber must never take the capture thread down with it: losing live
            // transcription is recoverable, losing the recording is not.
            AppLog.Write("Sample subscriber threw; live transcription may be degraded", exception);
        }
    }

    internal static float DbToLevel(double amplitude, double floorDb, double ceilingDb)
    {
        var decibels = 20 * Math.Log10(Math.Max(amplitude, 0.000001));
        return (float)Math.Clamp((decibels - floorDb) / (ceilingDb - floorDb), 0, 1);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        string? failedPath = null;
        lock (_sync)
        {
            CleanupCaptureLocked();
            if (args.Exception is null)
            {
                _stopped?.TrySetResult();
            }
            else
            {
                failedPath = _path;
                _stopped?.TrySetException(args.Exception);
            }
        }

        TryDeleteFile(failedPath);
    }

    private void CleanupCaptureLocked()
    {
        _writer?.Dispose();
        _writer = null;
        if (_waveIn is null)
        {
            return;
        }

        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.RecordingStopped -= OnRecordingStopped;
        _waveIn.Dispose();
        _waveIn = null;
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // The next recording startup also clears stale temporary files.
        }
    }

    private static void DeleteStaleRecordings(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "voice-*.wav"))
        {
            try
            {
                if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // A locked file belongs to another running operation.
            }
        }
    }

    public void Dispose()
    {
        string? path;
        lock (_sync)
        {
            path = _path;
            if (_waveIn is not null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                try
                {
                    _waveIn.StopRecording();
                }
                catch
                {
                    // Cleanup below is sufficient during application shutdown.
                }
            }
            CleanupCaptureLocked();
            _stopped?.TrySetCanceled();
            _stopped = null;
            _path = null;
        }

        TryDeleteFile(path);
    }
}
