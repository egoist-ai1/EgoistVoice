using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class TranscriptFormatterTests
{
    [Fact]
    public void Format_skips_empty_segments_and_joins_normal_speech()
    {
        TranscriptSegment[] segments =
        [
            new(" Привет,", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new(" ", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.1)),
            new("мой друг.", TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(2))
        ];

        Assert.Equal("Привет, мой друг.", TranscriptFormatter.Format(segments));
    }

    [Fact]
    public void Format_adds_paragraph_after_long_spoken_pause()
    {
        var first = new string('А', 84) + ".";
        TranscriptSegment[] segments =
        [
            new(first, TimeSpan.Zero, TimeSpan.FromSeconds(4)),
            new("Новая мысль.", TimeSpan.FromSeconds(5.2), TimeSpan.FromSeconds(6))
        ];

        Assert.Equal(
            $"{first}{Environment.NewLine}{Environment.NewLine}Новая мысль.",
            TranscriptFormatter.Format(segments));
    }

    [Fact]
    public void Format_does_not_split_short_phrase_on_pause()
    {
        TranscriptSegment[] segments =
        [
            new("Короткая фраза.", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new("Продолжение.", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4))
        ];

        Assert.Equal("Короткая фраза. Продолжение.", TranscriptFormatter.Format(segments));
    }

    [Fact]
    public void Format_does_not_add_space_before_punctuation_segment()
    {
        TranscriptSegment[] segments =
        [
            new("Привет", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new(",", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.1)),
            new("мир!", TimeSpan.FromSeconds(1.1), TimeSpan.FromSeconds(2))
        ];

        Assert.Equal("Привет, мир!", TranscriptFormatter.Format(segments));
    }
}
