using Egoist.Voice.Core;
using System.Text.Json;
using Xunit;

namespace Egoist.Voice.Tests;

public sealed class TranslateCommandParserTests
{
    [Fact]
    public void PrefixBareCommand_DefaultsToEnglish()
    {
        var d = TranslateCommandParser.TryParse("Переведи: привет, как дела?");
        Assert.NotNull(d);
        Assert.Equal("English", d.TargetLanguage);
        Assert.Equal("привет, как дела?", d.Payload);
        Assert.Equal("Auto", d.Direction.SourceLanguage);
        Assert.Equal(TranslateCommandPosition.Prefix, d.Position);
        Assert.Equal(TranslateCommandMatchClass.DefaultTarget, d.MatchClass);
    }

    [Fact]
    public void PrefixWithThis_StripsCommandWords()
    {
        var d = TranslateCommandParser.TryParse("Переведи это: я тебя жду у входа.");
        Assert.NotNull(d);
        Assert.Equal("я тебя жду у входа.", d.Payload);
        Assert.Equal("English", d.TargetLanguage);
    }

    [Fact]
    public void PrefixWithLanguage_ResolvesLanguage()
    {
        var d = TranslateCommandParser.TryParse("Переведи на немецкий, встреча переносится на завтра.");
        Assert.NotNull(d);
        Assert.Equal("German", d.TargetLanguage);
        Assert.Equal("встреча переносится на завтра.", d.Payload);
    }

    [Fact]
    public void PrefixWithLanguageAndYazikWord()
    {
        var d = TranslateCommandParser.TryParse("Переведи на английский язык добрый вечер всем");
        Assert.NotNull(d);
        Assert.Equal("English", d.TargetLanguage);
        Assert.Equal("добрый вечер всем", d.Payload);
    }

    [Fact]
    public void PrefixAdverbForm_PoAngliyski()
    {
        var d = TranslateCommandParser.TryParse("Переведи по-английски как пройти в библиотеку");
        Assert.NotNull(d);
        Assert.Equal("English", d.TargetLanguage);
        Assert.Equal("как пройти в библиотеку", d.Payload);
    }

    [Fact]
    public void SuffixBareCommand()
    {
        var d = TranslateCommandParser.TryParse("Привет, как дела? Переведи.");
        Assert.NotNull(d);
        Assert.Equal("English", d.TargetLanguage);
        Assert.Equal("Привет, как дела?", d.Payload);
    }

    [Fact]
    public void SuffixWithLanguage()
    {
        var d = TranslateCommandParser.TryParse("Скажи ему, что всё готово, переведи на японский");
        Assert.NotNull(d);
        Assert.Equal("Japanese", d.TargetLanguage);
        Assert.Equal("Скажи ему, что всё готово", d.Payload);
        Assert.Equal(TranslateCommandPosition.Suffix, d.Position);
        Assert.Equal(TranslateCommandMatchClass.ExplicitTarget, d.MatchClass);
    }

    [Fact]
    public void SuffixWithThisWord()
    {
        var d = TranslateCommandParser.TryParse("Мне нужно два билета до центра. Переведи это.");
        Assert.NotNull(d);
        Assert.Equal("Мне нужно два билета до центра.", d.Payload);
    }

    [Fact]
    public void UnknownLanguageAfterNa_IsNotATranslateCommand()
    {
        // «переведи на завтра …» — это перенос встречи, а не смена языка.
        Assert.Null(TranslateCommandParser.TryParse("Переведи на завтра нашу встречу"));
    }

    [Fact]
    public void CommandInsideSentence_DoesNotTrigger() =>
        Assert.Null(TranslateCommandParser.TryParse("Я попросил его переведи текст самостоятельно и ушёл домой"));

    [Fact]
    public void BareCommandWithoutPayload_DoesNotTrigger() =>
        Assert.Null(TranslateCommandParser.TryParse("Переведи"));

    [Fact]
    public void EmptyInput_DoesNotTrigger() =>
        Assert.Null(TranslateCommandParser.TryParse("   "));

    [Fact]
    public void SuffixWithDoubleTailAndFiller_RealDictation()
    {
        // Реальная фраза из диктовки, на которой команда не сработала в v1.
        var d = TranslateCommandParser.TryParse(
            "Кстати, вчера был ураган, но это не страшно, мы пошли играть Dota 2 за Shaker, " +
            "соответственно переведи это все на английский.");
        Assert.NotNull(d);
        Assert.Equal("English", d.TargetLanguage);
        Assert.Equal("Кстати, вчера был ураган, но это не страшно, мы пошли играть Dota 2 за Shaker", d.Payload);
    }

