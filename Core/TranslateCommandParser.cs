using System.Text.RegularExpressions;

namespace Egoist.Voice.Core;

/// <summary>Распознанная голосовая команда перевода: что переводить и на какой язык.</summary>
public sealed record TranslateDirective(string Payload, string TargetLanguage);

/// <summary>
/// Голосовая команда «переведи»: в начале или в конце надиктованной фразы.
/// «Переведи на английский привет, как дела» / «Привет, как дела. Переведи.»
/// Язык по умолчанию — английский. Название языка уходит модели по-английски.
/// </summary>
/// <remarks>
/// <para>
/// Команда узнаётся во всех живых формах — «переведи», «переведите», «переводи», «перевести»,
/// «перевод», «перевёл», — потому что в диктовке человек не выбирает форму сознательно.
/// Между командой и текстом допускаются служебные слова в любом порядке и количестве:
/// «переведи вот это всё», «переведи всё, что я сказал», «переведи на испанский вот это».
/// </para>
/// <para>
/// Защита от ложных срабатываний ровно одна и намеренно узкая: «на &lt;слово&gt;» считается
/// указанием языка, только если слово есть в <see cref="TranslationLanguages"/>. «Переведи на
/// завтра встречу» командой не является и уйдёт в текст как есть. Голая форма «переведи …»
/// разрешена сознательно: так удобнее диктовать, а ценой становится то, что фраза, которая сама
/// начинается со слова «переведи», уйдёт в переводчик.
/// </para>
/// </remarks>
public static partial class TranslateCommandParser
{
    /// <summary>
    /// Формы глагола «перевести»/«переводить» и существительного «перевод». Перечислены явно, а не
    /// основой «перев\w*»: иначе командой становились бы «переводчик», «переводной» и «перевозка».
    /// </summary>
    private const string CommandWord =
        @"перев(?:" +
        @"ед(?:ите|и|[её]шь|[её]т[ье]?|[её]м|ут|ены|ено|ена|[её]н)" +
        @"|од(?:ите|ить|ишь|им|ят|ы|а|ом|е|у|и)?" +
        @"|[её]л(?:а|и|о)?" +
        @"|ести" +
        @")(?:\s*-?\s*ка)?";

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
        @"|это|этот|эту|эти|этого|всё|все|весь|всю|всего|вот|тут|здесь|текст[а-яё]*" +
        @"|сказанное|написанное|сообщение|фраз[а-яё]+|слов[а-яё]*|предложение|абзац" +
        @"|мне|нам|мой|моё|мою|пожалуйста|давай|-?ка" +
        @"|дальше|далее|ниже|выше|потом|затем|сюда|следующее|предыдущее|после|до" +
        @"|только|сейчас|быстро|срочно)";

    /// <summary>
    /// «на английский», «на английском языке», «на English», «по-английски». Слово захватывается
    /// как есть, а языком его признаёт словарь — на этом и держится защита от ложного срабатывания.
    /// Латиница разрешена: язык часто называют по-английски, и распознавание отдаёт его латиницей.
    /// </summary>
    private const string LangPart =
        @"(?:на\s+(?<lang>[a-zа-яё]+)(?:\s+язык[а-яё]*)?|(?<lang>по[-\s][a-zа-яё]+))";

    private const string Modifiers = $@"(?:[\s,]+(?:{TailWord}|{LangPart}))*";

    /// <remarks>
    /// «?» и «!» сюда не входят намеренно: в «Привет, как дела? Переведи.» вопросительный знак
    /// принадлежит тексту, и разделитель его бы съел.
    /// </remarks>
    private const string Separator = @"[\s,.:;—–\-]+";

    [GeneratedRegex(
        $@"^\s*{CommandWord}\b{Modifiers}{Separator}(?<rest>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PrefixPattern();

    [GeneratedRegex(
        $@"^(?<rest>.+?){Separator}{CommandWord}\b{Modifiers}[\s,.:;!?—–\-]*$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SuffixPattern();

    public static TranslateDirective? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return TryMatch(PrefixPattern(), text, trimTrailingFillers: false)
            ?? TryMatch(SuffixPattern(), text, trimTrailingFillers: true);
    }

    private static TranslateDirective? TryMatch(Regex pattern, string text, bool trimTrailingFillers)
    {
        var match = pattern.Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var language = "English";
        if (match.Groups["lang"].Success)
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

        var payload = match.Groups["rest"].Value.Trim();
        if (trimTrailingFillers)
        {
            payload = TrimTrailingFillers(payload);
        }

        return payload.Length < 2 ? null : new TranslateDirective(payload, language);
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
