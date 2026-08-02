using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

/// <summary>
/// The terms the user actually reported: they came out in Cyrillic because the substitution
/// dictionary shipped empty and only filled up if somebody found the JSON file first.
/// </summary>
public sealed class BuiltInVocabularyTests
{
    [Theory]
    [InlineData("Открой гитхаб и посмотри коммиты.", "GitHub")]
    [InlineData("Спроси у клод код про эту ошибку.", "Claude Code")]
    [InlineData("Я использую клод каждый день.", "Claude")]
    [InlineData("Разверни докер и проверь бэкенд.", "Docker")]
    [InlineData("Напиши скрипт на пайтоне.", "Python")]
    [InlineData("Проверь логи в кубернетесе.", "Kubernetes")]
    [InlineData("Открой вижуал студио код.", "Visual Studio Code")]
    [InlineData("Создай пул реквест.", "pull request")]
    [InlineData("Спроси у джемини.", "Gemini")]
    [InlineData("Сравни антро пик и дип сик.", "Anthropic")]
    [InlineData("Макет лежит в фиг ма.", "Figma")]
    [InlineData("Открой андроид студио.", "Android Studio")]
    [InlineData("Запусти контр страйк.", "Counter-Strike")]
    [InlineData("Драйвер эн видиа обновлён.", "NVIDIA")]
    public void Known_terms_are_written_in_latin_out_of_the_box(string spoken, string expected) =>
        Assert.Contains(expected, UserDictionary.BuiltIn.Apply(spoken), StringComparison.Ordinal);

    [Theory]
    [InlineData("Запусти кодекс на этой задаче.", EntityProfile.Technology, "Codex")]
    [InlineData("Открой клауд код.", EntityProfile.Technology, "Claude Code")]
    [InlineData("Открой курсор.", EntityProfile.Technology, "Cursor")]
    [InlineData("Запусти стим.", EntityProfile.Gaming, "Steam")]
    public void Ambiguous_entities_require_the_matching_profile(
        string spoken,
        EntityProfile profile,
        string expected) =>
        Assert.Contains(expected, UserDictionary.BuiltIn.Apply(spoken, profile), StringComparison.Ordinal);

    [Theory]
    [InlineData("Гражданский кодекс нужно перечитать.")]
    [InlineData("Поставь курсор в конец строки.")]
    [InlineData("Нужен стимул продолжать игру.")]
    [InlineData("Это мета-анализ нескольких работ.")]
    [InlineData("Google Cloud Code работает с облачным проектом.")]
    public void Negative_context_blocks_ambiguous_brand_repair_even_in_rich_profile(string text) =>
        Assert.Equal(text, UserDictionary.BuiltIn.Apply(text, EntityProfile.All));

    [Theory]
    [InlineData("антро пик", "Anthropic")]
    [InlineData("к лод код", "Claude Code")]
    [InlineData("гит хаб", "GitHub")]
    [InlineData("кубер нетес", "Kubernetes")]
    [InlineData("эн видиа", "NVIDIA")]
    [InlineData("клодкод", "Claude Code")]
    [InlineData("Deapsik", "DeepSeek")]
    public void Catalog_backed_split_and_join_repairs_are_exact(string split, string expected) =>
        Assert.Equal(expected, UserDictionary.BuiltIn.Apply(split));

    [Theory]
    [InlineData("anthropic", "Anthropic")]
    [InlineData("github", "GitHub")]
    [InlineData("playstation", "PlayStation")]
    [InlineData("powershell", "PowerShell")]
    public void Canonical_latin_casing_is_repaired_without_translation(string input, string expected) =>
        Assert.Equal(expected, UserDictionary.BuiltIn.Apply(input));

