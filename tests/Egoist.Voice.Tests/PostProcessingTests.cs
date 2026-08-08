using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class PostProcessingTests
{
    // ── Словарь ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dictionary_replaces_a_spoken_form_with_its_written_form()
    {
        var dictionary = UserDictionary.FromTerms([new DictionaryTerm(["питон", "пайтон"], "Python")]);

        Assert.Equal("Открой Python и запусти скрипт.", dictionary.Apply("Открой питон и запусти скрипт."));
        Assert.Equal("Открой Python.", dictionary.Apply("Открой пайтон."));
    }

    [Fact]
    public void Dictionary_tolerates_russian_case_endings()
    {
        // A dictionary that only works in the nominative is a dictionary that works in one sentence
        // out of five.
        var dictionary = UserDictionary.FromTerms([new DictionaryTerm(["докер"], "Docker")]);

        Assert.Equal("Разверни в Docker на выходных.", dictionary.Apply("Разверни в докере на выходных."));
        Assert.Equal("Собери Docker.", dictionary.Apply("Собери докером."));
    }

    [Fact]
    public void Dictionary_does_not_swallow_a_longer_unrelated_word()
    {
        var dictionary = UserDictionary.FromTerms([new DictionaryTerm(["код"], "code")]);

        Assert.Equal("Проверь кодировку файла.", dictionary.Apply("Проверь кодировку файла."));
    }

    [Fact]
    public void Longer_terms_win_over_the_prefixes_they_contain()
    {
        var dictionary = UserDictionary.FromTerms([
            new DictionaryTerm(["код"], "code"),
            new DictionaryTerm(["вижуал студио код"], "Visual Studio Code")]);

        Assert.Equal("Открой Visual Studio Code.", dictionary.Apply("Открой вижуал студио код."));
    }

    [Fact]
    public void Dictionary_supports_multi_word_forms_with_flexible_separators()
    {
        var dictionary = UserDictionary.FromTerms([new DictionaryTerm(["пул реквест"], "pull request")]);

        Assert.Equal("Создай pull request.", dictionary.Apply("Создай пул реквест."));
        Assert.Equal("Создай pull request.", dictionary.Apply("Создай пул-реквест."));
    }

    [Fact]
    public void Dictionary_supports_regular_expressions()
    {
        var dictionary = UserDictionary.FromTerms([
            new DictionaryTerm(null, "Kubernetes", @"(?i)\bкубер(нетес)?\w{0,3}\b")]);

        Assert.Equal("Разверни Kubernetes.", dictionary.Apply("Разверни кубернетес."));
        Assert.Equal("Разверни Kubernetes.", dictionary.Apply("Разверни кубер."));
    }

    [Fact]
    public void A_malformed_pattern_does_not_disable_the_rest_of_the_dictionary()
    {
        var dictionary = UserDictionary.FromTerms([
            new DictionaryTerm(null, "X", "([unclosed"),
            new DictionaryTerm(["питон"], "Python")]);

        Assert.Equal(1, dictionary.Count);
        Assert.Equal("Python", dictionary.Apply("питон"));
    }

    [Fact]
    public void Dictionary_parses_its_file_format()
    {
        var dictionary = UserDictionary.Parse(
            """
            {
              "terms": [
                { "spoken": ["гитхаб"], "written": "GitHub" },
                { "spoken": ["моя подпись"], "written": "С уважением, Родион" }
              ]
            }
            """);

        // Больше двух: пользовательские правила ложатся поверх встроенного словаря, а не заменяют
        // его — иначе добавление одного термина стирало бы знание обо всех остальных.
        Assert.True(dictionary.Count > 2);
        Assert.Equal("Открой GitHub.", dictionary.Apply("Открой гитхаб."));
        Assert.Equal("С уважением, Родион", dictionary.Apply("моя подпись"));
    }

    // ── Числа ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("двадцать пять", "25")]
    [InlineData("сто двадцать три", "123")]
    [InlineData("две тысячи двадцать шесть", "2026")]
    [InlineData("пятнадцать", "15")]
    [InlineData("сорок", "40")]
    [InlineData("три миллиона", "3000000")]
    public void Numbers_become_digits(string spoken, string expected) =>
        Assert.Equal(expected, RussianNumberNormalizer.Normalize(spoken));

    [Fact]
    public void Numbers_keep_their_surrounding_text()
    {
        Assert.Equal(
            "Встреча начнётся через 25 минут.",
            RussianNumberNormalizer.Normalize("Встреча начнётся через двадцать пять минут."));
    }

    [Theory]
    [InlineData("один из них справится")]
    [InlineData("это одна из причин")]
    public void A_standalone_one_stays_a_word(string text) =>
        Assert.Equal(text, RussianNumberNormalizer.Normalize(text));

    [Fact]
    public void A_compound_number_containing_one_is_still_converted() =>
        Assert.Equal("21", RussianNumberNormalizer.Normalize("двадцать один"));

    [Fact]
    public void Percent_becomes_a_sign() =>
        Assert.Equal("Рост на 5 %.", RussianNumberNormalizer.Normalize("Рост на пять процентов."));

    [Fact]
    public void An_ordinal_day_before_a_month_becomes_a_digit() =>
        Assert.Equal("Встреча 5 июля.", RussianNumberNormalizer.Normalize("Встреча пятое июля."));

    [Fact]
    public void An_ordinal_without_a_month_is_left_alone() =>
        Assert.Equal("Пятое место.", RussianNumberNormalizer.Normalize("Пятое место."));

    [Fact]
    public void Text_without_numerals_is_returned_unchanged()
    {
        const string text = "Совершенно обычная фраза без чисел.";
        Assert.Equal(text, RussianNumberNormalizer.Normalize(text));
    }

    // ── Голосовые команды ────────────────────────────────────────────────────

    [Fact]
    public void A_command_occupying_its_own_segment_is_executed()
    {
        var processor = new VoiceCommandProcessor();

        Assert.Equal(
            $"Первая строка{Environment.NewLine}вторая строка",
            Normalized(processor.Apply("Первая строка. Новая строка. вторая строка")));
    }

    [Fact]
    public void The_same_words_inside_a_sentence_are_left_alone()
    {
        // This is the whole reason commands are matched per segment: "точка входа" is ordinary
        // speech, not a request for a full stop.
        var processor = new VoiceCommandProcessor();
        const string text = "Проверь точку входа и новую строку кода.";

        Assert.Equal(text, processor.Apply(text));
    }

    [Fact]
    public void A_punctuation_command_replaces_the_delimiter_instead_of_stacking_on_it()
    {
        var processor = new VoiceCommandProcessor();

        Assert.Equal("Привет, мир", processor.Apply("Привет, запятая, мир"));
    }

    [Fact]
    public void Bracket_commands_produce_balanced_output()
    {
        var processor = new VoiceCommandProcessor();

        Assert.Equal(
            "Смотри (важно) дальше",
            processor.Apply("Смотри. Открыть скобку. важно. Закрыть скобку. дальше"));
    }

    [Fact]
    public void Inline_punctuation_command_gets_spacing_and_sentence_casing()
    {
        var processor = new TranscriptPostProcessor(UserDictionary.BuiltIn);

        Assert.Equal(
            "Исправь. И пробела нет.",
            processor.Process("исправь точка и пробела нет."));
    }

    [Fact]
    public void Point_as_an_ordinary_noun_is_not_treated_as_punctuation()
    {
        var processor = new VoiceCommandProcessor();
        const string text = "Проверь точка входа и точка доступа.";

        Assert.Equal(text, processor.Apply(text));
    }

    [Theory]
    [InlineData("открой вью джиэс", "Открой Vue.js")]
    [InlineData("перейди на example.com", "Перейди на example.com")]
    [InlineData("сохрани config.json", "Сохрани config.json")]
    public void Sentence_casing_does_not_corrupt_dotted_identifiers(string spoken, string expected)
    {
        var processor = new TranscriptPostProcessor(UserDictionary.BuiltIn);

        Assert.Equal(expected, processor.Process(spoken));
    }

    // ── Конвейер целиком ─────────────────────────────────────────────────────

    [Fact]
    public void Pipeline_applies_dictionary_then_numbers_then_commands()
    {
        var processor = new TranscriptPostProcessor(
            UserDictionary.FromTerms([new DictionaryTerm(["докер"], "Docker")]),
            new PostProcessingOptions(ApplyNumberNormalization: true));

        var result = processor.Process("Запусти докер на двадцать пять минут. Новая строка. Готово");

        Assert.Contains("Docker", result, StringComparison.Ordinal);
        Assert.Contains("25", result, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Pipeline_repairs_the_reported_product_name_variants()
    {
        var processor = new TranscriptPostProcessor(UserDictionary.BuiltIn);

        Assert.Equal(
            "Установи Egoist Voice и EGOIST Translator.",
            processor.Process("установи Egist Voice и Egast-translate."));
    }

    [Fact]
    public void Pipeline_respects_disabled_steps()
    {
        var processor = new TranscriptPostProcessor(
            UserDictionary.FromTerms([new DictionaryTerm(["докер"], "Docker")]),
            new PostProcessingOptions(
                ApplyDictionary: false,
                ApplyVoiceCommands: false,
                ApplyNumberNormalization: false));

        var result = processor.Process("Запусти докер на двадцать пять минут");

        Assert.Equal("Запусти докер на двадцать пять минут", result);
    }

    [Fact]
    public void Pipeline_returns_empty_for_blank_input() =>
        Assert.Equal(string.Empty, new TranscriptPostProcessor().Process("   "));

    private static string Normalized(string text) => TranscriptNormalizer.Normalize(text);
}
