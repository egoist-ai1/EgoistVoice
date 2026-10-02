using System.Text.RegularExpressions;
using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class AudioTranscriptComposerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Empty_primary_cannot_borrow_hallucinated_words(string? primary)
    {
        Assert.Equal(string.Empty, AudioTranscriptComposer.Compose(primary, "Спасибо за просмотр!"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("!!!")]
    [InlineData("\ud800")]
    public void Missing_secondary_preserves_primary_exactly(string? secondary)
    {
        const string primary = "  здесь  HuggingFace v3 работает \t";
        Assert.Equal(primary, AudioTranscriptComposer.Compose(primary, secondary));
    }

    [Theory]
    [InlineData("я люблю чай", "Я очень люблю кофе!", "Я люблю чай!")]
    [InlineData("я очень люблю чай", "Я люблю чай.", "Я очень люблю чай.")]
    [InlineData("один два один", "Один, один.", "Один, два один.")]
    [InlineData("привет мир", "Спасибо за просмотр!", "привет мир!")]
    [InlineData("все еще работает", "Всё ещё работает.", "Все еще работает.")]
    [InlineData("используй HuggingFace RNNT v3 и API42", "Используй HUGGINGFACE, rnnt V3 и api42!", "Используй HuggingFace, RNNT v3 и API42!")]
    [InlineData("цена 42 и 7", "Цена 41, и 9.", "Цена 42, и 7.")]
    [InlineData("привет мир", "Привет?!. Мир:;", "Привет?!. Мир:;")]
    [InlineData("три два один", "Три три два один один!", "три два один!")]
    public void Alignment_projects_surface_but_never_secondary_words(
        string primary, string secondary, string expected)
    {
        var actual = AudioTranscriptComposer.Compose(primary, secondary);
        Assert.Equal(expected, actual);
        Assert.Equal(LexicalTokens(primary), LexicalTokens(actual));
    }

    [Theory]
    [InlineData("открой https://example.org/API?q=42")]
    [InlineData(@"открой C:\Models\GigaAM\encoder.int8.onnx")]
    [InlineData("сохрани ../models/encoder.onnx")]
    [InlineData("проверь user_name и foo.Bar(x)")]
    [InlineData("письмо User.Name@example.org")]
    [InlineData("значение -2.50 и 3,14 версия 2.3.0")]
    [InlineData("return value != null;")]
    [InlineData("«Слова», уже расставлены: сохрани!")]
    [InlineData("по-русски и всё-таки")]
    [InlineData("первая строка\nвторая строка")]
    [InlineData("... 🙂")]
    public void Literal_syntax_and_explicit_primary_punctuation_are_preserved(string primary)
    {
        Assert.Equal(primary, AudioTranscriptComposer.Compose(primary, "Другая модель: меняет всё!"));
    }

    [Fact]
    public void Latin_and_mixed_name_case_is_preserved_while_Russian_initial_case_is_borrowed()
    {
        const string primary = "работает egoistVoice café é RNNT имяAPI2 ёлка";
        var actual = AudioTranscriptComposer.Compose(primary, "Работает EGOISTVOICE CAFÉ É rnnt Имяapi2 Елка.");
        Assert.Equal("Работает egoistVoice café é RNNT имяAPI2 Ёлка.", actual);
    }

    [Fact]
    public void Normalization_for_alignment_does_not_replace_primary_yo_or_existing_case()
    {
        Assert.Equal("Ёж всё знает.", AudioTranscriptComposer.Compose("ёж всё знает", "Еж все знает."));
        Assert.Equal("Мой ДОМ.", AudioTranscriptComposer.Compose("Мой ДОМ", "мой дом."));
    }

    [Fact]
    public void Unicode_letter_and_number_tokens_survive_without_utf16_splitting()
    {
        const string primary = "слово 𝟙 𐐨 слово";
        var actual = AudioTranscriptComposer.Compose(primary, "Слово 𝟚 𐐀 слово.");
        Assert.Equal("Слово 𝟙 𐐨 слово.", actual);
        Assert.Equal(primary.Split(' ').Skip(1).Take(2), actual.Split(' ').Skip(1).Take(2));
    }

    [Fact]
    public void Secondary_leading_and_extra_words_cannot_enter_output()
    {
        const string primary = "мой текст";
        var actual = AudioTranscriptComposer.Compose(primary, "[шум] мой чужой текст ненужные слова.");
        Assert.Equal(LexicalTokens(primary), LexicalTokens(actual));
        Assert.DoesNotContain("шум", actual);
        Assert.DoesNotContain("чужой", actual);
        Assert.DoesNotContain("ненужные", actual);
    }

    [Fact]
    public void Very_long_input_retains_primary_without_unbounded_alignment()
    {
        var primary = string.Join(' ', Enumerable.Repeat("слово", 4_096));
        var secondary = string.Join(' ', Enumerable.Repeat("Слово!", 4_096));
        Assert.Equal(primary, AudioTranscriptComposer.Compose(primary, secondary));
        Assert.Equal("тихий голос", AudioTranscriptComposer.Compose("тихий голос", new string('а', 100_000)));
    }

    [Fact]
    public void Matrix_limit_retains_primary_even_when_each_side_fits_token_limit()
    {
        var primary = string.Join(' ', Enumerable.Repeat("слово", 512));
        var secondary = string.Join(' ', Enumerable.Repeat("Слово!", 512));
        Assert.Equal(primary, AudioTranscriptComposer.Compose(primary, secondary));
    }

    [Fact]
    public void Moderate_alignment_is_deterministic_and_retains_every_primary_word()
    {
        var primary = string.Join(' ', Enumerable.Range(0, 300).Select(index => $"слово{index}"));
        var secondary = "Вставка, " + string.Join(' ', Enumerable.Range(0, 300).Select(index => $"Слово{index},"));
        var first = AudioTranscriptComposer.Compose(primary, secondary);
        Assert.Equal(first, AudioTranscriptComposer.Compose(primary, secondary));
        Assert.Equal(LexicalTokens(primary), LexicalTokens(first));
        Assert.EndsWith("слово299,", first);
    }

    private static string[] LexicalTokens(string value) => Regex.Matches(value, @"[\p{L}\p{N}]+")
        .Select(match => match.Value.ToLowerInvariant().Replace('ё', 'е')).ToArray();
}
