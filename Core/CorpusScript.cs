using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egoist.Voice.Core;

/// <summary>Заголовок набора: показывается один раз, когда начинается его начитка.</summary>
public sealed record CorpusScriptSet(string Name, string Title, string Hint);

/// <summary>Одна фраза скрипта. <see cref="Audio"/> — путь относительно корня корпуса.</summary>
public sealed record CorpusScriptLine(string Id, string Set, string Text)
{
    public string Audio => Id + ".wav";
}

/// <summary>
/// Скрипт начитки корпуса: последовательность фраз с заголовками наборов.
///
/// Отдельный тип, а не пара списков, потому что здесь живёт вся логика, которую нужно проверять
/// тестами: разбор, поиск первой незаписанной фразы и построение <c>reference.jsonl</c>.
/// Окно записи остаётся тонким и занимается только звуком и клавишами.
/// </summary>
public sealed class CorpusScript
{
    private CorpusScript(IReadOnlyList<CorpusScriptLine> lines, IReadOnlyDictionary<string, CorpusScriptSet> sets)
    {
        Lines = lines;
        Sets = sets;
    }

    public IReadOnlyList<CorpusScriptLine> Lines { get; }

    public IReadOnlyDictionary<string, CorpusScriptSet> Sets { get; }

    public const string FileName = "script.jsonl";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // По умолчанию System.Text.Json экранирует кириллицу в \uXXXX. Для reference.jsonl это
        // недопустимо: файл вычитывается глазами, а один незамеченный битый эталон сдвигает WER
        // на пять пунктов. Нечитаемый эталон никто не вычитает.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static CorpusScript Parse(IEnumerable<string> rawLines)
    {
        var lines = new List<CorpusScriptLine>();
        var sets = new Dictionary<string, CorpusScriptSet>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var currentSet = string.Empty;

        foreach (var raw in rawLines)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var node = JsonSerializer.Deserialize<ScriptNode>(trimmed, Json);
            if (node is null)
            {
                continue;
            }

            if (string.Equals(node.Kind, "set", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(node.Set))
                {
                    continue;
                }

                currentSet = node.Set;
                sets[currentSet] = new CorpusScriptSet(currentSet, node.Title ?? currentSet, node.Hint ?? string.Empty);
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.Text))
            {
                continue;
            }

            // Набор определяется по первому сегменту id, а не по последнему увиденному заголовку:
            // так строка, случайно оказавшаяся не в своём месте, попадёт туда, куда указывает её
            // же имя файла, и отчёт не разъедется с содержимым каталога.
            var set = SetOf(node.Id);
            if (set.Length == 0)
            {
                set = currentSet;
            }

            // Дубль id означал бы, что одна запись перезапишет другую, а в reference.jsonl
            // появятся две строки на один файл. Тихо пропустить хуже, чем не собрать корпус.
            if (!seen.Add(node.Id))
            {
                throw new InvalidOperationException($"Дубль id в скрипте: {node.Id}");
            }

            lines.Add(new CorpusScriptLine(node.Id, set, node.Text.Trim()));
        }

        return new CorpusScript(lines, sets);
    }

    public static CorpusScript Load(string corpusDirectory)
    {
        var path = Path.Combine(corpusDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Не найден {FileName} в {corpusDirectory}. См. tests/corpus/README.md.", path);
        }

        return Parse(File.ReadLines(path));
    }

    public static string SetOf(string id)
    {
        var slash = id.IndexOf('/');
        return slash <= 0 ? string.Empty : id[..slash];
    }

    /// <summary>
    /// Индекс первой фразы, для которой ещё нет аудио. Начитка длинная, её бросают и возвращаются;
    /// без этого пришлось бы каждый раз проматывать записанное вручную.
    /// Возвращает длину списка, когда записано всё.
    /// </summary>
    public int FirstUnrecorded(Func<CorpusScriptLine, bool> isRecorded)
    {
        for (var index = 0; index < Lines.Count; index++)
        {
            if (!isRecorded(Lines[index]))
            {
                return index;
            }
        }

        return Lines.Count;
    }

    public int FirstUnrecorded(string corpusDirectory) =>
        FirstUnrecorded(line => File.Exists(Path.Combine(corpusDirectory, line.Audio)));

    public int RecordedCount(string corpusDirectory) =>
        Lines.Count(line => File.Exists(Path.Combine(corpusDirectory, line.Audio)));

    /// <summary>
    /// Строит содержимое <c>reference.jsonl</c> по фразам, аудио для которых уже записано.
    ///
    /// Файл перестраивается целиком на каждое сохранение, а не дописывается: дописывание после
    /// перезаписи фразы оставило бы две строки на один файл, и WER считался бы по обеим.
    /// </summary>
    public string BuildReference(Func<CorpusScriptLine, bool> isRecorded)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// Сгенерировано режимом --corpus-record. Правьте вручную, если что-то прочитано не так,");
        builder.AppendLine("// как написано в script.jsonl: эталон должен соответствовать голосу, а не скрипту.");

        foreach (var line in Lines)
        {
            if (!isRecorded(line))
            {
                continue;
            }

            var entry = new CorpusEntry(line.Id, line.Audio, line.Text, [line.Set]);
            builder.AppendLine(JsonSerializer.Serialize(entry, Json));
        }

        return builder.ToString();
    }

    public string BuildReference(string corpusDirectory) =>
        BuildReference(line => File.Exists(Path.Combine(corpusDirectory, line.Audio)));

    private sealed record ScriptNode(
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("set")] string? Set,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("hint")] string? Hint);
}
