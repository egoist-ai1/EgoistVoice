using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

/// <summary>
/// One test per defect found in review. Each of these passed silently before the fix, which is
/// exactly why they belong here rather than in the suite for the feature they guard.
/// </summary>
public sealed class ReviewRegressionTests
{
    [Fact]
    public void A_command_segment_carrying_a_number_is_not_treated_as_a_command()
    {
        // Folding drops non-letters, so "новая строка 5" collapsed onto the command key and the
        // digit disappeared from the user's text. Silent data loss, no log line, no way to notice.
        var processor = new VoiceCommandProcessor();

        var result = processor.Apply("Первая. новая строка 5. вторая");

        Assert.Contains("5", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_segment_carrying_punctuation_is_not_treated_as_a_command()
    {
        var processor = new VoiceCommandProcessor();
        const string text = "Смотри. запятая — важно. дальше";

        Assert.Contains("важно", processor.Apply(text), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("пять пять", "5 5")]
    [InlineData("два два", "2 2")]
    [InlineData("сорок сорок", "40 40")]
    [InlineData("три три пять", "3 3 5")]
    public void Repeated_magnitudes_stay_separate_numbers(string spoken, string expected) =>
        Assert.Equal(expected, RussianNumberNormalizer.Normalize(spoken));

    [Theory]
    [InlineData("сто двадцать три", "123")]
    [InlineData("две тысячи двадцать шесть", "2026")]
    [InlineData("сто тысяч", "100000")]
    [InlineData("два миллиона три", "2000003")]
    public void Genuine_compound_numbers_still_combine(string spoken, string expected) =>
        Assert.Equal(expected, RussianNumberNormalizer.Normalize(spoken));

    [Theory]
    [InlineData("Встреча двадцать первое июля.", "Встреча 21 июля.")]
    [InlineData("Отчёт тридцать первое декабря.", "Отчёт 31 декабря.")]
    public void Two_word_ordinal_dates_are_read_as_one_day(string spoken, string expected) =>
        // These used to come out as "20 1 июля": the cardinal branch ate "двадцать" and the date
        // branch handled "первое июля" separately.
        Assert.Equal(expected, RussianNumberNormalizer.Normalize(spoken));

    [Theory]
    [InlineData("кодекс")]
    [InlineData("кодер")]
    [InlineData("кодировка")]
    public void A_short_dictionary_term_does_not_capture_longer_words(string word)
    {
        // The previous "up to three trailing letters" rule was tolerance of inflection in name only.
        var dictionary = UserDictionary.FromTerms([new DictionaryTerm(["код"], "code")]);

        Assert.Equal(word, dictionary.Apply(word));
    }

    [Theory]
    [InlineData("кода")]
    [InlineData("коде")]
    [InlineData("кодом")]
    public void Real_case_endings_are_still_matched(string word)
    {
        var dictionary = UserDictionary.FromTerms([new DictionaryTerm(["код"], "code")]);

        Assert.Equal("code", dictionary.Apply(word));
    }

    [Fact]
    public void A_written_form_containing_a_dollar_sign_is_inserted_literally()
    {
        // Regex.Replace interprets $1, $& and $$ in the replacement string, so a term written as
        // "$100" came out as garbage or threw.
        var dictionary = UserDictionary.FromTerms([new DictionaryTerm(["сто баксов"], "$100")]);

        Assert.Equal("Это $100.", dictionary.Apply("Это сто баксов."));
    }

    [Theory]
    [InlineData("Это интеллектуальная задача.")]
    [InlineData("Он хромает после травмы.")]
    [InlineData("Нужен стимул продолжать.")]
    [InlineData("Интеллигентный человек.")]
    [InlineData("Сетчатый питон лежит на ветке.")]
    public void Ordinary_russian_words_do_not_trigger_the_fallback(string transcript)
    {
        // Stems like "интел", "хром" and "стим" matched these words as prefixes, and every false
        // trigger costs a full Whisper pass — the very latency the detector exists to avoid.
        Assert.False(new MixedSpeechDetector().Inspect(transcript, false).NeedsFallback);
    }

    [Theory]
    [InlineData("Открой гитхаб.")]
    [InlineData("Разверни докер.")]
    [InlineData("Спроси джемини.")]
    public void Real_russified_terms_still_trigger_the_fallback(string transcript) =>
        Assert.True(new MixedSpeechDetector().Inspect(transcript, false).NeedsFallback);

    [Fact]
    public void Non_ascii_digits_do_not_crash_the_scorer()
    {
        // char.IsDigit accepts Arabic-Indic digits, and the offset arithmetic then indexed far
        // outside the word table.
        var options = ScoringOptions.Default with { ExpandDigits = true };

        var score = RecognitionScorer.Score("пять", "٥", options);

        Assert.True(score.WordErrors >= 0);
    }

    [Fact]
    public void Every_paste_method_still_emits_balanced_events()
    {
        foreach (var method in Enum.GetValues<PasteMethod>())
        {
            if (method == PasteMethod.ClipboardOnly)
            {
                continue;
            }

            var events = TextInsertionService.PasteEventCount(method);
            Assert.True(events % 2 == 0, $"{method}: {events} событий — нажатия и отпускания не сбалансированы.");
        }
    }

    [Fact]
    public void Whisper_unload_refuses_while_the_engine_is_busy()
    {
        // TryUnload took the factory lock with a zero timeout, but the decode path never held that
        // lock — so the unload always succeeded and released the native context under a running
        // decode. An access violation, not a catchable exception.
        using var whisper = new WhisperTranscriptionService(new NeverReadyModelManager());

        Assert.False(whisper.TryUnload());
    }

    private sealed class NeverReadyModelManager : IModelManager
    {
        public event EventHandler<ModelTransferProgress>? ProgressChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<ModelDescriptor> RequiredModels => [];
        public bool AreAllModelsReady => false;
        public ModelTransferProgress? CurrentProgress => null;

        public Task<string> EnsureModelAsync(
            ModelDescriptor descriptor,
            IProgress<ModelTransferProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DownloadRequiredModelsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