    [Fact]
    public void PrefixWithReversedDoubleTail()
    {
        var d = TranslateCommandParser.TryParse("Переведи всё это на немецкий добрый день, коллеги");
        Assert.NotNull(d);
        Assert.Equal("German", d.TargetLanguage);
        Assert.Equal("добрый день, коллеги", d.Payload);
    }

    [Fact]
    public void SuffixFillerPozhaluysta_IsStripped()
    {
        var d = TranslateCommandParser.TryParse("Я задержусь на десять минут; пожалуйста, переведи");
        Assert.NotNull(d);
        Assert.Equal("Я задержусь на десять минут", d.Payload);
    }

    [Fact]
    public void YoNormalization_InLanguageName()
    {
        var d = TranslateCommandParser.TryParse("Переведи на казахский добрый день");
        Assert.NotNull(d);
        Assert.Equal("Kazakh", d.TargetLanguage);
    }

    // ── Формы команды ────────────────────────────────────────────────────────────────────────
    // Командой считаются только повелительные формы. Существительное «перевод», прошедшее
    // время «перевёл» и инфинитив «перевести» встречаются в обычной диктовке слишком часто.

    [Theory]
    [InlineData("Переведи: привет")]
    [InlineData("Переведите: привет")]
    [InlineData("Переводи: привет")]
    [InlineData("Переводите: привет")]
    [InlineData("Переведи-ка: привет")]
    [InlineData("Переведика: привет")]
    public void CommandForms_AreRecognisedAsPrefix(string text)
    {
        var directive = TranslateCommandParser.TryParse(text);
        Assert.NotNull(directive);
        Assert.Equal("привет", directive.Payload);
    }

