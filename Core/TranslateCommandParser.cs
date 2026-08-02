using System.Text.RegularExpressions;

namespace Egoist.Voice.Core;

public enum TranslateCommandPosition
{
    Prefix,
    Suffix
}

public enum TranslateCommandMatchClass
{
    DefaultTarget,
    ExplicitTarget
}

/// <summary>Направление локального перевода. Источник пока определяется моделью автоматически.</summary>
public sealed record TranslationDirection(string SourceLanguage, string TargetLanguage)
{
    public static TranslationDirection AutoTo(string targetLanguage) => new("Auto", targetLanguage);
}

/// <summary>Типизированное намерение перевода без потери позиции и класса совпадения.</summary>
public sealed record TranslateDirective(
    string Payload,
    TranslationDirection Direction,
    TranslateCommandPosition Position,
    TranslateCommandMatchClass MatchClass)
{
    /// <summary>Совместимый короткий доступ для существующего MT-клиента.</summary>
    public string TargetLanguage => Direction.TargetLanguage;
}

/// <summary>
/// Голосовая команда «переведи»: в начале или в конце надиктованной фразы.
/// «Переведи на английский привет, как дела» / «Привет, как дела. Переведи.»
/// Язык по умолчанию — английский. Название языка уходит модели по-английски.
/// </summary>
/// <remarks>
/// <para>
/// Команда узнаётся только в повелительных формах — «переведи», «переведите», «переводи»,
/// «переводите». Существительное «перевод», прошедшее время «перевёл», будущее
/// «переведёшь» и инфинитив «перевести» остаются обычной диктовкой: они часто встречаются
/// в предложениях и не выражают однозначного намерения запустить перевод.
/// Между командой и текстом допускаются служебные слова в любом порядке и количестве:
/// «переведи вот это всё», «переведи всё, что я сказал», «переведи на испанский вот это».
/// </para>
/// <para>
/// «На &lt;слово&gt;» считается указанием языка, только если слово есть в
/// <see cref="TranslationLanguages"/>. Чтобы «переведи стрелки/деньги/курсор» не становились
/// командами, префикс без названного языка требует явной границы («переведи: …»), а суффикс —
/// пунктуационной границы завершённой фразы («…; переведи»). Упоминания команды остаются текстом.
/// </para>
/// </remarks>
public static partial class TranslateCommandParser
{
    /// <summary>
    /// Только повелительные формы глаголов «перевести»/«переводить». Более широкая морфология
    /// детерминированно перехватывала обычные фразы: «Перевод денег задержался», «Я закончил
    /// перевод», «Перевёл документ вчера».
    /// </summary>
    private const string CommandWord =
        @"перев(?:еди(?:те)?|оди(?:те)?)(?:\s*-?\s*ка)?";

    /// <summary>
    /// Служебные слова между командой и текстом. Смысла они не несут, но в речи звучат всегда:
    /// «переведи-ка вот это всё», «переведи мне, пожалуйста, то, что я сказал».
    /// </summary>
    /// <remarks>
    /// «что я сказал» перечислено целой связкой, а не по словам. Отдельным служебным словом «я»
    /// быть не может: фраза «переведи это я тебя жду» потеряла бы первое слово текста, а потеря
    /// содержимого хуже несработавшей команды.
    /// </remarks>
    private const string TailWord =
        @"(?:" +
        @"что\s+(?:я|мы)\s+(?:скажу|скажем|сказал[а]?|сказали|говорю|говорил[а]?|надиктовал[а]?|надиктую)" +
        @"|то|это|этот|эту|эти|этого|его|её|ее|их|всё|все|весь|всю|всего|вот|тут|здесь|текст[а-яё]*" +
        @"|сказанное|написанное|сообщение|фраз[а-яё]+|слов[а-яё]*|предложение|абзац" +
        @"|мне|нам|мой|моё|мою|пожалуйста|давай|-?ка" +
        @"|дальше|далее|ниже|выше|потом|затем|сюда|следующ[а-яё]*|предыдущ[а-яё]*|после|до" +
        @"|только|сейчас|быстро|срочно)";

    /// <summary>
    /// «на английский», «на английском языке», «на English», «по-английски». Слово захватывается
    /// как есть, а языком его признаёт словарь — на этом и держится защита от ложного срабатывания.
    /// Латиница разрешена: язык часто называют по-английски, и распознавание отдаёт его латиницей.
    /// </summary>
    private const string LangPart =
        @"(?<langPart>(?:на\s+(?<lang>[a-zа-яё]+)(?:\s+язык[а-яё]*)?|(?<lang>по[-\s][a-zа-яё]+)))";

    private const string Modifiers = $@"(?:[\s,]+(?:{TailWord}|{LangPart}))*";

    /// <remarks>
    /// «?» и «!» сюда не входят намеренно: в «Привет, как дела? Переведи.» вопросительный знак
    /// принадлежит тексту, и разделитель его бы съел.
    /// </remarks>
    private const string Separator = @"[\s,.:;—–\-]+";

