using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class TranscriptNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData(" привет   мир ", "Привет мир")]
    [InlineData("привет , мир !", "Привет, мир!")]
    [InlineData("текст  ( внутри ) ", "Текст (внутри)")]
    [InlineData("у лукоморья дуб зелёный.", "У лукоморья дуб зелёный.")]
    public void Normalize_returns_clean_russian_text(string? input, string expected)
    {
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_preserves_paragraph_breaks()
    {
        var input = "первый абзац.\r\n\r\n   второй   абзац.";
        var expected = $"Первый абзац.{Environment.NewLine}{Environment.NewLine}Второй абзац.";

        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }
    [Theory]
    [InlineData("из за погоды", "Из-за погоды")]
    [InlineData("из под стола", "Из-под стола")]
    [InlineData("как то так", "Как-то так")]
    [InlineData("что то интересное", "Что-то интересное")]
    [InlineData("где то там", "Где-то там")]
    [InlineData("кто нибудь знает", "Кто-нибудь знает")]
    [InlineData("все таки получилось", "Всё-таки получилось")]
    [InlineData("по прежнему жду", "По-прежнему жду")]
    [InlineData("кое как успели", "Кое-как успели")]
    [InlineData("по русски говори", "По-русски говори")]
    [InlineData("по английски напиши", "По-английски напиши")]
    [InlineData("сделай по быстрому", "Сделай по-быстрому")]
    [InlineData("сделай по новому", "Сделай по-новому")]
    [InlineData("по нашему это лучший вариант", "По-нашему это лучший вариант")]
    [InlineData("по новому адресу", "По новому адресу")]
    [InlineData("по нашему плану", "По нашему плану")]
    [InlineData("надо по другому сделать", "Надо по-другому сделать")]
    [InlineData("чуть чуть подожди", "Чуть-чуть подожди")]
    [InlineData("давным давно было", "Давным-давно было")]
    [InlineData("точь в точь совпало", "Точь-в-точь совпало")]
    public void Normalize_repairs_russian_hyphens(string input, string expected)
    {
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("красиво но сложно", "Красиво, но сложно")]
    [InlineData("не я а он", "Не я, а он")]
    [InlineData("план А сработал", "План А сработал")]
    [InlineData("я опоздал потому что был занят", "Я опоздал, потому что был занят")]
    [InlineData("мы успели хотя было трудно", "Мы успели, хотя было трудно")]
    [InlineData("принеси хотя бы хлеб.", "Принеси хотя бы хлеб.")]
    [InlineData("он сказал что придет", "Он сказал, что придет")]
    [InlineData("я уверен что это сработает", "Я уверен, что это сработает")]
    public void Normalize_adds_russian_commas(string input, string expected)
    {
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("ты уверен", "Ты уверен?")]
    [InlineData("как дела", "Как дела?")]
    [InlineData("почему это не работает", "Почему это не работает?")]
    [InlineData("где ты находишься", "Где ты находишься?")]
    [InlineData("где логи", "Где логи?")]
    [InlineData("сколько фпс выдает", "Сколько фпс выдает?")]
    [InlineData("ты сможешь помочь", "Ты сможешь помочь?")]
    [InlineData("ты хочешь чай", "Ты хочешь чай?")]
    [InlineData("ты уверен что мы успеем", "Ты уверен, что мы успеем?")]
    [InlineData("Привет. Как дела", "Привет. Как дела?")]
    public void Normalize_detects_questions_and_appends_question_mark(string input, string expected)
    {
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("вообщем все готово", "В общем все готово")]
    [InlineData("расскажи в крации", "Расскажи вкратце")]
    [InlineData("он как-будто не слышит", "Он как будто не слышит")]
    [InlineData("надо зделать это", "Надо сделать это")]
    [InlineData("сдесь никого нет", "Здесь никого нет")]
    [InlineData("выполняй паралельно", "Выполняй параллельно")]
    [InlineData("точность распознования", "Точность распознавания")]
    [InlineData("пиши граммотно", "Пиши грамотно")]
    [InlineData("будующий релиз", "Будущий релиз")]
    [InlineData("отправь скрин шот", "Отправь скриншот")]
    public void Normalize_repairs_common_orthographic_and_asr_errors(string input, string expected)
    {
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }
}
