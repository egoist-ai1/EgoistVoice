using System.IO;
using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class CorpusScriptTests
{
    private static CorpusScript Parse(params string[] lines) => CorpusScript.Parse(lines);

    [Fact]
    public void Set_headers_describe_the_lines_that_follow()
    {
        var script = Parse(
            "// комментарий",
            """{"kind":"set","set":"ru-clean","title":"Обычная речь","hint":"Тихая комната"}""",
            """{"kind":"line","id":"ru-clean/001","text":"Первая фраза"}""");

        var line = Assert.Single(script.Lines);
        Assert.Equal("ru-clean", line.Set);
        Assert.Equal("ru-clean/001.wav", line.Audio);
        Assert.Equal("Тихая комната", script.Sets["ru-clean"].Hint);
    }

    [Fact]
    public void Set_comes_from_the_id_not_from_the_last_header()
    {
        // Строка, оказавшаяся не под своим заголовком, должна попасть в набор, на который
        // указывает её имя файла: иначе отчёт по наборам разойдётся с содержимым каталога.
        var script = Parse(
            """{"kind":"set","set":"ru-clean","title":"Обычная речь","hint":""}""",
            """{"kind":"line","id":"ru-numbers/004","text":"Двадцать пятого июля"}""");

        Assert.Equal("ru-numbers", Assert.Single(script.Lines).Set);
    }

    [Fact]
    public void Duplicate_ids_are_rejected_instead_of_silently_overwriting()
    {
        // Два одинаковых id — это одна перезаписанная запись и две строки эталона на один файл.
        var exception = Assert.Throws<InvalidOperationException>(() => Parse(
            """{"kind":"line","id":"ru-clean/001","text":"Первая"}""",
            """{"kind":"line","id":"ru-clean/001","text":"Вторая"}"""));

        Assert.Contains("ru-clean/001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Blank_and_commented_lines_are_skipped()
    {
        var script = Parse(
            "",
            "   ",
            "// {\"kind\":\"line\",\"id\":\"ru-clean/009\",\"text\":\"закомментировано\"}",
            """{"kind":"line","id":"ru-clean/001","text":"Живая фраза"}""");

        Assert.Single(script.Lines);
    }

    [Fact]
    public void Recording_resumes_at_the_first_gap()
    {
        var script = Parse(
            """{"kind":"line","id":"ru-clean/001","text":"Первая"}""",
            """{"kind":"line","id":"ru-clean/002","text":"Вторая"}""",
            """{"kind":"line","id":"ru-clean/003","text":"Третья"}""");

        // Пропуск в середине важнее конца: если вернуть на конец, перезаписанная неудачная фраза
        // так и останется недописанной, а начитка выглядит завершённой.
        var recorded = new HashSet<string> { "ru-clean/001", "ru-clean/003" };
        Assert.Equal(1, script.FirstUnrecorded(line => recorded.Contains(line.Id)));
    }

    [Fact]
    public void Fully_recorded_script_reports_position_past_the_end()
    {
        var script = Parse(
            """{"kind":"line","id":"ru-clean/001","text":"Первая"}""",
            """{"kind":"line","id":"ru-clean/002","text":"Вторая"}""");

        Assert.Equal(2, script.FirstUnrecorded(_ => true));
    }

    [Fact]
    public void Reference_contains_only_recorded_lines()
    {
        var script = Parse(
            """{"kind":"line","id":"ru-clean/001","text":"Записана"}""",
            """{"kind":"line","id":"ru-clean/002","text":"Ещё не записана"}""");

        var reference = script.BuildReference(line => line.Id == "ru-clean/001");

        Assert.Contains("Записана", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("Ещё не записана", reference, StringComparison.Ordinal);
    }

    [Fact]
    public void Reference_survives_a_round_trip_through_the_benchmark_loader()
    {
        var script = Parse(
            """{"kind":"set","set":"ru-en-mixed","title":"Смешанная","hint":""}""",
            """{"kind":"line","id":"ru-en-mixed/001","text":"Запушь это в GitHub"}""");

        var directory = Path.Combine(Path.GetTempPath(), "egoist-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, CorpusBenchmark.ReferenceFileName),
                script.BuildReference(_ => true));

            var entry = Assert.Single(CorpusBenchmark.LoadReferences(directory));
            Assert.Equal("ru-en-mixed/001", entry.Id);
            Assert.Equal("ru-en-mixed/001.wav", entry.Audio);
            Assert.Equal("Запушь это в GitHub", entry.Text);
            Assert.Equal(["ru-en-mixed"], entry.Tags);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Reference_line_carries_only_the_four_documented_fields()
    {
        // Set — вычисляемое свойство CorpusEntry, и сериализатор дописывал его пятым полем.
        // Файл вычитывается руками: лишнее поле, способное разойтись с id, — приглашение
        // поправить не то.
        var script = Parse("""{"kind":"line","id":"ru-clean/001","text":"Фраза"}""");

        var line = script.BuildReference(_ => true)
            .Split('\n')
            .First(candidate => candidate.Contains("ru-clean/001", StringComparison.Ordinal));

        using var document = System.Text.Json.JsonDocument.Parse(line);
        Assert.Equal(
            ["id", "audio", "text", "tags"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void Rebuilding_the_reference_never_duplicates_a_re_recorded_line()
    {
        // Перезапись фразы не должна оставлять две строки на один файл: WER считался бы по обеим.
        var script = Parse("""{"kind":"line","id":"ru-clean/001","text":"Единственная"}""");

        var first = script.BuildReference(_ => true);
        var second = script.BuildReference(_ => true);

        Assert.Equal(first, second);
        Assert.Single(
            second.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.Contains("ru-clean/001", StringComparison.Ordinal));
    }

    [Fact]
    public void Versioned_script_rejects_wrong_declared_set_count()
    {
        var exception = Assert.Throws<InvalidDataException>(() => Parse(
            """{"kind":"schema","version":2,"privacy":"private-local-only"}""",
            """{"kind":"set","set":"ru-clean","title":"Обычная","hint":"","expectedCount":2}""",
            """{"kind":"line","id":"ru-clean/001","text":"Одна фраза"}"""));

        Assert.Contains("объявлено 2, найдено 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Versioned_script_requires_private_local_only_policy()
    {
        var exception = Assert.Throws<InvalidDataException>(() => Parse(
            """{"kind":"schema","version":2,"privacy":"public"}""",
            """{"kind":"set","set":"ru-clean","title":"Обычная","hint":"","expectedCount":1}""",
            """{"kind":"line","id":"ru-clean/001","text":"Одна фраза"}"""));

        Assert.Contains("private-local-only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Versioned_script_rejects_unknown_kind_and_unmapped_fields()
    {
        Assert.Throws<InvalidDataException>(() => Parse(
            """{"kind":"schema","version":2,"privacy":"private-local-only"}""",
            """{"kind":"set","set":"ru-clean","title":"Обычная","hint":"","expectedCount":1}""",
            """{"kind":"clip","id":"ru-clean/001","text":"Фраза"}"""));

        Assert.Throws<InvalidDataException>(() => Parse(
            """{"kind":"schema","version":2,"privacy":"private-local-only"}""",
            """{"kind":"set","set":"ru-clean","title":"Обычная","hint":"","expectedCount":1}""",
            """{"kind":"line","id":"ru-clean/001","text":"Фраза","privatePath":"C:\\\\voice.wav"}"""));
    }

    [Fact]
    public void Unsafe_or_absolute_ids_are_rejected()
    {
        var exception = Assert.Throws<InvalidDataException>(() => Parse(
            """{"kind":"line","id":"../private/001","text":"Фраза"}"""));

        Assert.Contains("corpus id", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reference_preserves_private_annotations_under_a_versioned_manifest()
    {
        var script = Parse(
            """{"kind":"schema","version":2,"privacy":"private-local-only"}""",
            """{"kind":"set","set":"ru-en","title":"Смешанная","hint":"","expectedCount":1}""",
            """{"kind":"line","id":"ru-en/001","text":"антропик","expected":"Anthropic","tags":["mixed"],"entities":["Anthropic"],"translationCommand":false,"boundary":"end","boundaryTarget":"Anthropic"}""");

        var reference = script.BuildReference(_ => true);

        Assert.Contains("corpus-reference", reference, StringComparison.Ordinal);
        Assert.Contains(script.Fingerprint, reference, StringComparison.Ordinal);
        Assert.Contains("\"entities\":[\"Anthropic\"]", reference, StringComparison.Ordinal);
        Assert.Contains("\"translationCommand\":false", reference, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_script_is_valid_and_every_line_has_a_set_header()
    {
        var path = Path.Combine(RepositoryRoot(), "tests", "corpus", CorpusScript.FileName);
        Assert.True(File.Exists(path), $"Не найден {path}");

        var script = CorpusScript.Load(Path.GetDirectoryName(path)!);

        Assert.NotEmpty(script.Lines);
        Assert.Equal(CorpusScript.CurrentSchemaVersion, script.SchemaVersion);
        Assert.Equal(CorpusScript.PrivateDataPolicy, script.Privacy);
        Assert.Equal(350, script.Lines.Count);
        Assert.Equal(109, script.Lines.Sum(line => line.Entities.Count));
        Assert.Equal(40, script.Lines.Count(line => line.TranslationCommandExpected is true));
        Assert.Equal(80, script.Lines.Count(line => line.TranslationCommandExpected is false));
        Assert.Matches("^[0-9a-f]{64}$", script.Fingerprint);
        foreach (var line in script.Lines)
        {
            Assert.True(
                script.Sets.ContainsKey(line.Set),
                $"У набора {line.Set} нет заголовка, фраза {line.Id} останется без подсказки.");
            Assert.False(string.IsNullOrWhiteSpace(line.Text), $"Пустой текст у {line.Id}");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Egoist.Voice.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
