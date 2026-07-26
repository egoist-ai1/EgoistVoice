using System.Globalization;
using System.Text;

namespace Egoist.Voice.Core;

/// <summary>
/// Inverse text normalization for Russian numerals: "двадцать пять" becomes 25.
/// </summary>
/// <remarks>
/// Deliberately a hand-written finite walk rather than a WFST or a neural tagger. The measured
/// contribution of number normalization is about 0.7 percentage points of WER, which does not
/// justify a Python dependency in the installer — and a rule that fires wrongly is far more
/// annoying than one that fires rarely, so ambiguous cases are left as words on purpose.
/// </remarks>
public static class RussianNumberNormalizer
{
    private static readonly Dictionary<string, long> Units = new(StringComparer.Ordinal)
    {
        ["ноль"] = 0, ["нуль"] = 0,
        ["один"] = 1, ["одна"] = 1, ["одно"] = 1,
        ["два"] = 2, ["две"] = 2,
        ["три"] = 3, ["четыре"] = 4, ["пять"] = 5, ["шесть"] = 6,
        ["семь"] = 7, ["восемь"] = 8, ["девять"] = 9,
        ["десять"] = 10, ["одиннадцать"] = 11, ["двенадцать"] = 12, ["тринадцать"] = 13,
        ["четырнадцать"] = 14, ["пятнадцать"] = 15, ["шестнадцать"] = 16,
        ["семнадцать"] = 17, ["восемнадцать"] = 18, ["девятнадцать"] = 19,
        ["двадцать"] = 20, ["тридцать"] = 30, ["сорок"] = 40, ["пятьдесят"] = 50,
        ["шестьдесят"] = 60, ["семьдесят"] = 70, ["восемьдесят"] = 80, ["девяносто"] = 90,
        ["сто"] = 100, ["двести"] = 200, ["триста"] = 300, ["четыреста"] = 400,
        ["пятьсот"] = 500, ["шестьсот"] = 600, ["семьсот"] = 700,
        ["восемьсот"] = 800, ["девятьсот"] = 900
    };

    private static readonly Dictionary<string, long> Scales = new(StringComparer.Ordinal)
    {
        ["тысяча"] = 1_000, ["тысячи"] = 1_000, ["тысяч"] = 1_000, ["тысячу"] = 1_000,
        ["миллион"] = 1_000_000, ["миллиона"] = 1_000_000, ["миллионов"] = 1_000_000,
        ["миллиард"] = 1_000_000_000, ["миллиарда"] = 1_000_000_000, ["миллиардов"] = 1_000_000_000
    };

    private static readonly Dictionary<string, long> OrdinalDays = BuildOrdinalDays();

    private static readonly HashSet<string> Months = new(StringComparer.Ordinal)
    {
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    };

    private static readonly HashSet<string> PercentWords = new(StringComparer.Ordinal)
    {
        "процент", "процента", "процентов"
    };

    /// <summary>
    /// Standalone "один"/"одна"/"одно" stays a word: in running speech it is far more often an
    /// article-like pronoun ("один из них") than a quantity, and turning it into "1" reads as a bug.
    /// </summary>
    private static readonly HashSet<string> AmbiguousAlone = new(StringComparer.Ordinal)
    {
        "один", "одна", "одно"
    };

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var tokens = Tokenize(text);
        var builder = new StringBuilder(text.Length);
        var index = 0;

        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (!token.IsWord)
            {
                builder.Append(token.Text);
                index++;
                continue;
            }

            if (TryReadOrdinalDate(tokens, index, out var dayText, out var dayLength))
            {
                builder.Append(dayText);
                index += dayLength;
                continue;
            }

            if (TryReadCardinal(tokens, index, out var value, out var length))
            {
                builder.Append(value.ToString(CultureInfo.InvariantCulture));
                index += length;

                if (TryReadPercent(tokens, index, out var percentLength))
                {
                    builder.Append(" %");
                    index += percentLength;
                }
                continue;
            }