    [Theory]
    [InlineData("Я закончил документ, переведи")]
    [InlineData("Я закончил документ, переведите")]
    [InlineData("Я закончил документ, переводи")]
    [InlineData("Я закончил документ, переводите")]
    public void CommandForms_AreRecognisedAsSuffix(string text)
    {
        var directive = TranslateCommandParser.TryParse(text);
        Assert.NotNull(directive);
        Assert.StartsWith("Я закончил документ", directive.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Related_words_are_not_commands()
    {
        // «переводчик», «переводной», «перевозка» начинаются так же, но командой не являются.
        Assert.Null(TranslateCommandParser.TryParse("Открой переводчик и вставь туда текст"));
        Assert.Null(TranslateCommandParser.TryParse("Это переводной роман, я его читал"));
        Assert.Null(TranslateCommandParser.TryParse("Закажи перевозку на понедельник"));
    }

    [Theory]
    [InlineData("Перевод денег задержался")]
    [InlineData("Я закончил перевод")]
    [InlineData("Перевёл документ вчера")]
    [InlineData("Я закончил документ, а ты перевёл")]
    [InlineData("Переведёшь документ завтра")]
    [InlineData("Перевести документ было сложно")]
    public void Ordinary_grammar_forms_are_not_commands(string text) =>
        Assert.Null(TranslateCommandParser.TryParse(text));

    // ── Служебные слова между командой и текстом ─────────────────────────────────────────────

    [Theory]
    [InlineData("Переведи вот это всё: встречаемся в шесть")]
    [InlineData("Переведи всё, что я сказал: встречаемся в шесть")]
    [InlineData("Переведи всё, что я скажу — встречаемся в шесть")]
    [InlineData("Переведи мне, пожалуйста, вот это: встречаемся в шесть")]
    [InlineData("Переведи-ка вот этот текст: встречаемся в шесть")]
    [InlineData("Переведи следующее: встречаемся в шесть")]
    [InlineData("Переведи дальше: встречаемся в шесть")]
    public void TailWords_AreStrippedBeforePayload(string text)
    {
        var directive = TranslateCommandParser.TryParse(text);
        Assert.NotNull(directive);
        Assert.Equal("встречаемся в шесть", directive.Payload);
    }

    [Fact]
    public void TailWords_And_language_in_either_order()
    {
        var first = TranslateCommandParser.TryParse("Переведи на испанский вот это всё: добрый день");
        var second = TranslateCommandParser.TryParse("Переведи вот это всё на испанский: добрый день");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("Spanish", first.TargetLanguage);
        Assert.Equal("Spanish", second.TargetLanguage);
        Assert.Equal("добрый день", first.Payload);
        Assert.Equal("добрый день", second.Payload);
    }

    // ── Языки ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("английский", "English")]
    [InlineData("английском", "English")]
    [InlineData("English", "English")]
    [InlineData("инглиш", "English")]
    [InlineData("испанский", "Spanish")]
    [InlineData("испаньол", "Spanish")]
    [InlineData("Spanish", "Spanish")]
    [InlineData("немецкий", "German")]
    [InlineData("германский", "German")]
    [InlineData("нидерландский", "Dutch")]
    [InlineData("голландский", "Dutch")]
    [InlineData("французском", "French")]
    [InlineData("French", "French")]
    [InlineData("русский", "Russian")]
    [InlineData("Russian", "Russian")]
    [InlineData("африканский", "Afrikaans")]
    [InlineData("африкаанс", "Afrikaans")]
    [InlineData("сеульский", "Korean")]
    [InlineData("корейском", "Korean")]
    [InlineData("хинди", "Hindi")]
    [InlineData("фарси", "Persian")]
    [InlineData("иврит", "Hebrew")]
    [InlineData("украинский", "Ukrainian")]
    [InlineData("шведский", "Swedish")]
    [InlineData("грузинский", "Georgian")]
    public void Language_names_resolve_in_any_case_form(string spoken, string expected)
    {
        var directive = TranslateCommandParser.TryParse($"Переведи на {spoken} добрый день");
        Assert.NotNull(directive);
        Assert.Equal(expected, directive.TargetLanguage);
    }

    [Theory]
    [InlineData("по-английски", "English")]
    [InlineData("по английски", "English")]
    [InlineData("по-немецки", "German")]
    [InlineData("по-испански", "Spanish")]
    [InlineData("по-французски", "French")]
    [InlineData("по-корейски", "Korean")]
    public void Adverb_forms_resolve(string spoken, string expected)
    {
        var directive = TranslateCommandParser.TryParse($"Переведи {spoken} добрый день");
        Assert.NotNull(directive);
        Assert.Equal(expected, directive.TargetLanguage);
    }

    [Fact]
    public void Language_catalogue_has_no_empty_or_duplicate_names()
    {
        Assert.All(TranslationLanguages.Names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(TranslationLanguages.Names.Count, TranslationLanguages.Names.Distinct().Count());
        Assert.True(TranslationLanguages.SpellingCount > 400, "Словарь написаний подозрительно мал.");
    }

    [Fact]
    public void Ordinary_speech_is_not_mistaken_for_a_language()
    {
        // Слово после «на» проверяется по словарю: без этого «переведи на потом» стало бы командой.
        Assert.Null(TranslateCommandParser.TryParse("Переведи на потом эту задачу"));
        Assert.Null(TranslateCommandParser.TryParse("Переведи на другую страницу"));
    }

    [Theory]
    [InlineData("Переведи стрелки часов на завтра утром.")]
    [InlineData("Переведи деньги на другой банковский счёт.")]
    [InlineData("Переведи курсор на начало строки.")]
    [InlineData("Переведи разговор на другую тему.")]
    public void Bare_prefix_without_explicit_boundary_is_not_a_command(string text) =>
        Assert.Null(TranslateCommandParser.TryParse(text));

    [Fact]
    public void Mention_ending_in_command_word_is_not_a_suffix_command() =>
        Assert.Null(TranslateCommandParser.TryParse("Текст заканчивается примером команды переведи."));

    [Fact]
    public void Example_introduction_before_command_is_not_executed() =>
        Assert.Null(TranslateCommandParser.TryParse("Я повторю пример: переведи следующую фразу."));

    [Fact]
    public void Explicit_language_without_colon_preserves_payload_nouns()
    {
        var directive = TranslateCommandParser.TryParse(
            "Переведи на английский это сообщение для команды.");

        Assert.NotNull(directive);
        Assert.Equal("это сообщение для команды.", directive.Payload);
    }

    [Fact]
    public void Locked_command_fixture_meets_recall_and_precision_gate()
    {
        var misses = new List<string>();
        var falsePositives = new List<string>();
        var positives = 0;
        var negatives = 0;

        foreach (var line in File.ReadLines(FindProjectFile("tests", "corpus", "script.jsonl")))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("translationCommand", out var expectedElement))
            {
                continue;
            }

            var expected = expectedElement.GetBoolean();
            var id = root.GetProperty("id").GetString() ?? "<unknown>";
            var text = root.GetProperty("text").GetString() ?? string.Empty;
            var actual = TranslateCommandParser.TryParse(text) is not null;

            if (expected)
            {
                positives++;
                if (!actual)
                {
                    misses.Add(id);
                }
            }
            else
            {
                negatives++;
                if (actual)
                {
                    falsePositives.Add(id);
                }
            }
        }

        Assert.Equal(40, positives);
        Assert.Equal(80, negatives);
        Assert.True(misses.Count == 0, $"Missed commands: {string.Join(", ", misses)}");
        Assert.True(falsePositives.Count == 0, $"False commands: {string.Join(", ", falsePositives)}");
    }

    private static string FindProjectFile(params string[] relativeParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Egoist.Voice.csproj")))
            {
                continue;
            }

            return Path.Combine([directory.FullName, .. relativeParts]);
        }

        throw new DirectoryNotFoundException("Egoist Voice project root was not found.");
    }
}
