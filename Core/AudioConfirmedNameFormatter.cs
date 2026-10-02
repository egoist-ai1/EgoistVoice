using System.Text;

namespace Egoist.Voice.Core;

/// <summary>
/// Changes known name spans or explicit abbreviations corroborated by a second recognition.
/// Call after punctuation composition, with both hypotheses from the same immutable audio chunk.
/// Whole-chunk agreement is required outside one bounded repair; this helper does not prove acoustic spelling.
/// </summary>
internal static class AudioConfirmedNameFormatter
{
    private const int MaximumInputCharacters = 32_768;
    private const int MaximumTokens = 512;

    // Even an ASR's Latin output cannot distinguish these ordinary words from brand homophones.
    // Never activate context-only terms, user regexes, generic inflections or conversational fixes.
    private static readonly HashSet<string> AmbiguousAliases = new(StringComparer.Ordinal)
    {
        "питон", "кодекс", "курсор", "мета", "хром", "нода", "редис", "стим", "телега",
        "опера", "лама", "сора", "клауд", "клауд код", "неон", "электрон", "блендер",
        "зум", "канва", "джейсон", "прометеус"
    };
    private static readonly HashSet<string> AmbiguousCanonicalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Codex", "Cursor", "Meta", "Zoom", "Neon", "Electron", "Blender", "Canva", "Rust", "Prometheus"
    };
    // General also contains conversational expressions; General is not a name safety class.
    private static readonly HashSet<string> NonNameCanonicalPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hello, my friend, how are you", "Hello, my friend", "How are you?", "by the way",
        "just in case", "check this out", "Let's go", "Thank you so much", "Thank you",
        "You're welcome", "Good luck", "Never mind", "looks good to me", "that makes sense",
        "makes sense", "step by step", "from scratch", "from Russia with love", "from Russia",
        "no problem", "to be honest", "Good job", "Well done", "See you later", "Take care",
        "one more thing", "as soon as possible", "Are you sure", "I don't know", "I have no idea",
        "of course", "Have a nice day", "Best regards", "keep in touch"
    };
    private static readonly IReadOnlyDictionary<string, Rule[]> Rules = BuildRules();

    internal static string Apply(string primaryComposed, string? secondaryAudioTranscript)
    {
        if (string.IsNullOrWhiteSpace(primaryComposed) || string.IsNullOrWhiteSpace(secondaryAudioTranscript) ||
            primaryComposed.Length > MaximumInputCharacters ||
            secondaryAudioTranscript.Length > MaximumInputCharacters ||
            !TryTokenize(primaryComposed, out var primaryTokens) ||
            !TryTokenize(secondaryAudioTranscript, out var secondaryTokens) ||
            primaryTokens.Count == 0 || secondaryTokens.Count == 0)
        {
            return primaryComposed;
        }

        var repaired = RepairCorroboratedSpan(primaryComposed, primaryTokens,
            secondaryAudioTranscript, secondaryTokens);
        if (!ReferenceEquals(repaired, primaryComposed))
        {
            primaryComposed = repaired;
            if (!TryTokenize(primaryComposed, out primaryTokens))
                return primaryComposed;
        }

        var primary = Collapse(primaryComposed, primaryTokens);
        var secondary = Collapse(secondaryAudioTranscript, secondaryTokens);
        if (primary.Count != secondary.Count)
        {
            return primaryComposed;
        }
        for (var index = 0; index < primary.Count; index++)
        {
            if (primary[index].Key != secondary[index].Key)
            {
                return primaryComposed;
            }
        }

        StringBuilder? result = null;
        var copiedUntil = 0;
        for (var index = 0; index < primary.Count; index++)
        {
            var item = primary[index];
            var other = secondary[index];
            if (item.Written is null || !other.IsExactCanonical ||
                primaryComposed.AsSpan(item.Start, item.Length).SequenceEqual(item.Written.AsSpan()))
            {
                continue;
            }

            result ??= new StringBuilder(primaryComposed.Length);
            result.Append(primaryComposed.AsSpan(copiedUntil, item.Start - copiedUntil));
            result.Append(item.Written);
            copiedUntil = item.Start + item.Length;
        }
        if (result is null)
        {
            return primaryComposed;
        }
        result.Append(primaryComposed.AsSpan(copiedUntil));
        return result.ToString();
    }

    private static readonly HashSet<string> NamePrepositions = new(StringComparer.Ordinal)
        { "с", "из", "в", "на", "к", "от", "о", "об", "по", "для", "без", "до", "у" };
    private static readonly string[] NameCaseEndings = ["", "а", "у", "ом", "е"];

    private static string RepairCorroboratedSpan(string primary, IReadOnlyList<Token> tokens,
        string secondary, IReadOnlyList<Token> otherTokens)
    {
        // One catalogue-backed repair per chunk, plus two unchanged lexical anchors.
        // No edit-distance guesses, arbitrary suffix changes, grammar or secondary-only words.
        var index = 0;
        var otherIndex = 0;
        var anchors = 0;
        Token? repair = null;
        string? replacement = null;
        while (index < tokens.Count && otherIndex < otherTokens.Count)
        {
            var token = tokens[index];
            var other = otherTokens[otherIndex];
            var otherSpan = secondary.Substring(other.Start, other.Length);
            if (TryRussianNameForm(otherSpan, out var nameAliases) &&
                nameAliases.Contains(token.Key, StringComparer.Ordinal))
            {
                if (!primary.AsSpan(token.Start, token.Length).SequenceEqual(otherSpan.AsSpan()))
                {
                    if (repair is not null) return primary;
                    repair = token;
                    replacement = otherSpan;
                }
                index++;
                otherIndex++;
                continue;
            }
            if (BuiltInVocabulary.AudioConfirmedAbbreviations.Contains(otherSpan, StringComparer.Ordinal) &&
                token.Key.Length <= other.Key.Length &&
                CollapseRepeatedLetters(other.Key) == CollapseRepeatedLetters(token.Key))
            {
                // Only repeated letters may be restored; every distinct letter must already exist.
                if (!primary.AsSpan(token.Start, token.Length).SequenceEqual(otherSpan.AsSpan()))
                {
                    if (repair is not null) return primary;
                    repair = token;
                    replacement = otherSpan;
                }
                index++;
                otherIndex++;
                continue;
            }
            if (token.Key == other.Key)
            {
                if (token.Key.Length >= 2 && token.Key.All(char.IsLetter)) anchors++;
                index++;
                otherIndex++;
                continue;
            }
            if (repair is null && NamePrepositions.Contains(other.Key) &&
                otherIndex + 1 < otherTokens.Count)
            {
                var name = otherTokens[otherIndex + 1];
                var nameSpan = secondary.Substring(name.Start, name.Length);
                var between = secondary.AsSpan(other.Start + other.Length,
                    name.Start - other.Start - other.Length);
                if (between.Length > 0 && IsOnlyWhitespace(between) &&
                    TryRussianNameForm(nameSpan, out var aliases) &&
                    aliases.Any(alias => token.Key == other.Key + alias))
                {
                    repair = token;
                    replacement = otherSpan + " " + nameSpan;
                    index++;
                    otherIndex += 2;
                    continue;
                }
            }
            return primary;
        }
        if (index != tokens.Count || otherIndex != otherTokens.Count || repair is null || anchors < 2)
            return primary;
        var span = repair.Value;
        return string.Concat(primary.AsSpan(0, span.Start), replacement,
            primary.AsSpan(span.Start + span.Length));
    }

    private static bool TryRussianNameForm(string text, out string[] aliases)
    {
        foreach (var (stem, spoken) in BuiltInVocabulary.AudioConfirmedRussianNames)
        {
            foreach (var ending in NameCaseEndings)
            {
                // Exact proper-name case in the secondary; retain the same suffix in the primary.
                if (text == stem + ending)
                {
                    aliases = spoken.Select(alias => alias + ending).ToArray();
                    return true;
                }
            }
        }
        aliases = [];
        return false;
    }

    private static bool IsOnlyWhitespace(ReadOnlySpan<char> text)
    {
        foreach (var rune in text.EnumerateRunes())
            if (!Rune.IsWhiteSpace(rune)) return false;
        return true;
    }

    private static string CollapseRepeatedLetters(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var character in text)
            if (result.Length == 0 || result[^1] != character) result.Append(character);
        return result.ToString();
    }

    private static IReadOnlyDictionary<string, Rule[]> BuildRules()
    {
        var rules = new List<Rule>();
        foreach (var term in BuiltInVocabulary.Terms)
        {
            if ((term.Profiles & EntityProfile.General) == 0 || term.Pattern is not null || !term.WholeWord ||
                AmbiguousCanonicalNames.Contains(term.Written) || NonNameCanonicalPhrases.Contains(term.Written) ||
                !IsLatinName(term.Written))
            {
                continue;
            }
            foreach (var alias in (term.Spoken ?? []).Append(term.Written))
            {
                if (!IsWordPhrase(alias) || AmbiguousAliases.Contains(Normalize(alias)))
                {
                    continue;
                }
                var keys = alias.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Normalize).ToArray();
                rules.Add(new Rule(keys, term.Written, term.BlockWhenTextContains));
            }
        }

        // An alias belonging to two different names is not a usable identity anchor.
        return rules.GroupBy(rule => string.Join(' ', rule.Keys), StringComparer.Ordinal)
            .Where(group => group.Select(rule => rule.Written).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .Select(group => group.First())
            .GroupBy(rule => rule.Keys[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.OrderByDescending(rule => rule.Keys.Length)
                    .ThenByDescending(rule => string.Join(' ', rule.Keys).Length).ToArray(), StringComparer.Ordinal);
    }

    private static bool IsLatinName(string text) =>
        text.Any(character => character is >= 'A' and <= 'Z') &&
        text.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or ' ') &&
        IsWordPhrase(text);

    private static bool IsWordPhrase(string text) => !string.IsNullOrWhiteSpace(text) &&
        text.EnumerateRunes().All(rune => IsLexical(rune) || Rune.IsWhiteSpace(rune)) &&
        !text.Contains('\r') && !text.Contains('\n');

    private static List<Item> Collapse(string text, IReadOnlyList<Token> tokens)
    {
        var result = new List<Item>();
        for (var index = 0; index < tokens.Count;)
        {
            Rule? match = null;
            if (Rules.TryGetValue(tokens[index].Key, out var candidates))
            {
                foreach (var rule in candidates)
                {
                    if (Matches(text, tokens, index, rule))
                    {
                        match = rule;
                        break;
                    }
                }
            }
            var token = tokens[index];
            if (match is null)
            {
                result.Add(new Item(token.Key, token.Start, token.Length, null, false));
                index++;
                continue;
            }
            var last = tokens[index + match.Keys.Length - 1];
            var length = last.Start + last.Length - token.Start;
            var exactCanonical = text.AsSpan(token.Start, length).Equals(match.Written.AsSpan(), StringComparison.OrdinalIgnoreCase);
            result.Add(new Item("entity:" + Normalize(match.Written), token.Start, length, match.Written, exactCanonical));
            index += match.Keys.Length;
        }
        return result;
    }

    private static bool Matches(string text, IReadOnlyList<Token> tokens, int start, Rule rule)
    {
        if (start + rule.Keys.Length > tokens.Count ||
            rule.BlockWhenTextContains?.Any(block => text.Contains(block, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return false;
        }
        for (var offset = 0; offset < rule.Keys.Length; offset++)
        {
            var token = tokens[start + offset];
            if (token.Key != rule.Keys[offset])
            {
                return false;
            }
            if (offset > 0)
            {
                var previous = tokens[start + offset - 1];
                foreach (var rune in text.AsSpan(previous.Start + previous.Length,
                    token.Start - previous.Start - previous.Length).EnumerateRunes())
                {
                    if (!Rune.IsWhiteSpace(rune))
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

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
                if (rune.Value is '\r' or '\n' ||
                    (!Rune.IsWhiteSpace(rune) && rune.Value is not (',' or '.' or '!' or '?' or ';' or ':' or
                        '—' or '–' or '-' or '\'' or '"' or '«' or '»' or '“' or '”')) ||
                    IsJoinedSyntax(text, offset, rune))
                {
                    return false;
                }
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
            tokens.Add(new Token(start, offset - start, Normalize(text.Substring(start, offset - start))));
        }
        return true;
    }

    private static bool IsJoinedSyntax(string text, int offset, Rune rune)
    {
        if (Rune.IsWhiteSpace(rune))
        {
            return false;
        }
        var nextOffset = offset + rune.Utf16SequenceLength;
        var nextLexical = nextOffset < text.Length && Rune.TryGetRuneAt(text, nextOffset, out var next) && IsLexical(next);
        var previousOffset = offset - 1;
        if (previousOffset > 0 && char.IsLowSurrogate(text[previousOffset]) && char.IsHighSurrogate(text[previousOffset - 1]))
        {
            previousOffset--;
        }
        var previousLexical = previousOffset >= 0 && Rune.TryGetRuneAt(text, previousOffset, out var previous) && IsLexical(previous);
        // Blocks domains, extensions, versions, decimals, hyphenated identifiers and leading .NET.
        return nextLexical && (previousLexical || rune.Value == '.');
    }

    private static bool IsLexical(Rune rune) => Rune.IsLetter(rune) || Rune.IsNumber(rune);
    private static string Normalize(string text) => text.ToLowerInvariant().Replace('ё', 'е');
    private sealed record Rule(string[] Keys, string Written, IReadOnlyList<string>? BlockWhenTextContains);
    private readonly record struct Token(int Start, int Length, string Key);
    private readonly record struct Item(string Key, int Start, int Length, string? Written, bool IsExactCanonical);
}
