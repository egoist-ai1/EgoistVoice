using NAudio.Dsp;
using NAudio.Wave;

namespace Egoist.Voice.Services;

/// <summary>Bounded display-only FFT. Never changes the samples passed to speech recognition.</summary>
internal sealed class VoiceSpectrumAnalyzer
{
    private const int Size = 1024;
    private readonly object _gate = new();
    private readonly float[] _samples = new float[Size];
    private readonly Complex[] _fft = new Complex[Size];
    private static readonly float[] Window = Enumerable.Range(0, Size)
        .Select(index => (float)FastFourierTransform.HannWindow(index, Size)).ToArray();
    private static readonly int[] Edges = [70, 180, 350, 650, 1100, 1800, 2800, 4200, 8000];
    private int _position;
    private int _sampleRate;

    internal VoiceSpectrum Measure(byte[] bytes, int byteCount, WaveFormat format)
    {
        var readable = format.AsStandardWaveFormat();
        var width = readable.BitsPerSample / 8;
        if (readable.SampleRate <= 0 || readable.Channels <= 0 || width is not (2 or 3 or 4) ||
            !(readable.Encoding == WaveFormatEncoding.Pcm ||
              readable.Encoding == WaveFormatEncoding.IeeeFloat && width == 4)) return default;
        lock (_gate)
        {
            if (_sampleRate != readable.SampleRate)
            {
                Array.Clear(_samples);
                _position = 0;
                _sampleRate = readable.SampleRate;
            }
            var stride = width * readable.Channels;
            var length = Math.Clamp(byteCount, 0, bytes.Length);
            // Only the latest transform window is needed, even for an unusually large callback.
            var first = Math.Max(0, length / stride - Size) * stride;
            for (var offset = first; offset + stride <= length; offset += stride)
            {
                double mono = 0;
                for (var channel = 0; channel < readable.Channels; channel++)
                {
                    var at = offset + channel * width;
                    var sample = ReadSample(bytes, at, width, readable.Encoding);
                    if (double.IsFinite(sample)) mono += Math.Clamp(sample, -1, 1);
                }
                _samples[_position] = (float)(mono / readable.Channels);
                _position = (_position + 1) % Size;
            }
            for (var index = 0; index < Size; index++)
                _fft[index] = new Complex { X = _samples[(_position + index) % Size] * Window[index] };
            FastFourierTransform.FFT(true, 10, _fft);
            Span<double> energy = stackalloc double[8];
            for (var bin = 1; bin < Size / 2; bin++)
            {
                var frequency = (double)bin * _sampleRate / Size;
                for (var band = 0; band < 8; band++)
                {
                    if (frequency < Edges[band] || frequency >= Edges[band + 1]) continue;
                    energy[band] += _fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y;
                    break;
                }
            }
            Span<float> levels = stackalloc float[8];
            for (var band = 0; band < 8; band++)
                levels[band] = AudioCaptureService.DbToLevel(Math.Sqrt(energy[band] * (2 / 0.375)), -66, -24);
            return new(levels[0], levels[1], levels[2], levels[3], levels[4], levels[5], levels[6], levels[7]);
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_samples);
            Array.Clear(_fft);
            _position = 0;
        }
    }

    private static double ReadSample(byte[] bytes, int offset, int width, WaveFormatEncoding encoding)
    {
        if (encoding == WaveFormatEncoding.IeeeFloat) return BitConverter.ToSingle(bytes, offset);
        if (width == 2) return BitConverter.ToInt16(bytes, offset) / 32768d;
        if (width == 4) return BitConverter.ToInt32(bytes, offset) / 2147483648d;
        var value = bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16;
        if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
        return value / 8388608d;
    }
}
