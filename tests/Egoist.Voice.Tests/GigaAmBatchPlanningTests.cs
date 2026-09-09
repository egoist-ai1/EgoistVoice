using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class GigaAmBatchPlanningTests
{
    [Fact]
    public void Short_final_tail_is_decoded_without_full_chunk_padding()
    {
        var chunks = Chunks(560_000, 160_000);
        Assert.Equal(new[] { 1, 1 }, BatchSizes(chunks));
    }

    [Fact]
    public void Comparable_chunks_remain_batched()
    {
        var chunks = Chunks(560_000, 500_000, 280_000);
        Assert.Equal(new[] { 3 }, BatchSizes(chunks));
    }

    [Fact]
    public void Long_recording_batches_remain_bounded_and_cover_every_chunk()
    {
        var chunks = Chunks(560_000, 560_000, 560_000, 560_000, 560_000,
            560_000, 560_000, 560_000, 160_000);
        var sizes = BatchSizes(chunks);
        Assert.Equal(new[] { 6, 2, 1 }, sizes);
        Assert.Equal(chunks.Count, sizes.Sum());
    }

    private static IReadOnlyList<GigaAmAudioChunk> Chunks(params int[] lengths) =>
        lengths.Select(length => new GigaAmAudioChunk(new float[length], false)).ToArray();

    private static int[] BatchSizes(IReadOnlyList<GigaAmAudioChunk> chunks)
    {
        var result = new List<int>();
        for (var offset = 0; offset < chunks.Count;)
        {
            var size = GigaAmTranscriptionService.GetBatchSize(chunks, offset);
            Assert.InRange(size, 1, 6);
            result.Add(size);
            offset += size;
        }
        return result.ToArray();
    }
}
