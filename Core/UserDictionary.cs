using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Egoist.Voice.Core;

/// <summary>
/// One replacement rule. Either <see cref="Spoken"/> (literal forms, matched with Russian
/// inflection tolerance) or <see cref="Pattern"/> (a regular expression, for people who want it).
/// </summary>
public sealed record DictionaryTerm(
    [property: JsonPropertyName("spoken")] IReadOnlyList<string>? Spoken,
    [property: JsonPropertyName("written")] string Written,
    [property: JsonPropertyName("pattern")] string? Pattern = null,
    [property: JsonPropertyName("wholeWord")] bool WholeWord = true);

public sealed record UserDictionaryFile(
    [property: JsonPropertyName("terms")] IReadOnlyList<DictionaryTerm> Terms);

/// <summary>
/// Deterministic vocabulary substitution applied after recognition.
/// </summary>
/// <remarks>
/// This is the project's answer to contextual biasing, which is architecturally unavailable:
/// sherpa-onnx hotwords require a transducer with modified_beam_search, and GigaAM is present there
/// as a NeMo CTC model. A measured comparison also favours this route — deterministic substitution
/// achieves the same 1–2 percentage points as neural correction at 0.02 ms instead of 80 ms, and
/// without the failure mode where the corrector "fixes" text that was already right.
/// </remarks>
public sealed partial class UserDictionary
{
    private readonly List<CompiledTerm> _terms;

    private UserDictionary(List<CompiledTerm> terms) => _terms = terms;

    /// <summary>Truly empty. Used by tests and by the disabled path, not as a default.</summary>
    public static UserDictionary Empty { get; } = new([]);

    private static UserDictionary? _builtIn;

    /// <summary>
    /// What the application ships with. This is the default the pipeline uses when the user has no
    /// dictionary of their own — previously that case fell back to <see cref="Empty"/>, which meant
    /// every known term stayed in Cyrillic until somebody wrote the rules by hand.
    /// </summary>
    /// <remarks>
    /// Built on first use rather than in a field initializer. Static initializers run in textual
    /// order, so a field declared here would be constructed before <c>CaseEndings</c> below it had
    /// a value — and would throw inside a type initializer, which surfaces as an unrelated error
    /// everywhere the type is touched.
    /// </remarks>
    public static UserDictionary BuiltIn => _builtIn ??= FromTerms(BuiltInVocabulary.Terms);

    public int Count => _terms.Count;

    /// <summary>Spoken forms, used to widen the mixed-speech suspicion map.</summary>
    public IEnumerable<string> SpokenForms => _terms
        .Where(term => term.Literal is not null)
        .Select(term => term.Literal!);

