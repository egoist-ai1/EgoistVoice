using System.IO;
using System.Media;

namespace Egoist.Voice.Services;

public enum FeedbackSound
{
    RecordingStarted,
    RecordingStopped,
    TextInserted,
    Error
}

/// <summary>
/// Short synthesized cues for the four moments that matter.
/// </summary>
/// <remarks>
/// <para>
/// Wispr Flow's own documentation puts this first: "when you hear the ping <i>or</i> see the white
/// bars moving". Sound is named before the visual because it does not require looking at a corner
/// of the screen — the user can keep their eyes on what they are dictating into.
/// </para>
/// <para>
/// Tones are generated in memory rather than shipped as files: four WAVs would add nothing to the
/// installer but weight, and generating them keeps pitch and length adjustable in one place.
/// </para>
/// </remarks>
public sealed class FeedbackSoundService : IDisposable
{
    private const int SampleRate = 44_100;
    private const int FadeSamples = 220;

    private readonly Dictionary<FeedbackSound, (byte[] Payload, MemoryStream Stream, SoundPlayer Player)> _cues = new();
    private readonly object _sync = new();
    private readonly Action<TimeSpan>? _captureIsolation;
    private SoundPlayer? _player;
    private bool _disposed;

    public FeedbackSoundService(Action<TimeSpan>? captureIsolation = null)
    {
        _captureIsolation = captureIsolation;
        PreloadAll();
    }

    public bool Enabled { get; set; } = false;

    /// <summary>0 is silent, 1 is full scale. Default 0.32: a cue, not an alert.</summary>
    public double Volume { get; set; } = 0.32;

    public void Play(FeedbackSound sound)
    {
        Queue(sound, requireEnabled: true);
    }

    public void Preview(FeedbackSound sound)
    {
        Queue(sound, requireEnabled: false);
    }

    private void Queue(FeedbackSound sound, bool requireEnabled)
    {
        if (_disposed || (requireEnabled && !Enabled) || Volume <= 0)
        {
            return;
        }

        try
        {
            PlayCore(sound);
        }
        catch (Exception exception)
        {
            AppLog.Write("Feedback sound failed", exception);
        }
    }

    internal static TimeSpan CaptureExclusionWindow(FeedbackSound sound) =>
        CueDuration(sound) + TimeSpan.FromMilliseconds(120);

    private void PlayCore(FeedbackSound sound)
    {
        SoundPlayer player;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (!_cues.TryGetValue(sound, out var entry))
            {
                var payload = Synthesize(sound, Volume);
                var stream = new MemoryStream(payload, writable: false);
                var p = new SoundPlayer(stream);
                p.LoadAsync();
                entry = (payload, stream, p);
                _cues[sound] = entry;
            }

            player = entry.Player;
            _player = player;
        }

        if (sound == FeedbackSound.RecordingStarted)
        {
            _captureIsolation?.Invoke(CaptureExclusionWindow(sound));
        }

        _player.Play();
    }

    private void PreloadAll()
    {
        lock (_sync)
        {
            if (_disposed || Volume <= 0) return;
            foreach (FeedbackSound sound in Enum.GetValues<FeedbackSound>())
            {
                try
                {
                    var payload = Synthesize(sound, Volume);
                    var stream = new MemoryStream(payload, writable: false);
                    var player = new SoundPlayer(stream);
                    player.Load();
                    _cues[sound] = (payload, stream, player);
                }
                catch { }
            }
        }
    }

    /// <summary>Invalidates the cache after a volume change, so the next cue is regenerated.</summary>
    public void Invalidate()
    {
        lock (_sync)
        {
            ClearCuesLocked();
            PreloadAll();
        }
    }

    private void ClearCuesLocked()
    {
        foreach (var entry in _cues.Values)
        {
            try
            {
                entry.Player.Dispose();
                entry.Stream.Dispose();
            }
            catch { }
        }
        _cues.Clear();
    }

    internal static byte[] Synthesize(FeedbackSound sound, double volume)
    {
        var (startHz, endHz, milliseconds) = SoundShape(sound);

        var sampleCount = SampleRate * milliseconds / 1000;
        var samples = new short[sampleCount];
        var amplitude = Math.Clamp(volume, 0, 1) * short.MaxValue * 0.6;
        var phase = 0d;

        for (var index = 0; index < sampleCount; index++)
        {
            var progress = index / (double)sampleCount;
            var frequency = startHz + ((endHz - startHz) * progress);
            phase += 2 * Math.PI * frequency / SampleRate;

            // Cosine fade at both ends: smooth, elegant, zero clicks
            var envelope = Envelope(index, sampleCount);
            // Subtle 2nd harmonic (88% fundamental + 12% 2nd harmonic) produces a clean, modern acoustic tone
            var signal = (Math.Sin(phase) * 0.88) + (Math.Sin(2 * phase) * 0.12);
            samples[index] = (short)(signal * amplitude * envelope);
        }

        return BuildWave(samples);
    }

    private static (double StartHz, double EndHz, int Milliseconds) SoundShape(FeedbackSound sound) =>
        sound switch
        {
            FeedbackSound.RecordingStarted => (520d, 720d, 36),
            FeedbackSound.RecordingStopped => (700d, 500d, 32),
            FeedbackSound.TextInserted => (840d, 840d, 30),
            _ => (280d, 220d, 50)
        };

    private static TimeSpan CueDuration(FeedbackSound sound) =>
        TimeSpan.FromMilliseconds(SoundShape(sound).Milliseconds);

    private static double Envelope(int index, int count)
    {
        var fade = Math.Min(FadeSamples, count / 2);
        if (fade <= 0)
        {
            return 1;
        }

        if (index < fade)
        {
            return 0.5 * (1 - Math.Cos(Math.PI * index / fade));
        }

        var fromEnd = count - 1 - index;
        return fromEnd < fade ? 0.5 * (1 - Math.Cos(Math.PI * fromEnd / fade)) : 1;
    }

    private static byte[] BuildWave(short[] samples)
    {
        var dataBytes = samples.Length * sizeof(short);
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
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
            _player?.Dispose();
            _player = null;
            ClearCuesLocked();
        }
    }
}
