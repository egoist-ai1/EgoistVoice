using System.Text.Json;
using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class LiteralDictationTests
{
    [Theory]
    [InlineData("кулибатуту")]
    [InlineData("зе хлеб он май")]
    [InlineData("двадцать пять новая строка точка")]
    [InlineData("переведи на английский питон и гитхаб")]
    [InlineData("пумпуруру бимбарара")]
    public void Literal_mode_preserves_the_decoder_text(string text)
    {
        var processor = new TranscriptPostProcessor(UserDictionary.BuiltIn,
            new PostProcessingOptions(true, true, true, PreserveSpokenWords: true));
        Assert.Equal(text, processor.Process("  " + text + "  "));
    }

    [Fact]
    public void Existing_settings_without_mode_use_literal_processing()
    {
        var settings = JsonSerializer.Deserialize<DictationSettings>(
            """{"formatWithQwen":true,"startLocalQwen":true,"applyDictionary":true}""")!;
        Assert.True(settings.PreserveSpokenWords);
        Assert.True(settings.ToPostProcessingOptions().PreserveSpokenWords);
    }

    [Fact]
    public void Explicit_dictionary_mode_remains_available()
    {
        var settings = DictationSettings.Default with { PreserveSpokenWords = false };
        var processor = new TranscriptPostProcessor(UserDictionary.BuiltIn, settings.ToPostProcessingOptions());
        Assert.Contains("GitHub", processor.Process("гитхаб"));
    }
}
