using System.Globalization;
using System.Text;

namespace Egoist.Voice.Core;

/// <summary>
/// How aggressively a transcript is normalized before it is compared to a reference. Scores are
/// only comparable between runs that used the same options, so the choice belongs in the report.
/// </summary>
public sealed record ScoringOptions(
    bool IgnoreCase = true,
    bool IgnorePunctuation = true,
    bool FoldYo = true,
    bool ExpandDigits = false)
{
    public static ScoringOptions Default { get; } = new();
}

/// <summary>
/// Error counts, deliberately kept as counts rather than rates. Aggregating a corpus means summing
/// errors and reference lengths — averaging per-clip rates would let a three-word clip outweigh a
/// two-minute one.
/// </summary>
public sealed record RecognitionScore(
    int ReferenceWords,
    int WordErrors,
    int ReferenceCharacters,
    int CharacterErrors)
{
    public double WordErrorRate => ReferenceWords == 0 ? 0 : WordErrors / (double)ReferenceWords;
    public double CharacterErrorRate =>
        ReferenceCharacters == 0 ? 0 : CharacterErrors / (double)ReferenceCharacters;

    public static RecognitionScore Empty { get; } = new(0, 0, 0, 0);

    public RecognitionScore Add(RecognitionScore other) => new(
        ReferenceWords + other.ReferenceWords,
        WordErrors + other.WordErrors,
        ReferenceCharacters + other.ReferenceCharacters,
        CharacterErrors + other.CharacterErrors);
}

public static class RecognitionScorer
{
    private static readonly string[] DigitWords =
    [
        "ноль", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"
    ];

    public static RecognitionScore Score(string reference, string hypothesis, ScoringOptions? options = null)
    {
        options ??= ScoringOptions.Default;
        var referenceWords = Tokenize(reference, options);
        var hypothesisWords = Tokenize(hypothesis, options);
        var wordErrors = EditDistance<string>(referenceWords, hypothesisWords, StringComparer.Ordinal);

        var referenceChars = string.Join(' ', referenceWords);
        var hypothesisChars = string.Join(' ', hypothesisWords);
        var characterErrors = EditDistance(
            referenceChars.ToCharArray(),
            hypothesisChars.ToCharArray(),
            EqualityComparer<char>.Default);

        return new RecognitionScore(
            referenceWords.Length,
            wordErrors,
            referenceChars.Length,
            characterErrors);
    }

    public static RecognitionScore Aggregate(IEnumerable<RecognitionScore> scores) =>
        scores.Aggregate(RecognitionScore.Empty, (total, score) => total.Add(score));

    public static string[] Tokenize(string text, ScoringOptions options)
    {
        var normalized = Normalize(text, options);
        return normalized.Length == 0
            ? []
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static string Normalize(string text, ScoringOptions options)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                Append(builder, ' ');
                continue;
            }

            // Explicitly ASCII: char.IsDigit accepts Arabic-Indic and other digit ranges, and the
            // subtraction below would then index far outside DigitWords.
            if (character is >= '0' and <= '9')
            {
                if (options.ExpandDigits)
                {
                    // Written and spoken forms of the same number must not count as an error, so
                    // both sides collapse to words before comparison.
                    Append(builder, ' ');
                    builder.Append(DigitWords[character - '0']);
                    Append(builder, ' ');
                }
                else
                {
                    builder.Append(character);
                }
                continue;
            }

            if (!char.IsLetter(character))
            {
                if (options.IgnorePunctuation)
                {
                    Append(builder, ' ');
                }
                else
                {
                    builder.Append(character);
                }
                continue;
            }

            var letter = options.IgnoreCase
                ? char.ToLower(character, CultureInfo.InvariantCulture)
                : character;
            if (options.FoldYo)
            {
                letter = letter switch { 'ё' => 'е', 'Ё' => 'Е', _ => letter };
            }
            builder.Append(letter);
        }

        return builder.ToString().Trim();
    }

    private static void Append(StringBuilder builder, char separator)
    {
        if (builder.Length > 0 && builder[^1] != separator)
        {
            builder.Append(separator);
        }
    }

    /// <summary>
    /// Levenshtein distance over two rolling rows. The full matrix is never allocated: a
    /// three-minute dictation against its reference would otherwise cost tens of megabytes.
    /// </summary>
    private static int EditDistance<T>(
        IReadOnlyList<T> reference,
        IReadOnlyList<T> hypothesis,
        IEqualityComparer<T> comparer)
    {
        if (reference.Count == 0)
        {
            return hypothesis.Count;
        }
        if (hypothesis.Count == 0)
        {
            return reference.Count;
        }

        var previous = new int[hypothesis.Count + 1];
        var current = new int[hypothesis.Count + 1];
        for (var column = 0; column <= hypothesis.Count; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= reference.Count; row++)
        {
            current[0] = row;
            for (var column = 1; column <= hypothesis.Count; column++)
            {
                var substitution = previous[column - 1] +
                    (comparer.Equals(reference[row - 1], hypothesis[column - 1]) ? 0 : 1);
                var deletion = previous[column] + 1;
                var insertion = current[column - 1] + 1;
                current[column] = Math.Min(substitution, Math.Min(deletion, insertion));
            }

            (previous, current) = (current, previous);
        }

        return previous[hypothesis.Count];
    }
}
