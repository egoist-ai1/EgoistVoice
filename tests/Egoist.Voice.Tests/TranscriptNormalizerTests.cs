using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class TranscriptNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData(" привет   мир ", "Привет мир")]
    [InlineData("привет , мир !", "Привет, мир!")]
    [InlineData("текст  ( внутри ) ", "Текст (внутри)")]
    [InlineData("у лукоморья дуб зелёный.", "У лукоморья дуб зелёный.")]
    public void Normalize_returns_clean_russian_text(string? input, string expected)
    {
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_preserves_paragraph_breaks()
    {
        var input = "первый абзац.\r\n\r\n   второй   абзац.";
        var expected = $"Первый абзац.{Environment.NewLine}{Environment.NewLine}Второй абзац.";

        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }
}
