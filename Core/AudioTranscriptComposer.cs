using System.Text;

namespace Egoist.Voice.Core;

/// <summary>
/// Projects punctuation from a second recognition of the same audio onto the primary words.
/// The secondary hypothesis never supplies words, spelling, Latin case or digits.
/// </summary>
public static class AudioTranscriptComposer
{
    // Dictation-sized alignment only. Larger/structured input safely retains the primary text.
    // The largest edit-distance table is one MiB, regardless of caller input length.
    private const int MaximumInputCharacters = 32_768;
    private const int MaximumTokens = 512;
    private const int MaximumAlignmentCells = 262_144;

    public static string Compose(string? primary, string? secondaryAudioTranscript)
    {
        if (string.IsNullOrWhiteSpace(primary))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(secondaryAudioTranscript) ||
            primary.Length > MaximumInputCharacters ||
            secondaryAudioTranscript.Length > MaximumInputCharacters ||
            !IsPlainPrimary(primary))
        {
            return primary;
        }

        if (!TryTokenize(primary, out var words) ||
            !TryTokenize(secondaryAudioTranscript, out var formattedWords) ||
            words.Count == 0 || formattedWords.Count == 0 ||
            (long)(words.Count + 1) * (formattedWords.Count + 1) > MaximumAlignmentCells)
        {
            return primary;
        }

        var mapping = Align(words, formattedWords);
        var result = new StringBuilder(primary.Length);
        for (var index = 0; index < words.Count; index++)
        {
            if (index > 0)
            {
                result.Append(' ');
            }

            var word = words[index];
            var otherIndex = mapping[index];
            var capitalize = otherIndex >= 0 && CanCapitalize(primary, word) &&
                Rune.IsUpper(Rune.GetRuneAt(secondaryAudioTranscript, formattedWords[otherIndex].Start));
            if (capitalize)
            {
                result.Append(char.ToUpperInvariant(primary[word.Start]));
                result.Append(primary.AsSpan(word.Start + 1, word.Length - 1));
            }
            else
            {
                result.Append(primary.AsSpan(word.Start, word.Length));
            }

            if (otherIndex >= 0)
            {
                var other = formattedWords[otherIndex];
                var afterEnd = otherIndex + 1 < formattedWords.Count
                    ? formattedWords[otherIndex + 1].Start
                    : secondaryAudioTranscript.Length;
                for (var offset = other.Start + other.Length; offset < afterEnd; offset++)
                {
                    var character = secondaryAudioTranscript[offset];
                    if (character is ',' or '.' or '!' or '?' or ';' or ':' or '—' or '–' or '-')
                    {
                        result.Append(character);
                    }
                }
            }
        }

        return result.ToString();
    }

    private static bool IsPlainPrimary(string text)
    {
        // Explicit punctuation/line structure, code, URLs, paths, decimal/version literals and
        // identifiers with separators already have meaning. Do not reconstruct their syntax.
        // This deliberately protects the whole primary when such a span occurs in a phrase.
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is '\r' or '\n' ||
                (!IsLexical(rune) && !Rune.IsWhiteSpace(rune)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanCapitalize(string text, Token word)
    {
        // Only the first Cyrillic letter may change case. Mixed/Latin names and digit labels
        // retain the exact primary spelling and case, including a lower-case product name.
        if (text[word.Start] is not (>= '\u0400' and <= '\u052f'))
        {
            return false;
        }

        foreach (var rune in text.AsSpan(word.Start, word.Length).EnumerateRunes())
        {
            if (Rune.IsNumber(rune) || rune.Value is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLexical(Rune rune) => Rune.IsLetter(rune) || Rune.IsNumber(rune);

    private static bool TryTokenize(string text, out List<Token> tokens)
    {
        tokens = new List<Token>();
        var offset = 0;
        while (offset < text.Length)
        {
            if (!Rune.TryGetRuneAt(text, offset, out var rune))
            {
                return false;
            }
            if (!IsLexical(rune))
            {
                offset += rune.Utf16SequenceLength;
                continue;
            }

            if (tokens.Count == MaximumTokens)
            {
                return false;
            }

            var start = offset;
            do
            {
                offset += rune.Utf16SequenceLength;
                if (offset == text.Length)
                {
                    break;
                }
                if (!Rune.TryGetRuneAt(text, offset, out rune))
                {
                    return false;
                }
            }
            while (IsLexical(rune));

            var length = offset - start;
            var key = text.Substring(start, length).ToLowerInvariant().Replace('ё', 'е');
            tokens.Add(new Token(start, length, key));
        }

        return true;
    }

    private static int[] Align(IReadOnlyList<Token> primary, IReadOnlyList<Token> secondary)
    {
        var width = secondary.Count + 1;
        var distance = new int[(primary.Count + 1) * width];
        for (var row = 0; row <= primary.Count; row++)
        {
            distance[row * width] = row;
        }
        for (var column = 0; column < width; column++)
        {
            distance[column] = column;
        }
        for (var row = 1; row <= primary.Count; row++)
        {
            for (var column = 1; column < width; column++)
            {
                var substitution = primary[row - 1].Key == secondary[column - 1].Key ? 0 : 1;
                distance[row * width + column] = Math.Min(
                    Math.Min(distance[(row - 1) * width + column] + 1,
                        distance[row * width + column - 1] + 1),
                    distance[(row - 1) * width + column - 1] + substitution);
            }
        }

        var mapping = new int[primary.Count];
        Array.Fill(mapping, -1);
        var x = primary.Count;
        var y = secondary.Count;
        while (x > 0 || y > 0)
        {
            var substitution = x > 0 && y > 0 && primary[x - 1].Key == secondary[y - 1].Key ? 0 : 1;
            // The prototype's deterministic tie order is diagonal, primary deletion, insertion.
            // Even a substituted anchor may supply punctuation, but never its secondary word.
            if (x > 0 && y > 0 &&
                distance[x * width + y] == distance[(x - 1) * width + y - 1] + substitution)
            {
                mapping[x - 1] = y - 1;
                x--;
                y--;
            }
            else if (x > 0 && distance[x * width + y] == distance[(x - 1) * width + y] + 1)
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        return mapping;
    }

    private readonly record struct Token(int Start, int Length, string Key);
}