    [Fact]
    public void The_pipeline_uses_the_built_in_dictionary_by_default()
    {
        // Not just the dictionary in isolation: the default post-processor has to reach it.
        var processor = new TranscriptPostProcessor(UserDictionary.BuiltIn);

        var result = processor.Process("открой гитхаб, запусти докер и спроси у клод код");

        Assert.Contains("GitHub", result, StringComparison.Ordinal);
        Assert.Contains("Docker", result, StringComparison.Ordinal);
        Assert.Contains("Claude Code", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_longer_term_wins_over_the_shorter_one_inside_it()
    {
        // "клод код" must not degrade into "Claude код".
        Assert.Equal("Claude Code.", UserDictionary.BuiltIn.Apply("клод код."));
    }

    [Theory]
    // Термины, исключённые из словаря изначально.
    [InlineData("Он хромает после травмы.")]
    [InlineData("Это интеллектуальная задача.")]
    [InlineData("Поставь курсор в начало строки.")]
    [InlineData("Нужен стимул продолжать.")]
    [InlineData("Сложите данные в ноду дерева.")]
    // Термины, которые какое-то время отгружались и молча портили обычную речь. Каждая строка —
    // ровно та фраза, на которой ревью поймало подмену.
    [InlineData("В зоопарке живёт питон.")]
    [InlineData("Сетчатый питон опасен.")]
    [InlineData("Купи редис и огурцы.")]
    [InlineData("Сломалась телега у дороги.")]
    [InlineData("Ношеный свитер лежал на стуле.")]
    [InlineData("Ношеные джинсы пора выбросить.")]
    [InlineData("У соседа шарпей.")]
    public void Ordinary_russian_is_left_alone(string text) =>
        // A wrong replacement mid-sentence is worse than a missed one: the user can retype a term,
        // but they may not notice a word that was quietly swapped.
        Assert.Equal(text, UserDictionary.BuiltIn.Apply(text));

    [Fact]
    public void Case_endings_are_still_handled_on_built_in_terms()
    {
        Assert.Equal("Смотри в GitHub.", UserDictionary.BuiltIn.Apply("Смотри в гитхабе."));
        Assert.Equal("Собрал Docker.", UserDictionary.BuiltIn.Apply("Собрал докером."));
    }

    [Fact]
    public void A_user_rule_overrides_a_shipped_one()
    {
        // "Кодекс" is the one deliberately debatable entry — a user who dictates legal texts must
        // be able to take it back without editing the application.
        var dictionary = UserDictionary.Parse(
            """{ "terms": [ { "spoken": ["кодекс"], "written": "кодекс" } ] }""");

        Assert.Equal("Гражданский кодекс.", dictionary.Apply("Гражданский кодекс.", EntityProfile.Technology));
        Assert.Contains("GitHub", dictionary.Apply("открой гитхаб"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_dictionary_adds_to_the_built_in_one_rather_than_replacing_it()
    {
        var dictionary = UserDictionary.Parse(
            """{ "terms": [ { "spoken": ["мой проект"], "written": "EgoistCODEX" } ] }""");

        Assert.Contains("EgoistCODEX", dictionary.Apply("открой мой проект"), StringComparison.Ordinal);
        Assert.Contains("Docker", dictionary.Apply("запусти докер"), StringComparison.Ordinal);
    }

    [Fact]
    public void Built_in_terms_feed_the_mixed_speech_detector()
    {
        var detector = new MixedSpeechDetector(
            MixedSpeechDetector.DeriveRussifiedForms(BuiltInVocabulary.SpokenForms));

        Assert.True(detector.Inspect("Спроси у джемини.", false).NeedsFallback);
        Assert.False(detector.Inspect("Обычная фраза без терминов.", false).NeedsFallback);
    }

    [Fact]
    public void Every_shipped_entry_is_well_formed()
    {
        foreach (var term in BuiltInVocabulary.Terms)
        {
            Assert.False(string.IsNullOrWhiteSpace(term.Written), "Пустая замена в словаре.");
            Assert.NotNull(term.Spoken);
            Assert.NotEmpty(term.Spoken!);
            foreach (var spoken in term.Spoken!)
            {
                Assert.False(string.IsNullOrWhiteSpace(spoken), $"Пустая форма у «{term.Written}».");
                Assert.True(
                    spoken.Length >= 3 || string.Equals(spoken, term.Written, StringComparison.OrdinalIgnoreCase),
                    $"Слишком короткая форма «{spoken}» у «{term.Written}».");
            }
        }
    }

    [Fact]
    public void No_spoken_form_is_defined_twice()
    {
        var duplicates = BuiltInVocabulary.Terms
            .SelectMany(term => term.Spoken ?? [])
            .GroupBy(form => form, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.True(duplicates.Length == 0, $"Дубли в словаре: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Versioned_catalog_covers_ai_apps_companies_and_games()
    {
        Assert.Equal("2", BuiltInVocabulary.Version);
        var written = BuiltInVocabulary.Terms
            .Select(term => term.Written)
            .ToHashSet(StringComparer.Ordinal);

        var expected = new[]
        {
            "Claude Code", "Anthropic", "OpenAI", "ChatGPT", "Gemini", "DeepSeek",
            "GitHub", "Docker", "Kubernetes", "Visual Studio Code", "Microsoft Teams",
            "Figma", "Notion", "Cloudflare", "Stripe", "NVIDIA", "AMD", "Intel",
            "Apple", "Microsoft", "Steam", "Epic Games Store", "PlayStation", "Xbox",
            "Unreal Engine", "Unity", "Minecraft", "Counter-Strike", "Cyberpunk 2077"
        };

        Assert.All(expected, entity => Assert.Contains(entity, written));
    }
}
