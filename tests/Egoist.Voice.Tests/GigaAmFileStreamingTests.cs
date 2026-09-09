using Egoist.Voice.Services;
using NAudio.Wave;

namespace Egoist.Voice.Tests;

public sealed class GigaAmFileStreamingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3500)]
    [InlineData(3501)]
    [InlineData(3700)]
    [InlineData(31 * 60 * 100)]
    public void Streaming_preserves_every_sample_and_final_tail_without_duration_limit(int count)
    {
        var provider = new GeneratedProvider(count);
        var offset = 0;
        var covered = 0;
        foreach (var chunk in AudioSampleReader.ReadChunks(provider))
        {
            Assert.InRange(chunk.Samples.Length, 1, 3500);
            Assert.False(chunk.ParagraphBreakBefore);
            for (var index = 0; index < chunk.Samples.Length; index++)
                Assert.Equal(GeneratedProvider.Sample(offset + index), chunk.Samples.Span[index]);
            covered = offset + chunk.Samples.Length;
            offset = covered - 24;
        }
        Assert.Equal(count, covered);
        Assert.Equal(count, provider.Position);
        Assert.InRange(provider.LargestRead, 1, 3501);
    }

    [Fact]
    public void Streaming_reads_only_one_window_before_first_chunk()
    {
        var provider = new GeneratedProvider(100 * 60 * 60);
        using var chunks = AudioSampleReader.ReadChunks(provider).GetEnumerator();
        Assert.True(chunks.MoveNext());
        Assert.Equal(3501, provider.Position);
        Assert.Equal(3500, chunks.Current.Samples.Length);
    }

    [Fact]
    public void Streaming_preserves_pause_boundaries_and_overlap()
    {
        var samples = Enumerable.Repeat(0.2f, 5000).ToArray();
        Array.Clear(samples, 3200, 150);
        var expected = GigaAmAudioChunker.Split(samples, 100);
        var actual = AudioSampleReader.ReadChunks(new ArrayProvider(samples)).ToArray();
        Assert.Equal(expected.Count, actual.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            Assert.Equal(expected[index].ParagraphBreakBefore, actual[index].ParagraphBreakBefore);
            Assert.Equal(expected[index].Samples.ToArray(), actual[index].Samples.ToArray());
        }
        Assert.True(actual[1].ParagraphBreakBefore);
    }

    [Fact]
    public void Cancellation_stops_before_reading_another_window()
    {
        var provider = new GeneratedProvider(10000);
        using var cancellation = new CancellationTokenSource();
        using var chunks = AudioSampleReader.ReadChunks(provider, cancellation.Token).GetEnumerator();
        Assert.True(chunks.MoveNext());
        var read = provider.Position;
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => chunks.MoveNext());
        Assert.Equal(read, provider.Position);
    }

    private sealed class GeneratedProvider(int count) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(100, 1);
        public int Position { get; private set; }
        public int LargestRead { get; private set; }
        public static float Sample(int index) => 0.2f + index % 97 * 0.0001f;
        public int Read(float[] buffer, int offset, int requested)
        {
            LargestRead = Math.Max(LargestRead, requested);
            var size = Math.Min(Math.Min(requested, 137), count - Position);
            for (var index = 0; index < size; index++) buffer[offset + index] = Sample(Position++);
            return size;
        }
    }

    private sealed class ArrayProvider(float[] samples) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(100, 1);
        private int _position;
        public int Read(float[] buffer, int offset, int count)
        {
            var size = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, size);
            _position += size;
            return size;
        }
    }
}