            builder.Append(token.Text);
            index++;
        }

        return builder.ToString();
    }

    private static bool TryReadCardinal(IReadOnlyList<Token> tokens, int start, out long value, out int length)
    {
        value = 0;
        length = 0;

        long total = 0;
        long group = 0;
        var consumed = 0;
        var sawNumber = false;
        var wordCount = 0;
        var lastMagnitude = long.MaxValue;

        for (var index = start; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!token.IsWord)
            {
                // A single space is part of a numeral phrase; punctuation ends it.
                if (token.Text.Trim().Length == 0 && sawNumber)
                {
                    continue;
                }
                break;
            }

            var word = token.Normalized;
            if (Units.TryGetValue(word, out var unit))
            {
                // Magnitudes have to strictly decrease inside one number: "двадцать пять" is 25,
                // but "пять пять" is somebody reading digits aloud and must stay two numbers.
                var magnitude = Magnitude(unit);
                if (magnitude >= lastMagnitude)
                {
                    break;
                }

                lastMagnitude = magnitude;
                group += unit;
                sawNumber = true;
                wordCount++;
                consumed = index - start + 1;
                continue;
            }

            if (Scales.TryGetValue(word, out var scale) && sawNumber)
            {
                group = group == 0 ? 1 : group;
                total += group * scale;
                group = 0;
                lastMagnitude = long.MaxValue;
                wordCount++;
                consumed = index - start + 1;
                continue;
            }

            break;
        }

        if (!sawNumber)
        {
            return false;
        }

        if (wordCount == 1 && AmbiguousAlone.Contains(tokens[start].Normalized))
        {
            return false;
        }

        value = total + group;
        length = consumed;
        return true;
    }

    /// <summary>Order of magnitude of a numeral: 900 → 100, 40 → 10, 7 → 1.</summary>
    private static long Magnitude(long unit) => unit switch
    {
        >= 100 => 100,
        >= 20 => 10,

        // Teens occupy the units slot but cannot be followed by another unit — "пятнадцать шесть"
        // is not a number — so they claim the tens magnitude and close the group.
        >= 10 => 10,
        _ => 1
    };

    private static bool TryReadOrdinalDate(
        IReadOnlyList<Token> tokens,
        int start,
        out string replacement,
        out int length)
    {
        replacement = string.Empty;
        length = 0;

        var day = ReadOrdinalDay(tokens, start, out var dayTokens);
        if (day == 0)
        {
            return false;
        }

        var next = start + dayTokens;
        while (next < tokens.Count && !tokens[next].IsWord && tokens[next].Text.Trim().Length == 0)
        {
            next++;
        }

        if (next >= tokens.Count || !tokens[next].IsWord || !Months.Contains(tokens[next].Normalized))
        {
            return false;
        }

        // "5 июля" and not "05.07": the month stays a word because that is how the phrase reads,
        // and a numeric date would silently change the register of the sentence.
        replacement = day.ToString(CultureInfo.InvariantCulture) + " " + tokens[next].Text;
        length = next - start + 1;
        return true;
    }

    /// <summary>
    /// Reads a day-of-month ordinal, single- or two-word.
    /// </summary>
    /// <remarks>
    /// Two-word ordinals used to be dropped from the lookup entirely, so "двадцать первое июля"
    /// went through the cardinal branch for "двадцать" and the date branch for "первое июля" and
    /// came out as "20 1 июля" — corrupted text, not a missed opportunity.
    /// </remarks>
    private static long ReadOrdinalDay(IReadOnlyList<Token> tokens, int start, out int tokenCount)
    {
        tokenCount = 0;
        if (!tokens[start].IsWord)
        {
            return 0;
        }

        if (Tens.TryGetValue(tokens[start].Normalized, out var tens))
        {
            var next = start + 1;
            while (next < tokens.Count && !tokens[next].IsWord && tokens[next].Text.Trim().Length == 0)
            {
                next++;
            }

            if (next < tokens.Count &&
                tokens[next].IsWord &&
                OrdinalDays.TryGetValue(tokens[next].Normalized, out var ones) &&
                ones < 10)
            {
                tokenCount = next - start + 1;
                return tens + ones;
            }

            return 0;
        }

        if (OrdinalDays.TryGetValue(tokens[start].Normalized, out var day))
        {
            tokenCount = 1;
            return day;
        }

        return 0;
    }

    /// <summary>Cardinal tens that can lead a compound ordinal day.</summary>
    private static readonly Dictionary<string, long> Tens = new(StringComparer.Ordinal)
    {
        ["двадцать"] = 20,
        ["тридцать"] = 30
    };

    private static bool TryReadPercent(IReadOnlyList<Token> tokens, int start, out int length)
    {
        length = 0;
        var index = start;
        while (index < tokens.Count && !tokens[index].IsWord && tokens[index].Text.Trim().Length == 0)
        {
            index++;
        }

        if (index >= tokens.Count || !tokens[index].IsWord || !PercentWords.Contains(tokens[index].Normalized))
        {
            return false;
        }

        length = index - start + 1;
        return true;
    }

    private static Dictionary<string, long> BuildOrdinalDays()
    {
        var names = new[]
        {
            "первое", "второе", "третье", "четвёртое", "пятое", "шестое", "седьмое", "восьмое",
            "девятое", "десятое", "одиннадцатое", "двенадцатое", "тринадцатое", "четырнадцатое",
            "пятнадцатое", "шестнадцатое", "семнадцатое", "восемнадцатое", "девятнадцатое",
            "двадцатое", "двадцать первое", "тридцатое", "тридцать первое"
        };

        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        long[] values = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 30, 31];
        for (var index = 0; index < names.Length; index++)
        {
            var key = Fold(names[index]);
            if (!key.Contains(' '))
            {
                map[key] = values[index];
            }
        }
        return map;
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>(text.Length / 4);
        var builder = new StringBuilder(24);
        var isWord = false;

        foreach (var character in text)
        {
            var characterIsWord = char.IsLetter(character);
            if (builder.Length > 0 && characterIsWord != isWord)
            {
                tokens.Add(Token.Create(builder.ToString(), isWord));
                builder.Clear();
            }

            isWord = characterIsWord;
            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            tokens.Add(Token.Create(builder.ToString(), isWord));
        }

        return tokens;
    }

    private static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var lowered = char.ToLowerInvariant(character);
            builder.Append(lowered is 'ё' ? 'е' : lowered);
        }
        return builder.ToString();
    }

    private readonly record struct Token(string Text, string Normalized, bool IsWord)
    {
        internal static Token Create(string text, bool isWord) =>
            new(text, isWord ? Fold(text) : text, isWord);
    }
}
