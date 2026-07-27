namespace Egoist.Voice.Core;

/// <summary>
/// Названия языков, которые распознаются в голосовой команде перевода, и их приведение к
/// английскому имени — именно оно уходит в промпт переводчика.
///
/// Отдельный тип, а не словарь внутри парсера: список большой, растёт, и его формы нужно
/// проверять тестами независимо от грамматики самой команды.
/// </summary>
public static class TranslationLanguages
{
    /// <summary>
    /// Один язык: английское имя для промпта, основа русского прилагательного и произвольные
    /// дополнительные написания.
    /// </summary>
    /// <param name="Stems">
    /// Основы прилагательных без окончания через пробел: «немецк германск». Из каждой выводятся
    /// все падежные формы и наречие «по-немецки». Пусто, если прилагательного нет (хинди, урду).
    /// </param>
    private sealed record Entry(string Name, string Stems, params string[] Aliases);

    // Падежные окончания прилагательных на -ский. Диктовка приходит без согласования с падежом
    // предлога, потому что «на английский» и «на английском» одинаково естественны в речи.
    private static readonly string[] AdjectiveEndings =
        ["ий", "ого", "ому", "им", "ом", "ие", "их", "ими", "ая", "ой", "ую", "ое", "им"];

    private static readonly Entry[] Catalogue =
    [
        new("English", "английск", "англ", "инглиш", "english", "eng", "ингл"),
        new("Russian", "русск", "russian", "рашн"),
        new("German", "немецк германск", "german", "дойч", "джерман"),
        new("French", "французск", "french", "франсэ", "френч"),
        new("Spanish", "испанск", "spanish", "испаньол", "эспаньол", "español", "спаниш"),
        new("Italian", "итальянск", "italian", "итальяно"),
        new("Portuguese", "португальск", "portuguese", "португез"),
        new("Chinese", "китайск", "chinese", "мандарин", "путунхуа", "чайниз"),
        new("Japanese", "японск", "japanese", "ниппон", "джапаниз"),
        new("Korean", "корейск сеульск", "korean", "кориан"),
        new("Turkish", "турецк тюркск", "turkish"),
        new("Arabic", "арабск", "arabic", "арабик"),
        new("Polish", "польск", "polish"),
        new("Czech", "чешск", "czech"),
        new("Dutch", "нидерландск голландск", "dutch"),
        new("Ukrainian", "украинск", "ukrainian", "укр"),
        new("Belarusian", "белорусск беларусск", "belarusian"),
        new("Hindi", "", "хинди", "hindi"),
        new("Vietnamese", "вьетнамск", "vietnamese"),
        new("Thai", "тайск", "thai"),
        new("Hebrew", "ивритск", "иврит", "hebrew", "иврите"),
        new("Kazakh", "казахск", "kazakh"),
        new("Indonesian", "индонезийск", "indonesian"),
        new("Malay", "малайск", "malay"),
        new("Persian", "персидск", "фарси", "persian"),
        new("Urdu", "", "урду", "urdu"),
        new("Bengali", "бенгальск", "bengali"),
        new("Tamil", "тамильск", "tamil"),
        new("Mongolian", "монгольск", "mongolian"),
        new("Afrikaans", "африканск бурск", "африкаанс", "afrikaans"),
        new("Swedish", "шведск", "swedish"),
        new("Norwegian", "норвежск", "norwegian"),
        new("Danish", "датск", "danish"),
        new("Finnish", "финск", "finnish", "суоми"),
        new("Greek", "греческ", "greek"),
        new("Hungarian", "венгерск", "hungarian"),
        new("Romanian", "румынск", "romanian"),
        new("Bulgarian", "болгарск", "bulgarian"),
        new("Serbian", "сербск", "serbian"),
        new("Croatian", "хорватск", "croatian"),
        new("Slovak", "словацк", "slovak"),
        new("Slovenian", "словенск", "slovenian"),
        new("Lithuanian", "литовск", "lithuanian"),
        new("Latvian", "латышск латвийск", "latvian"),
        new("Estonian", "эстонск", "estonian"),
        new("Georgian", "грузинск", "georgian"),
        new("Armenian", "армянск", "armenian"),
        new("Azerbaijani", "азербайджанск", "azerbaijani", "азери"),
        new("Uzbek", "узбекск", "uzbek"),
        new("Kyrgyz", "киргизск кыргызск", "kyrgyz"),
        new("Tajik", "таджикск", "tajik"),
        new("Tatar", "татарск", "tatar"),
        new("Swahili", "", "суахили", "swahili"),
        new("Filipino", "филиппинск тагальск", "filipino", "тагалог"),
        new("Burmese", "бирманск мьянманск", "burmese"),
        new("Khmer", "кхмерск", "khmer"),
        new("Lao", "лаосск", "lao"),
        new("Nepali", "непальск", "nepali"),
        new("Sinhala", "сингальск", "sinhala"),
        new("Telugu", "", "телугу", "telugu"),
        new("Marathi", "", "маратхи", "marathi"),
        new("Gujarati", "гуджаратск", "gujarati", "гуджарати"),
        new("Punjabi", "пенджабск панджабск", "панджаби", "punjabi"),
        new("Catalan", "каталонск", "catalan"),
        new("Icelandic", "исландск", "icelandic"),
        new("Irish", "ирландск", "irish"),
        new("Welsh", "валлийск уэльск", "welsh"),
        new("Albanian", "албанск", "albanian"),
        new("Macedonian", "македонск", "macedonian"),
        new("Bosnian", "боснийск", "bosnian"),
        new("Maltese", "мальтийск", "maltese"),
        new("Basque", "баскск", "basque"),
        new("Latin", "латинск", "latin", "латынь"),
        new("Amharic", "амхарск", "amharic"),
        new("Somali", "сомалийск", "сомали", "somali"),
        new("Yoruba", "", "йоруба", "yoruba"),
        new("Hausa", "", "хауса", "hausa"),
        new("Zulu", "зулусск", "зулу", "zulu"),
        new("Pashto", "пуштунск", "пушту", "pashto"),
        new("Kurdish", "курдск", "kurdish"),
    ];

