using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class TrustedSpellingCorrectionTests
{
    private static readonly TranscriptPostProcessor LiteralProcessor = new(
        UserDictionary.BuiltIn,
        new PostProcessingOptions(
            ApplyDictionary: true,
            ApplyVoiceCommands: true,
            ApplyNumberNormalization: true,
            PreserveSpokenWords: true));

    [Fact]
    public void Literal_mode_repairs_only_confirmed_spelling_errors_in_reported_transcript()
    {
        const string input = "Hello my friend, how you I from Rostov On Don? Привет из России. Репозиторй, Discord Conor of Kings, Egast Games One, Egaist Shild, Egast Voice.";
        const string expected = "Hello my friend, how you I from Rostov On Don? Привет из России. Репозиторий, Discord Conor of Kings, Egoist Games One, Egoist Shield, Egoist Voice.";

        Assert.Equal(expected, LiteralProcessor.Process(input));
    }

    [Theory]
    [InlineData("репозиторй", "репозиторий")]
    [InlineData("Репозиторй", "Репозиторий")]
    [InlineData("РЕПОЗИТОРЙ", "РЕПОЗИТОРИЙ")]
    public void Repository_spelling_correction_preserves_letter_case(string input, string expected) =>
        Assert.Equal(expected, LiteralProcessor.Process(input));

    [Theory]
    [InlineData("Egaist Games One", "Egoist Games One")]
    [InlineData("Egast Shield", "Egoist Shield")]
    [InlineData("Egoist Shild", "Egoist Shield")]
    [InlineData("Egaist Voice", "Egoist Voice")]
    public void Confirmed_product_prefix_and_tail_variants_are_canonicalized(
        string input,
        string expected) =>
        Assert.Equal(expected, LiteralProcessor.Process(input));

    [Theory]
    [InlineData("зе хлебонмай")]
    [InlineData("эгоист и эгоизм")]
    [InlineData("Conor of Kings")]
    [InlineData("Egast, Egaist и Shild")]
    [InlineData("Egast Games Two")]
    [InlineData("Egaist Voices")]
    [InlineData("C:\\Egast Games One\\readme.txt")]
    public void Literal_mode_keeps_novel_words_ordinary_words_and_untrusted_fragments(string input) =>
        Assert.Equal(input, LiteralProcessor.Process(input));

    [Fact]
    public void Correct_product_names_and_repository_spelling_remain_unchanged()
    {
        const string input = "Egoist Games One, Egoist Shield, Egoist Voice, репозиторий.";

        Assert.Equal(input, LiteralProcessor.Process(input));
    }
}
