using System.Text;
using System.Text.RegularExpressions;

namespace Egoist.Voice.Core;

public static partial class TranscriptNormalizer
{
    private static readonly HashSet<string> NonAdversativeBeforeA = new(StringComparer.OrdinalIgnoreCase)
    {
        "план", "плана", "плане", "плану", "пункт", "пункта", "пункте", "пункту",
        "буква", "буквы", "букве", "букву", "буквой",
        "витамин", "витамина", "витамину", "витамином",
        "класс", "класса", "классе", "классу",
        "сектор", "сектора", "секторе", "сектору",
        "группа", "группы", "группе", "группу",
        "блок", "блока", "блоке", "блоку",
        "модель", "модели", "моделью",
        "вариант", "варианта", "варианте", "варианту",
        "раздел", "раздела", "разделе", "разделу",
        "категория", "категории", "категорию", "типа"
    };

    private static readonly string[] QuestionStarters =
    [
        "are you sure",
        "ты уверен", "вы уверены", "ты готов", "вы готовы", "ты согласен", "вы согласны",
        "ты можешь", "вы можете", "ты сможешь", "вы сможете", "ты хочешь", "вы хотите", "ты будешь", "вы будете",
        "как дела", "как поживаешь", "как ты", "как вы", "как настроение", "как успехи",
        "как думаешь", "как считаешь", "как это работает", "как это сделать", "как пройти", "как найти", "как понять", "как получить",
        "что думаешь", "что скажешь", "что случилось", "что произошло", "что нового", "что это", "что за", "что делать", "что происходит",
        "почему", "зачем", "откуда", "куда", "где", "сколько",
        "можно ли", "правда ли", "неужели", "разве",
        "кто это", "кто там", "кто такой", "кто такая", "кто такие"
    ];

    public static string Normalize(string? input) => Normalize(input, preserveFinalTerminalPunctuation: false);

    internal static string Normalize(string? input, bool preserveFinalTerminalPunctuation)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var paragraphs = ParagraphBreakRegex()
            .Split(input.Trim())
            .Select(paragraph => paragraph.Trim())
            .Where(paragraph => paragraph.Length > 0)
            .ToArray();

        for (var index = 0; index < paragraphs.Length; index++)
        {
            paragraphs[index] = NormalizeParagraph(
                paragraphs[index],
                preserveFinalTerminalPunctuation && index == paragraphs.Length - 1);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
    }