    private static readonly Dictionary<string, string> Lookup = Build();

    /// <summary>Все поддерживаемые английские имена — для настроек и документации.</summary>
    public static IReadOnlyList<string> Names { get; } = Catalogue.Select(entry => entry.Name).ToArray();

    /// <summary>Число распознаваемых написаний. Существует ради теста, а не ради вызывающих.</summary>
    public static int SpellingCount => Lookup.Count;

    private static Dictionary<string, string> Build()
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string spelling, string name)
        {
            var key = Normalize(spelling);
            if (key.Length > 1)
            {
                // Первое написание выигрывает: пересечения вроде «германск» разрешаются в пользу
                // языка, объявленного раньше, а не последним прочитанным.
                lookup.TryAdd(key, name);
            }
        }

        foreach (var entry in Catalogue)
        {
            Add(entry.Name, entry.Name);

            foreach (var alias in entry.Aliases)
            {
                Add(alias, entry.Name);
            }

            foreach (var stem in entry.Stems.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Add(stem, entry.Name);
                foreach (var ending in AdjectiveEndings)
                {
                    Add(stem + ending, entry.Name);
                }

                // Наречие: «по-английски». Предлог «по-» снимается до обращения сюда, поэтому
                // достаточно формы «английски».
                Add(stem + "и", entry.Name);
            }
        }

        return lookup;
    }

    /// <summary>
    /// Приводит услышанное название к английскому имени. <c>null</c> — это не язык, и значит
    /// «на …» в команде было не указанием языка, а частью текста.
    /// </summary>
    public static string? Resolve(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        var key = Normalize(word);
        if (Lookup.TryGetValue(key, out var direct))
        {
            return direct;
        }

        // «по-английски» приходит как одно слово вместе с предлогом.
        if (key.StartsWith("по-", StringComparison.Ordinal) && Lookup.TryGetValue(key[3..], out var adverb))
        {
            return adverb;
        }

        return null;
    }

    private static string Normalize(string word) =>
        word.Trim().ToLowerInvariant().Replace('ё', 'е').Replace(' ', '-');
}
