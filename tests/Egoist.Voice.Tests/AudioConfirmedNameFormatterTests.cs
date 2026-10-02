using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class AudioConfirmedNameFormatterTests
{
    [Theory]
    [InlineData("Открой гитхаб.", "Открой GitHub.", "Открой GitHub.")]
    [InlineData("Открой гит хаб!", "Открой GITHUB!", "Открой GitHub!")]
    [InlineData("Проверь хаггинг фейс и гитлаб.", "Проверь Hugging Face и GitLab.", "Проверь Hugging Face и GitLab.")]
    [InlineData("Эгоист войс и эгоист кодекс работают.", "Egoist Voice и Egoist Codex работают.", "Egoist Voice и Egoist Codex работают.")]
    [InlineData("Используй вижуал студио код.", "Используй Visual Studio Code.", "Используй Visual Studio Code.")]
    [InlineData("гитхаб", "GitHub", "GitHub")]
    [InlineData("Все ещё: гитхаб, API42, мой ДОМ!", "Всё еще GitHub API42 мой дом.", "Все ещё: GitHub, API42, мой ДОМ!")]
    [InlineData("гитхаб затем гитхаб", "GitHub затем гитхаб", "GitHub затем гитхаб")]
    [InlineData("гитхаб затем гитхаб", "гитхаб затем GitHub", "гитхаб затем GitHub")]
    public void Exact_known_names_require_whole_chunk_agreement_and_exact_latin_secondary(
        string primary, string secondary, string expected)
    {
        Assert.Equal(expected, AudioConfirmedNameFormatter.Apply(primary, secondary));
    }

    [Theory]
    [InlineData("Открой гитхаб.", "Открой GitLab.")]
    [InlineData("Открой гитхаб.", "Проверь GitHub.")]
    [InlineData("Открой гитхаб и чай.", "Открой GitHub и кофе.")]
    [InlineData("Открой гитхаб.", "Открой GitHub сейчас.")]
    [InlineData("Сначала гитхаб затем гитлаб.", "Сначала GitLab затем GitHub.")]
    [InlineData("Открой гитхаб.", "Открой гитхаб.")]
    [InlineData("Открой гитхаб.", "Открой Git Hub.")]
    [InlineData("Открой гитхабе.", "Открой GitHub.")]
    [InlineData("Открой неизвестный ксанафор.", "Открой неизвестный Xanafor.")]
    [InlineData("Используй питон и кодекс.", "Используй Python и Codex.")]
    [InlineData("Перемести курсор к мета анализу.", "Перемести Cursor к Meta анализу.")]
    [InlineData("Неон и электрон и блендер.", "Neon и Electron и Blender.")]
    [InlineData("Был зум и джейсон.", "Был Zoom и JSON.")]
    [InlineData("Запусти стим.", "Запусти Steam.")]
    [InlineData("Используй клауд код.", "Используй Claude Code.")]
    [InlineData("Нужен пайплайн и деплой.", "Нужен pipeline и deploy.")]
    [InlineData("сенк ю", "Thank you")]
    [InlineData("гуд лак", "Good luck")]
    [InlineData("ар ю шур", "Are you sure")]
    [InlineData("сенк ю вери мач", "Thank you so much")]
    [InlineData("фром раша", "from Russia")]
    [InlineData("вел дан", "Well done")]
    [InlineData("бест регардс", "Best regards")]
    public void Ambiguity_unknown_names_morphology_and_other_word_changes_abstain(
        string primary, string secondary)
    {
        Assert.Equal(primary, AudioConfirmedNameFormatter.Apply(primary, secondary));
    }

    [Theory]
    [InlineData("Открой гитхаб.txt.", "Открой GitHub.txt.")]
    [InlineData("Открой https://гитхаб.ru.", "Открой https://GitHub.ru.")]
    [InlineData(@"Открой C:\гитхаб\file.", @"Открой C:\GitHub\file.")]
    [InlineData("Проверь гитхаб_name.", "Проверь GitHub_name.")]
    [InlineData("Версия 2.3.0 гитхаб.", "Версия 2.3.0 GitHub.")]
    [InlineData("Почта гитхаб@example.org.", "Почта GitHub@example.org.")]
    [InlineData("гитхаб-гитлаб", "GitHub-GitLab")]
    [InlineData("𐐨-гитхаб", "𐐨-GitHub")]
    [InlineData("гитхаб\nгитлаб", "GitHub\nGitLab")]
    [InlineData("гит хаб", "GitHub/../../private")]
    public void Structured_literals_and_nonspace_alias_separators_are_preserved(
        string primary, string secondary)
    {
        Assert.Equal(primary, AudioConfirmedNameFormatter.Apply(primary, secondary));
    }

    [Fact]
    public void Whitespace_and_punctuation_outside_verified_entity_spans_are_preserved()
    {
        const string primary = "  Открой  гит   хаб,\tпотом гитлаб!  ";
        Assert.Equal("  Открой  GitHub,\tпотом GitLab!  ",
            AudioConfirmedNameFormatter.Apply(primary, "Открой GitHub потом GitLab."));
    }

    [Fact]
    public void Internal_entity_punctuation_does_not_become_alias_whitespace()
    {
        const string primary = "Открой гит, хаб.";
        Assert.Equal(primary, AudioConfirmedNameFormatter.Apply(primary, "Открой GitHub."));
    }

    [Fact]
    public void Formatting_runs_after_punctuation_projection()
    {
        var composed = AudioTranscriptComposer.Compose("открой гитхаб", "Открой GitHub.");
        Assert.Equal("Открой GitHub.", AudioConfirmedNameFormatter.Apply(composed, "Открой GitHub."));
    }

    [Fact]
    public void Repeated_application_is_idempotent()
    {
        var first = AudioConfirmedNameFormatter.Apply("гитхаб и хаггинг фейс", "GitHub и Hugging Face.");
        Assert.Equal("GitHub и Hugging Face", first);
        Assert.Equal(first, AudioConfirmedNameFormatter.Apply(first, "GitHub и Hugging Face."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("!!!")]
    [InlineData("\ud800")]
    public void Missing_or_invalid_secondary_retains_primary(string? secondary)
    {
        Assert.Equal("Открой гитхаб.", AudioConfirmedNameFormatter.Apply("Открой гитхаб.", secondary));
    }

    [Fact]
    public void Bounded_input_abstains_instead_of_allocating_unlimited_work()
    {
        var primary = string.Join(' ', Enumerable.Repeat("гитхаб", 513));
        var secondary = string.Join(' ', Enumerable.Repeat("GitHub", 513));
        Assert.Equal(primary, AudioConfirmedNameFormatter.Apply(primary, secondary));
        var oversized = "гитхаб " + new string('а', 32_768);
        Assert.Equal(oversized, AudioConfirmedNameFormatter.Apply(oversized, "GitHub"));
        Assert.Equal("гитхаб", AudioConfirmedNameFormatter.Apply("гитхаб", new string('a', 32_769)));
    }

    [Fact]
    public void Invalid_primary_utf16_is_preserved()
    {
        const string primary = "гитхаб \ud800";
        Assert.Equal(primary, AudioConfirmedNameFormatter.Apply(primary, "GitHub"));
    }
}