    /// <summary>
    /// Single line breaks survive normalization. They used to be collapsed into spaces along with
    /// every other whitespace run, which silently undid the "new line" voice command.
    /// </summary>
    private static string NormalizeParagraph(string input, bool preserveFinalTerminalPunctuation)
    {
        var lines = LineBreakRegex()
            .Split(input.Trim())
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (lines.Length == 0)
        {
            return string.Empty;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = NormalizeLine(
                lines[index],
                preserveFinalTerminalPunctuation && index == lines.Length - 1);
        }

        lines[0] = Capitalize(lines[0]);
        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeLine(string input, bool preserveFinalTerminalPunctuation)
    {
        var text = WhitespaceRegex().Replace(input.Trim(), " ");
        text = SpaceBeforePunctuationRegex().Replace(text, "$1");
        text = SpaceAfterOpeningBracketRegex().Replace(text, "$1");
        text = SpaceBeforeClosingBracketRegex().Replace(text, "$1");

        text = NormalizeCommonOrthography(text);
        text = NormalizeRussianHyphens(text);
        text = NormalizeRussianPunctuation(text);
        text = NormalizeSentenceQuestions(text, preserveFinalTerminalPunctuation);

        return CapitalizeSentenceStarts(text);
    }

    private static string NormalizeCommonOrthography(string text)
    {
        // в общем / вообщем
        text = Regex.Replace(text, @"\bвообщем\b", match => PreserveCase(match.Value, "в общем"), RegexOptions.IgnoreCase);
        // вкратце / в крации / вкрации
        text = Regex.Replace(text, @"\b(?:в\s*крации|вкрации)\b", match => PreserveCase(match.Value, "вкратце"), RegexOptions.IgnoreCase);
        // как будто (не через дефис)
        text = Regex.Replace(text, @"\bкак-будто\b", match => PreserveCase(match.Value, "как будто"), RegexOptions.IgnoreCase);
        // приставки з-/с-
        text = Regex.Replace(text, @"\bз(делать|делаю|делает|делаем|делаете|делают|делал|делала|делали|делано)\b", match =>
            PreserveCase(match.Value, "с" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bс(десь|дешний|дешняя|дешнее|дешние)\b", match =>
            PreserveCase(match.Value, "з" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bз(зади)\b", match =>
            PreserveCase(match.Value, "с" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        // частые акустические искажения и опечатки
        text = Regex.Replace(text, @"\bпаралельн(о|ый|ая|ое|ые|ых|ым|ыми)\b", match =>
            PreserveCase(match.Value, "параллельн" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bраспознован(ие|ия|ием|ии|ию)\b", match =>
            PreserveCase(match.Value, "распознаван" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bграммотн(о|ый|ая|ое|ые|ых|ым|ыми)\b", match =>
            PreserveCase(match.Value, "грамотн" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bагенств(о|а|у|ом|е)\b", match =>
            PreserveCase(match.Value, "агентств" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bсимпотичн(о|ый|ая|ое|ые|ых|ым|ыми)\b", match =>
            PreserveCase(match.Value, "симпатичн" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bкоординальн(о|ый|ая|ое|ые|ых|ым|ыми)\b", match =>
            PreserveCase(match.Value, "кардинальн" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bбудующ(ий|ая|ее|ие|их|им|ими|ем|ую)\b", match =>
            PreserveCase(match.Value, "будущ" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bинциндент(а|у|ом|е|ы|ов|ам|ами|ах)?\b", match =>
            PreserveCase(match.Value, "инцидент" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bпрецендент(а|у|ом|е|ы|ов|ам|ами|ах)?\b", match =>
            PreserveCase(match.Value, "прецедент" + match.Groups[1].Value), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(?:скрин\s+шот|скриптшот)\b", match =>
            PreserveCase(match.Value, "скриншот"), RegexOptions.IgnoreCase);

        return text;
    }

    private static string NormalizeRussianHyphens(string text)
    {
        // из-за, из-под
        text = Regex.Replace(text, @"\b(из)\s+(за|под)\b", match =>
            PreserveCase(match.Value, $"{match.Groups[1].Value}-{match.Groups[2].Value}"),
            RegexOptions.IgnoreCase);

        // всё-таки / все-таки
        text = Regex.Replace(text, @"\b(вс[её])\s+(таки)\b", match =>
            PreserveCase(match.Value, "всё-таки"),
            RegexOptions.IgnoreCase);

        // по-русски, по-английски, по-немецки, по-французски
        text = Regex.Replace(text, @"\b(по)\s+(русски|английски|немецки|французски)\b", match =>
            PreserveCase(match.Value, $"{match.Groups[1].Value}-{match.Groups[2].Value}"),
            RegexOptions.IgnoreCase);

        // Формы на -ому/-ему бывают наречиями («сделай по-новому») и определениями
        // («по новому адресу»). Не склеиваем их перед ограниченным набором частых существительных.
        const string dativeNoun = @"(?:мнению|желанию|плану|совету|указанию|слову|адресу|месту|пути|правилу|проекту|договору|дому|номеру|телефону|сценарию|маршруту|вопросу|каналу|способу|методу|подходу|заданию|расписанию|формату)";
        text = Regex.Replace(text,
            $@"\b(по)\s+(прежнему|видимому|моему|нашему|твоему|быстрому|новому|старому|настоящему|хорошему|простому)\b(?!\s+{dativeNoun}\b)",
            match => PreserveCase(match.Value, $"{match.Groups[1].Value}-{match.Groups[2].Value}"),
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(по)\s+(человечески)\b", match =>
            PreserveCase(match.Value, $"{match.Groups[1].Value}-{match.Groups[2].Value}"),
            RegexOptions.IgnoreCase);

        // по-другому (когда не следует слову пути, поводу, каналу, адресу, маршруту, сценарию, вопросу)
        text = Regex.Replace(text, @"\b(по)\s+(другому)\b(?!\s+(?:пути|поводу|каналу|адресу|маршруту|сценарию|вопросу)\b)", match =>
            PreserveCase(match.Value, "по-другому"),
            RegexOptions.IgnoreCase);

        // Парные и устойчивые наречия через дефис: чуть-чуть, давным-давно, мало-помалу, точь-в-точь, как-никак, де-факто, де-юре
        text = Regex.Replace(text, @"\b(чуть)\s+(чуть)\b", match =>
            PreserveCase(match.Value, "чуть-чуть"), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(давным)\s+(давно)\b", match =>
            PreserveCase(match.Value, "давным-давно"), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(мало)\s+(помалу)\b", match =>
            PreserveCase(match.Value, "мало-помалу"), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(точь)\s+(в)\s+(точь)\b", match =>
            PreserveCase(match.Value, "точь-в-точь"), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(как)\s+(никак)\b", match =>
            PreserveCase(match.Value, "как-никак"), RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(де)\s+(факто|юре)\b", match =>
            PreserveCase(match.Value, $"{match.Groups[1].Value}-{match.Groups[2].Value}"), RegexOptions.IgnoreCase);

        // Постфикс -то
        text = Regex.Replace(text, @"\b(как|что|где|куда|когда|кто|какой|какая|какое|какие|каком|каких|каким|какими|какую|почему|зачем|откуда|так)\s+(то)\b", match =>
            $"{match.Groups[1].Value}-то",
            RegexOptions.IgnoreCase);

        // Постфикс -нибудь
        text = Regex.Replace(text, @"\b(кто|что|как|где|куда|когда|какой|какая|какое|какие|каком|каких|каким|какими|какую|почему)\s+(нибудь)\b", match =>
            $"{match.Groups[1].Value}-нибудь",
            RegexOptions.IgnoreCase);

        // Постфикс -либо
        text = Regex.Replace(text, @"\b(кто|что|как|где|куда|когда|какой|какая|какое|какие|каком|каких|каким|какими|какую)\s+(либо)\b", match =>
            $"{match.Groups[1].Value}-либо",
            RegexOptions.IgnoreCase);

        // Префикс кое-
        text = Regex.Replace(text, @"\b(кое)\s+(как|кто|что|где|куда)\b", match =>
            $"{match.Groups[1].Value}-{match.Groups[2].Value}",
            RegexOptions.IgnoreCase);

        return text;
    }

    private static string NormalizeRussianPunctuation(string text)
    {
        // 1. Противительный союз «но» — в русском языке перед ним всегда ставится запятая
        text = Regex.Replace(text, @"(?<=[\p{L}\p{N}])\s+но\b", ", но");

        // 2. Противительный союз «а» между словами (исключая обозначения вроде «план А», «витамин А»)
        text = Regex.Replace(text, @"(?<=[\p{L}\p{N}])\s+а\s+(?=[\p{L}\p{N}])", match =>
        {
            var index = match.Index;
            var wordStart = index - 1;
            while (wordStart >= 0 && char.IsLetterOrDigit(text[wordStart]))
            {
                wordStart--;
            }
            var prevWord = text[(wordStart + 1)..index];
            if (NonAdversativeBeforeA.Contains(prevWord))
            {
                return match.Value;
            }
            return ", а ";
        });

        // 3. Подчинительные союзы: потому что, так как, то есть, хотя, чтобы, если
        text = Regex.Replace(text, @"(?<=[\p{L}\p{N}])\s+потому что\b", ", потому что");
        text = Regex.Replace(text, @"(?<=[\p{L}\p{N}])\s+так как\b", ", так как");
        text = Regex.Replace(text, @"(?<=[\p{L}\p{N}])\s+то есть\b", ", то есть");
        text = Regex.Replace(text, @"(?<=[\p{L}\p{N}])\s+хотя\b(?!\s+б(?:ы)?\b)", ", хотя");
        text = Regex.Replace(text, @"(?<=[\p{L}\p{N}])\s+чтобы\b", ", чтобы");
        text = Regex.Replace(text, @"(?<=[\p{L}\p{N}])\s+если\b", ", если");

        // 4. Придаточные изъяснительные с союзом «что» после глаголов речи и мысли
        text = Regex.Replace(text,
            @"(?<=\b(?:знаю|знает|знаем|знаете|знают|думаю|думает|думаем|думаете|думают|сказал|сказала|сказали|говорю|говорит|вижу|видит|видим|слышу|понял|поняла|поняли|кажется|оказалось|помню|помнит|считаю|уверен|уверена|уверены|рад|рада|рады|надеюсь|боюсь|жаль|ясно|понятно|очевидно))\s+что\b",
            ", что");

        return text;
    }

    private static string NormalizeSentenceQuestions(string text, bool preserveFinalTerminalPunctuation)
    {
        if (text.Length < 3)
        {
            return text;
        }

        var parts = SentenceBoundaryRegex().Split(text);
        if (parts.Length == 1)
        {
            return ProcessQuestionSegment(parts[0], preserveFinalTerminalPunctuation);
        }

        var results = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            results[i] = ProcessQuestionSegment(
                parts[i],
                preserveFinalTerminalPunctuation && i == parts.Length - 1);
        }

        return string.Join(" ", results);
    }

    private static string ProcessQuestionSegment(string segment, bool preserveTerminalPunctuation)
    {
        var trimmed = segment.Trim();
        if (trimmed.Length == 0)
        {
            return segment;
        }

        if (preserveTerminalPunctuation)
        {
            return segment;
        }

        if (IsQuestionSentence(trimmed))
        {
            if (trimmed.EndsWith('!') || trimmed.EndsWith('?'))
            {
                return segment;
            }

            if (trimmed.EndsWith('.'))
            {
                var withoutDot = trimmed[..^1];
                return segment.Replace(trimmed, withoutDot + "?");
            }

            return segment + "?";
        }

        return segment;
    }

    private static bool IsQuestionSentence(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return false;
        }

        foreach (var starter in QuestionStarters)
        {
            if (sentence.StartsWith(starter, StringComparison.OrdinalIgnoreCase))
            {
                if (sentence.Length == starter.Length)
                {
                    return true;
                }

                var nextChar = sentence[starter.Length];
                // Hyphenated forms like «где-то», «почему-то», «как-то», «что-то» are indefinite pronouns/adverbs, not questions.
                if (nextChar != '-' && (char.IsWhiteSpace(nextChar) || nextChar is ',' or ':' or ';'))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string PreserveCase(string original, string replacement) =>
        original.Length > 0 && char.IsUpper(original[0])
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement.ToLowerInvariant();

    private static string CapitalizeSentenceStarts(string text)
    {
        var builder = new StringBuilder(text.Length);
        var capitalizeNextLetter = false;
        var sawBoundaryWhitespace = false;
        foreach (var character in text)
        {
            if (capitalizeNextLetter && char.IsWhiteSpace(character))
            {
                builder.Append(character);
                sawBoundaryWhitespace = true;
                continue;
            }

            if (capitalizeNextLetter && sawBoundaryWhitespace && char.IsLetter(character))
            {
                builder.Append(char.ToUpperInvariant(character));
                capitalizeNextLetter = false;
                sawBoundaryWhitespace = false;
                continue;
            }

            builder.Append(character);
            if (character is '.' or '!' or '?')
            {
                capitalizeNextLetter = true;
                sawBoundaryWhitespace = false;
            }
            else if (capitalizeNextLetter && character is not '«' and not '(' and not '[')
            {
                capitalizeNextLetter = false;
                sawBoundaryWhitespace = false;
            }
        }
        return builder.ToString();
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

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBoundaryRegex();
}

