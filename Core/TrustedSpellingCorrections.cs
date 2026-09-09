using System.Text.RegularExpressions;

namespace Egoist.Voice.Core;

/// <summary>
/// Corrects a deliberately small set of confirmed spelling errors without formatting or
/// interpreting the rest of a literal transcript.
/// </summary>
internal static partial class TrustedSpellingCorrections
{
    public static string Apply(string text)
    {
        text = EgoistGamesOneAliases().Replace(text, "Egoist Games One");
        text = EgoistShieldAliases().Replace(text, "Egoist Shield");
        text = EgoistVoiceAliases().Replace(text, "Egoist Voice");
        return RepositoryTypo().Replace(text, match => InsertMissingLetter(match.Value));
    }

    private static string InsertMissingLetter(string source)
    {
        var missingLetter = source.All(character => !char.IsLetter(character) || char.IsUpper(character))
            ? "И"
            : "и";
        return source.Insert(source.Length - 1, missingLetter);
    }

    // The path/identifier guards mirror the shipped dictionary boundary policy. Only the complete
    // reported phrases are trusted here; standalone lookalikes remain decoder text.
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_/\\@])(?:egast|egaist|egoist)[ \t]+games[ \t]+one(?![\p{L}\p{N}_/\\@]|\.[\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        50)]
    private static partial Regex EgoistGamesOneAliases();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_/\\@])(?:egast|egaist|egoist)[ \t]+(?:shild|shield)(?![\p{L}\p{N}_/\\@]|\.[\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        50)]
    private static partial Regex EgoistShieldAliases();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_/\\@])(?:egast|egaist|egoist)[ \t]+voice(?![\p{L}\p{N}_/\\@]|\.[\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        50)]
    private static partial Regex EgoistVoiceAliases();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_/\\@])репозиторй(?![\p{L}\p{N}_/\\@]|\.[\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        50)]
    private static partial Regex RepositoryTypo();
}