    /// <summary>
    /// Parses a user dictionary and layers it over the built-in one. The user's entries are added
    /// last, so an identical spoken form written by the user wins — that is how a shipped rule gets
    /// overridden without needing a separate "remove" syntax.
    /// </summary>
    public static UserDictionary Parse(string json)
    {
        var file = JsonSerializer.Deserialize<UserDictionaryFile>(
            json,
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return FromTerms(BuiltInVocabulary.Terms.Concat(file?.Terms ?? []));
    }

    public static UserDictionary FromTerms(IEnumerable<DictionaryTerm> terms)
    {
        var compiled = new List<CompiledTerm>();
        foreach (var term in terms)
        {
            if (string.IsNullOrEmpty(term.Written))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(term.Pattern))
            {
                try
                {
                    // A timeout, because this runs after every dictation: catastrophic backtracking
                    // in somebody's own rule would otherwise hang the pipeline outright.
                    compiled.Add(new CompiledTerm(
                        new Regex(
                            term.Pattern,
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                            PatternTimeout),
                        term.Written,
                        null,
                        null));
                }
                catch (ArgumentException)
                {
                    // A malformed user pattern must not take the whole dictionary down with it.
                }
                continue;
            }

            foreach (var spoken in term.Spoken ?? [])
            {
                if (string.IsNullOrWhiteSpace(spoken))
                {
                    continue;
                }

                compiled.Add(new CompiledTerm(
                    BuildRegex(spoken, term.WholeWord),
                    term.Written,
                    Fold(spoken),
                    DedupKey(spoken)));
            }
        }

        // A spoken form defined twice means the user redefined a shipped rule. Keeping the later
        // one is what lets them override "кодекс" without a separate removal syntax.
        // Keyed on the spoken form as written, not on its letters-only fold. Folding collapses
        // "гитхаб" and "гит хаб" onto the same key, and keeping only one of them silently drops a
        // variant the user may well be the one who says.
        var deduplicated = new List<CompiledTerm>(compiled.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = compiled.Count - 1; index >= 0; index--)
        {
            var term = compiled[index];
            if (term.DedupKey is null || seen.Add(term.DedupKey))
            {
                deduplicated.Add(term);
            }
        }

        // Longest first: "visual studio code" must win over "code", otherwise the shorter rule
        // eats the head of the longer one and the result is nonsense.
        deduplicated.Sort((left, right) => right.SortLength.CompareTo(left.SortLength));
        return new UserDictionary(deduplicated);
    }

    public string Apply(string text)
    {
        if (_terms.Count == 0 || string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var term in _terms)
        {
            try
            {
                // A MatchEvaluator, not a replacement string: Regex.Replace would interpret "$1",
                // "$&" and "$$" inside the user's written form, so a term written as "$100" came
                // out as garbage or threw.
                text = term.Expression.Replace(text, _ => term.Written);
            }
            catch (RegexMatchTimeoutException)
            {
                AppLogWrite($"Правило словаря «{term.Written}» превысило лимит времени и пропущено");
            }
        }
        return text;
    }

    /// <summary>
    /// Indirection so this type stays free of a service dependency; the host wires it once.
    /// </summary>
    internal static Action<string> AppLogWrite { get; set; } = _ => { };

    /// <summary>
    /// Russian case endings, longest first. An explicit list rather than "up to three letters":
    /// the loose form matched "кодекс" and "кодер" for the term "код", which is not tolerance of
    /// inflection but a wrong replacement in the middle of a sentence.
    /// </summary>
    private static readonly string[] CaseEndings =
    [
        "ами", "ями", "ах", "ях", "ов", "ев", "ем", "ом", "ой", "ей", "ий", "ый", "ые", "ым", "ых",
        "ам", "ям", "ах", "ую", "юю", "ее", "ие", "а", "у", "е", "ы", "и", "о", "я", "ю", "й", "ь"
    ];

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(50);

    private static Regex BuildRegex(string spoken, bool wholeWord)
    {
        var builder = new StringBuilder();
        if (wholeWord)
        {
            builder.Append(@"(?<![\p{L}\p{N}])");
        }

        var words = spoken.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < words.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(@"[\s\-]+");
            }
            builder.Append(Regex.Escape(words[index]));
        }

        if (wholeWord)
        {
            builder.Append("(?:");
            builder.Append(string.Join('|', CaseEndings.Distinct().Select(Regex.Escape)));
            builder.Append(@")?(?![\p{L}\p{N}])");
        }

        return new Regex(
            builder.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            PatternTimeout);
    }

    private static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetter(character))
            {
                var lowered = char.ToLowerInvariant(character);
                builder.Append(lowered is 'ё' ? 'е' : lowered);
            }
        }
        return builder.ToString();
    }

    /// <summary>Lower-cased and whitespace-collapsed, but otherwise the form as the author wrote it.</summary>
    private static string DedupKey(string spoken) =>
        string.Join(' ', spoken.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private sealed record CompiledTerm(Regex Expression, string Written, string? Literal, string? DedupKey)
    {
        public int SortLength => Literal?.Length ?? int.MaxValue;
    }
}
