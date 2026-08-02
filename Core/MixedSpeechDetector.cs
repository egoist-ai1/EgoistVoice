using System.Text;

namespace Egoist.Voice.Core;

public enum MixedSpeechTrigger
{
    /// <summary>Pure Russian as far as anything can tell — the fallback engine is not worth its latency.</summary>
    None,

    /// <summary>The primary engine already produced Latin script, so it was reaching for English.</summary>
    LatinScript,

    /// <summary>A word matched a known phonetic russification of a technical term ("джемини").</summary>
    RussifiedTerm,

    /// <summary>The user or an application profile asked for mixed-language handling explicitly.</summary>
    Requested
}

public sealed record MixedSpeechDecision(MixedSpeechTrigger Trigger, string? Evidence)
{
    public bool NeedsFallback => Trigger != MixedSpeechTrigger.None;

    public static MixedSpeechDecision NotNeeded { get; } = new(MixedSpeechTrigger.None, null);
}

/// <summary>
/// Decides whether the Whisper fallback is worth running at all.
/// </summary>
/// <remarks>
/// Running both engines on every dictation cost roughly 0.28 s out of a 0.4 s p95 while the
/// fallback's output was discarded almost every time. The saving is only safe if the detector
/// catches the case it exists for, and the hard case is a term GigaAM russifies completely —
/// "Gemini" becomes "джемини" with no Latin letter left to notice. Hence the suspicion map
/// alongside the trivial script check.
/// </remarks>
public sealed class MixedSpeechDetector
{
    private readonly IReadOnlyCollection<string> _russifiedForms;

    public MixedSpeechDetector(IEnumerable<string>? additionalRussifiedForms = null)
    {
        var forms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in BuiltInRussifiedForms.Concat(additionalRussifiedForms ?? []))
        {
            var normalized = NormalizeWord(form);
            if (normalized.Length >= MinimumFormLength)
            {
                forms.Add(normalized);
            }
        }
        _russifiedForms = forms;
    }

    /// <summary>
    /// Five, not four. Shorter stems produce false triggers on ordinary Russian: "интел" matched
    /// "интеллект", "хром" matched "хромает", "стим" matched "стимул". Each false trigger costs a
    /// full Whisper pass — the exact latency this detector exists to avoid.
    /// </summary>
    private const int MinimumFormLength = 5;

    /// <summary>Longest inflection tail accepted after a stem.</summary>
    private const int MaximumEndingLength = 3;

    /// <summary>
    /// Stems rather than full words: Russian inflects borrowed terms freely ("докера", "докером"),
    /// and matching a prefix costs nothing while matching every declension by hand is endless.
    /// </summary>
    private static readonly string[] BuiltInRussifiedForms =
    [
        "гитхаб", "гитлаб", "докер", "джемини", "джимини", "чатджипити", "джипити",
        "пайтон", "джаваскрипт", "тайпскрипт", "джейсон",
        "реакт", "линукс", "виндовс", "макос", "майкрософт", "гугл",
        "энвидиа", "юнити", "анриал", "телеграм",
        "дискорд", "ноушен", "спотифай", "реддит", "эндпоинт",
        "поверщелл", "пауэршелл", "постгрес", "коммит", "пулреквест",
        "деплой", "фреймворк", "бэкенд", "фронтенд", "рефактор"
    ];

    public MixedSpeechDecision Inspect(string primaryTranscript, bool mixedModeRequested)
    {
        if (mixedModeRequested)
        {
            return new MixedSpeechDecision(MixedSpeechTrigger.Requested, null);
        }

        if (string.IsNullOrWhiteSpace(primaryTranscript))
        {
            return MixedSpeechDecision.NotNeeded;
        }

        foreach (var character in primaryTranscript)
        {
            if (IsLatinLetter(character))
            {
                return new MixedSpeechDecision(MixedSpeechTrigger.LatinScript, character.ToString());
            }
        }

        foreach (var word in EnumerateWords(primaryTranscript))
        {
            foreach (var form in _russifiedForms)
            {
                // The remainder must be a plausible case ending, not an arbitrary continuation.
                // Without this bound "докер" also matched "докерский" — and, worse, short stems
                // matched unrelated words outright.
                if (word.Length >= form.Length &&
                    word.Length - form.Length <= MaximumEndingLength &&
                    word.StartsWith(form, StringComparison.Ordinal))
                {
                    return new MixedSpeechDecision(MixedSpeechTrigger.RussifiedTerm, word);
                }
            }
        }

        return MixedSpeechDecision.NotNeeded;
    }

    /// <summary>
    /// Turns dictionary entries into likely russifications so a user-added term starts triggering
    /// the fallback without anyone maintaining a second list by hand.
    /// </summary>
    public static IEnumerable<string> DeriveRussifiedForms(IEnumerable<string> spokenForms)
    {
        foreach (var spoken in spokenForms)
        {
            var normalized = NormalizeWord(spoken);
            if (normalized.Length >= MinimumFormLength && !ContainsLatin(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static bool IsLatinLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool ContainsLatin(string value)
    {
        foreach (var character in value)
        {
            if (IsLatinLetter(character))
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<string> EnumerateWords(string text)
    {
        var builder = new StringBuilder(24);
        foreach (var character in text)
        {
            if (char.IsLetter(character))
            {
                builder.Append(Fold(character));
                continue;
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private static string NormalizeWord(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetter(character))
            {
                builder.Append(Fold(character));
            }
        }
        return builder.ToString();
    }

    private static char Fold(char character)
    {
        var lowered = char.ToLowerInvariant(character);
        return lowered is 'ё' ? 'е' : lowered;
    }
}
