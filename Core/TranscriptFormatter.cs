using System.Text;

namespace Egoist.Voice.Core;

public sealed record TranscriptSegment(string Text, TimeSpan Start, TimeSpan End);

public static class TranscriptFormatter
{
    private static readonly TimeSpan ParagraphPause = TimeSpan.FromMilliseconds(1100);
    private const int MinimumParagraphCharacters = 80;
    private const int PreferredParagraphCharacters = 420;

    public static string Format(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder();
        TimeSpan? previousEnd = null;
        var currentParagraphCharacters = 0;

        foreach (var segment in segments)
        {
            var text = segment.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var pause = previousEnd is null ? TimeSpan.Zero : segment.Start - previousEnd.Value;
            var paragraphBreak = result.Length > 0 &&
                ((pause >= ParagraphPause && currentParagraphCharacters >= MinimumParagraphCharacters) ||
                 (currentParagraphCharacters >= PreferredParagraphCharacters && EndsSentence(result)));

            if (result.Length > 0)
            {
                if (paragraphBreak)
                {
                    result.AppendLine();
                    result.AppendLine();
                    currentParagraphCharacters = 0;
                }
                else if (!char.IsWhiteSpace(result[^1]) && !StartsWithClosingPunctuation(text))
                {
                    result.Append(' ');
                    currentParagraphCharacters++;
                }
            }

            result.Append(text);
            currentParagraphCharacters += text.Length;
            previousEnd = segment.End;
        }

        return result.ToString();
    }

    private static bool EndsSentence(StringBuilder text)
    {
        for (var index = text.Length - 1; index >= 0; index--)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                continue;
            }

            return text[index] is '.' or '!' or '?' or '…';
        }

        return false;
    }

    private static bool StartsWithClosingPunctuation(string text) =>
        text[0] is ',' or '.' or ';' or ':' or '!' or '?' or ')' or ']' or '»';
}
