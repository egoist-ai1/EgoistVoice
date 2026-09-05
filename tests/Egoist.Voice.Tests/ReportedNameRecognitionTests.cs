using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class ReportedNameRecognitionTests
{
    private static readonly TranscriptPostProcessor Processor = new(
        UserDictionary.BuiltIn, new PostProcessingOptions(ApplyNumberNormalization: true));

    [Fact]
    public void Reported_mixed_speech_repairs_names_without_rewriting_correct_English()
    {
        const string input = "Город Rostofundone; AgistGames; PassFuizile2; I'm from Russia; GitHub; Discord; ChatGPT.";
        const string expected = "Город Ростов-на-Дону; Egoist Games; Path of Exile 2; I'm from Russia; GitHub; Discord; ChatGPT.";

        Assert.Equal(expected, Processor.Process(input));
        Assert.Equal(expected, Processor.Process(expected));
    }

    [Theory]
    [InlineData("Ростов на Дону", "Ростов-на-Дону")]
    [InlineData("эгоист геймс", "Egoist Games")]
    [InlineData("Agist Games", "Egoist Games")]
    [InlineData("EgoistGames", "Egoist Games")]
    [InlineData("Passive Exile 2", "Path of Exile 2")]
    [InlineData("пас оф экзайл два", "Path of Exile 2")]
    [InlineData("Path of Exile two", "Path of Exile 2")]
    public void Confirmed_complete_name_variants_are_canonicalized(string input, string expected) =>
        Assert.Equal(expected, UserDictionary.BuiltIn.Apply(input));

    [Theory]
    [InlineData("Файл C:\\games\\AgistGames\\save.dat не меняй.")]
    [InlineData("Открой https://AgistGames.example/PassFuizile2.")]
    [InlineData("Файл PassFuizile2.json сохранён.")]
    [InlineData("Поле AgistGames_backup не переименовывай.")]
    [InlineData("Почта AgistGames@example.com указана верно.")]
    [InlineData("AgistGamesBot и MyPassFuizile2Mod остаются идентификаторами.")]
    [InlineData("I'm from Rostov-on-Don. GitHub, Discord, ChatGPT.")]
    [InlineData("Не трогай Ростов Великий и пассивный залог.")]
    public void Similar_identifiers_and_unrelated_prose_are_preserved(string input) =>
        Assert.Equal(input, Processor.Process(input));
}
