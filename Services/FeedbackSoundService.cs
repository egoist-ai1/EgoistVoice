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

    private readonly Dictionary<FeedbackSound, byte[]> _cues = new();
    private readonly object _sync = new();
    private SoundPlayer? _player;
    private bool _disposed;

    public bool Enabled { get; set; } = true;

    /// <summary>0 is silent, 1 is full scale. Default 0.4: a cue, not an alert.</summary>
    public double Volume { get; set; } = 0.4;

    public void Play(FeedbackSound sound)
    {
        if (_disposed || !Enabled || Volume <= 0)
        {
            return;
        }

        // Fire and forget on the thread pool. A cue that delays the capsule by even a few
        // milliseconds defeats its own purpose.
        _ = Task.Run(() =>
        {
            try
            {
                PlayCore(sound);
            }
            catch (Exception exception)
            {
                AppLog.Write("Feedback sound failed", exception);
            }
        });
    }

    private void PlayCore(FeedbackSound sound)
    {
        byte[] payload;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (!_cues.TryGetValue(sound, out payload!))
            {
                payload = Synthesize(sound, Volume);
                _cues[sound] = payload;
            }

            _player ??= new SoundPlayer();
        }

        using var stream = new MemoryStream(payload, writable: false);
        lock (_sync)
        {
            if (_disposed || _player is null)
            {
                return;
            }
            _player.Stream = stream;
            _player.Play();
        }
    }

    /// <summary>Invalidates the cache after a volume change, so the next cue is regenerated.</summary>
    public void Invalidate()
    {
        lock (_sync)
        {
            _cues.Clear();
        }
    }

    internal static byte[] Synthesize(FeedbackSound sound, double volume)
    {
        var (startHz, endHz, milliseconds) = sound switch
        {
            FeedbackSound.RecordingStarted => (660d, 990d, 60),
            FeedbackSound.RecordingStopped => (880d, 620d, 60),
            FeedbackSound.TextInserted => (1_180d, 1_180d, 30),
            _ => (320d, 240d, 120)
        };

        var sampleCount = SampleRate * milliseconds / 1000;
        var samples = new short[sampleCount];
        var amplitude = Math.Clamp(volume, 0, 1) * short.MaxValue * 0.6;
        var phase = 0d;

        for (var index = 0; index < sampleCount; index++)
        {
            var progress = index / (double)sampleCount;
            var frequency = startHz + ((endHz - startHz) * progress);
            phase += 2 * Math.PI * frequency / SampleRate;

            // Cosine fade at both ends. A raw start or stop produces an audible click, which is
            // exactly the kind of cheapness this is meant to avoid.
            var envelope = Envelope(index, sampleCount);
            samples[index] = (short)(Math.Sin(phase) * amplitude * envelope);
        }

        return BuildWave(samples);
    }

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
            _cues.Clear();
        }
    }
}