    [GeneratedRegex(
        $@"^\s*(?<command>{CommandWord})\b{Modifiers}(?<separator>{Separator})(?<rest>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PrefixPattern();

    [GeneratedRegex(
        $@"^(?<rest>.+?)(?<separator>{Separator})(?<command>{CommandWord})\b{Modifiers}[\s,.:;!?—–\-]*$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SuffixPattern();

    public static TranslateDirective? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Trim();
        return TryMatch(PrefixPattern(), normalized, TranslateCommandPosition.Prefix)
            ?? TryMatch(SuffixPattern(), normalized, TranslateCommandPosition.Suffix);
    }

    private static TranslateDirective? TryMatch(
        Regex pattern,
        string text,
        TranslateCommandPosition position)
    {
        var match = pattern.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var language = "English";
        var hasExplicitTarget = match.Groups["lang"].Success;
        if (hasExplicitTarget)
        {
            // «на завтра», «по дороге» — это не язык, значит и не команда со сменой языка. Матч
            // отклоняется целиком: лучше вставить текст как есть, чем перевести не то и не туда.
            var resolved = TranslationLanguages.Resolve(match.Groups["lang"].Value);
            if (resolved is null)
            {
                return null;
            }

            language = resolved;
        }

        var separator = match.Groups["separator"].Value;
        if (position == TranslateCommandPosition.Prefix &&
            !hasExplicitTarget &&
            !ContainsStrongBoundary(separator))
        {
            return null;
        }

        var rawPayload = match.Groups["rest"].Value.Trim();
        if (position == TranslateCommandPosition.Suffix &&
            !ContainsStrongBoundary(separator) &&
            !EndsWithSentenceBoundary(rawPayload) &&
            !HasTrailingFillerBoundary(rawPayload))
        {
            return null;
        }

        // With an explicit language and no colon/dash, everything after the language is payload.
        // The old greedy modifier regex silently deleted nouns in phrases such as
        // «переведи на английский это сообщение для команды».
        var payload = position == TranslateCommandPosition.Prefix &&
            hasExplicitTarget &&
            !ContainsStrongBoundary(separator)
                ? PayloadAfterLanguage(text, match)
                : rawPayload;

        if (position == TranslateCommandPosition.Suffix)
        {
            payload = TrimTrailingFillers(payload);
            payload = RestoreTerminalBoundary(payload, separator);
        }

        if (payload.Length < 2 ||
            LooksLikeCommandMention(payload) ||
            (position == TranslateCommandPosition.Suffix && LooksLikeCommandIntroduction(payload)))
        {
            return null;
        }

        return new TranslateDirective(
            payload,
            TranslationDirection.AutoTo(language),
            position,
            hasExplicitTarget
                ? TranslateCommandMatchClass.ExplicitTarget
                : TranslateCommandMatchClass.DefaultTarget);
    }

    private static string PayloadAfterLanguage(string text, Match match)
    {
        var captures = match.Groups["langPart"].Captures;
        if (captures.Count == 0)
        {
            return match.Groups["rest"].Value.Trim();
        }

        var language = captures[^1];
        return text[language.Index..]
            .Remove(0, language.Length)
            .TrimStart(' ', ',', '.', ':', ';', '—', '–', '-');
    }

    private static bool ContainsStrongBoundary(string value) =>
        value.IndexOfAny([',', '.', ':', ';', '!', '?', '—', '–']) >= 0;

    private static bool EndsWithSentenceBoundary(string value)
    {
        var trimmed = value.TrimEnd();
        return trimmed.Length > 0 && trimmed[^1] is '.' or '!' or '?' or '…';
    }

    private static bool HasTrailingFillerBoundary(string payload)
    {
        var text = payload.TrimEnd();
        foreach (var filler in TrailingFillers)
        {
            if (text.Length <= filler.Length ||
                !text.EndsWith(filler, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var prefix = text[..^filler.Length].TrimEnd();
            return prefix.Length > 0 && ContainsStrongBoundary(prefix[^1].ToString());
        }

        return false;
    }

    private static string RestoreTerminalBoundary(string payload, string separator)
    {
        if (EndsWithSentenceBoundary(payload))
        {
            return payload;
        }

        return separator.IndexOf('.') >= 0 ? payload + "." : payload;
    }

    private static readonly string[] MentionPayloadPrefixes =
    [
        "название ",
        "пример ",
        "надпись ",
        "пункт ",
        "подпись ",
        "строка из ",
        "текст теста",
        "часть обычного текста"
    ];

    private static bool LooksLikeCommandMention(string payload)
    {
        var normalized = payload
            .TrimStart(' ', '«', '“', '"', '—', '–', '-')
            .Replace('ё', 'е')
            .ToLowerInvariant();

        return MentionPayloadPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static readonly string[] MentionIntroductionEndings =
    [
        " пример",
        " примером",
        " команда",
        " команду",
        " слово",
        " слова",
        " звучит так"
    ];

    private static bool LooksLikeCommandIntroduction(string payload)
    {
        var normalized = payload
            .TrimEnd(' ', ',', '.', ':', ';', '!', '?', '—', '–', '-')
            .Replace('ё', 'е')
            .ToLowerInvariant();

        return MentionIntroductionEndings.Any(ending => normalized.EndsWith(ending, StringComparison.Ordinal));
    }

    // Связки, которыми в живой речи подводят к команде: «…, соответственно переведи…».
    // В сам перевод они попадать не должны.
    private static readonly string[] TrailingFillers =
        ["соответственно", "пожалуйста", "давай", "итак", "короче", "теперь", "затем", "потом", "ну", "и", "а"];

    private static string TrimTrailingFillers(string payload)
    {
        var text = payload;
        var changed = true;

        while (changed)
        {
            changed = false;
            text = text.TrimEnd(' ', ',', ';', ':', '—', '-');

            foreach (var filler in TrailingFillers)
            {
                if (text.Length <= filler.Length + 1 ||
                    !text.EndsWith(filler, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var boundary = text[text.Length - filler.Length - 1];
                if (char.IsWhiteSpace(boundary) || boundary is ',' or ';')
                {
                    text = text[..^filler.Length];
                    changed = true;
                    break;
                }
            }
        }

        return text.TrimEnd(' ', ',', ';', ':', '—', '-');
    }
}
