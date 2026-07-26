using System.Text.RegularExpressions;

namespace Egoist.Voice.Core;

public static partial class TranscriptNormalizer
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var paragraphs = ParagraphBreakRegex()
            .Split(input.Trim())
            .Select(NormalizeParagraph)
            .Where(paragraph => paragraph.Length > 0);

        return string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
    }

    /// <summary>
    /// Single line breaks survive normalization. They used to be collapsed into spaces along with
    /// every other whitespace run, which silently undid the "new line" voice command.
    /// </summary>
    private static string NormalizeParagraph(string input)
    {
        var lines = LineBreakRegex()
            .Split(input.Trim())
            .Select(NormalizeLine)
            .Where(line => line.Length > 0)
            .ToArray();

        if (lines.Length == 0)
        {
            return string.Empty;
        }

        lines[0] = Capitalize(lines[0]);
        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeLine(string input)
    {
        var text = WhitespaceRegex().Replace(input.Trim(), " ");
        text = SpaceBeforePunctuationRegex().Replace(text, "$1");
        text = SpaceAfterOpeningBracketRegex().Replace(text, "$1");
        text = SpaceBeforeClosingBracketRegex().Replace(text, "$1");

        return text;
    }

    private static string Capitalize(string text) =>
        text.Length > 0 && char.IsLetter(text[0]) && char.IsLower(text[0])
            ? char.ToUpperInvariant(text[0]) + text[1..]
            : text;

    [GeneratedRegex(@"(?:[ \t]*\r?\n[ \t]*){2,}")]
    private static partial Regex ParagraphBreakRegex();

    [GeneratedRegex(@"[ \t]*\r?\n[ \t]*")]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\s+([,.;:!?])")]
    private static partial Regex SpaceBeforePunctuationRegex();

    [GeneratedRegex(@"([\(\[«])\s+")]
    private static partial Regex SpaceAfterOpeningBracketRegex();

    [GeneratedRegex(@"\s+([\)\]»])")]
    private static partial Regex SpaceBeforeClosingBracketRegex();
}
