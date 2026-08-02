using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Egoist.Voice.Core;

/// <summary>Заголовок набора: показывается один раз, когда начинается его начитка.</summary>
public sealed record CorpusScriptSet(
    string Name,
    string Title,
    string Hint,
    int? ExpectedCount = null);

/// <summary>Одна фраза скрипта. <see cref="Audio"/> — путь относительно корня корпуса.</summary>
public sealed record CorpusScriptLine(
    string Id,
    string Set,
    string Text,
    string ReferenceText,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Entities,
    bool? TranslationCommandExpected,
    string? Boundary,
    string? BoundaryTarget)
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
    public const int CurrentSchemaVersion = 2;
    public const string PrivateDataPolicy = "private-local-only";

    private static readonly Regex SafeId = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*/[0-9]{3}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private CorpusScript(
        IReadOnlyList<CorpusScriptLine> lines,
        IReadOnlyDictionary<string, CorpusScriptSet> sets,
        int schemaVersion,
        string privacy,
        bool schemaDeclared)
    {
        Lines = lines;
        Sets = sets;
        SchemaVersion = schemaVersion;
        Privacy = privacy;
        SchemaDeclared = schemaDeclared;
        Fingerprint = ComputeFingerprint(schemaVersion, privacy, lines, sets);
    }

    public IReadOnlyList<CorpusScriptLine> Lines { get; }

    public IReadOnlyDictionary<string, CorpusScriptSet> Sets { get; }

    public int SchemaVersion { get; }

    public string Privacy { get; }

    public bool SchemaDeclared { get; }

    /// <summary>SHA-256 over schema, set declarations and ordered prompts/expectations.</summary>
    public string Fingerprint { get; }

    public const string FileName = "script.jsonl";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,

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
        var schemaVersion = 1;
        var privacy = "legacy-unspecified";
        var schemaDeclared = false;
        var contentSeen = false;
        var lineNumber = 0;

        foreach (var raw in rawLines)
        {
            lineNumber++;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            ScriptNode? node;
            try
            {
                node = JsonSerializer.Deserialize<ScriptNode>(trimmed, Json);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Некорректный JSON в {FileName}, строка {lineNumber}.", exception);
            }
            if (node is null)
            {
                continue;
            }

            if (string.Equals(node.Kind, "schema", StringComparison.Ordinal))
            {
                if (schemaDeclared || contentSeen)
                {
                    throw new InvalidDataException("Schema manifest должен быть первой содержательной строкой и встречаться один раз.");
                }
                schemaVersion = node.Version ?? 0;
                privacy = node.Privacy?.Trim() ?? string.Empty;
                schemaDeclared = true;
                continue;
            }

            contentSeen = true;
            if (string.Equals(node.Kind, "set", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(node.Set))
                {
                    throw new InvalidDataException($"Пустое имя набора в {FileName}, строка {lineNumber}.");
                }

                currentSet = node.Set.Trim();
                ValidateSetName(currentSet);
                if (sets.ContainsKey(currentSet))
                {
                    throw new InvalidDataException($"Дубль набора в скрипте: {currentSet}");
                }
                if (node.ExpectedCount is <= 0)
                {
                    throw new InvalidDataException($"Набор {currentSet}: expectedCount должен быть больше нуля.");
                }

                sets[currentSet] = new CorpusScriptSet(
                    currentSet,
                    node.Title?.Trim() ?? currentSet,
                    node.Hint?.Trim() ?? string.Empty,
                    node.ExpectedCount);
                continue;
            }

            if ((schemaDeclared && !string.Equals(node.Kind, "line", StringComparison.Ordinal)) ||
                (!schemaDeclared && !string.IsNullOrWhiteSpace(node.Kind) &&
                 !string.Equals(node.Kind, "line", StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"Неизвестный kind '{node.Kind ?? "<empty>"}' в {FileName}, строка {lineNumber}.");
            }

            if (string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.Text))
            {
                throw new InvalidDataException($"Строка корпуса {lineNumber} должна содержать id и text.");
            }

            ValidateId(node.Id);

            // Набор определяется по первому сегменту id, а не по последнему увиденному заголовку:
            // так строка, случайно оказавшаяся не в своём месте, попадёт туда, куда указывает её
            // же имя файла, и отчёт не разъедется с содержимым каталога.
            var set = SetOf(node.Id);
            if (set.Length == 0)
            {
                set = currentSet;
            }
            if (schemaDeclared && !sets.ContainsKey(set))
            {
                throw new InvalidDataException($"У фразы {node.Id} нет объявленного set-заголовка {set}.");
            }

            // Дубль id означал бы, что одна запись перезапишет другую, а в reference.jsonl
            // появятся две строки на один файл. Тихо пропустить хуже, чем не собрать корпус.
            if (!seen.Add(node.Id))
            {
                throw new InvalidOperationException($"Дубль id в скрипте: {node.Id}");
            }

            var tags = NormalizeList(node.Tags, fallback: set.Length == 0 ? [] : [set]);
            var entities = NormalizeList(node.Entities, fallback: []);
            var boundary = node.Boundary?.Trim().ToLowerInvariant();
            if (boundary is not null && boundary is not ("start" or "end"))
            {
                throw new InvalidDataException($"Фраза {node.Id}: boundary допускает только start/end.");
            }
            if (boundary is not null && string.IsNullOrWhiteSpace(node.BoundaryTarget))
            {
                throw new InvalidDataException($"Фраза {node.Id}: для boundary нужен boundaryTarget.");
            }

            lines.Add(new CorpusScriptLine(
                node.Id,
                set,
                node.Text.Trim(),
                string.IsNullOrWhiteSpace(node.Expected) ? node.Text.Trim() : node.Expected.Trim(),
                tags,
                entities,
                node.TranslationCommand,
                boundary,
                node.BoundaryTarget?.Trim()));
        }

        if (schemaDeclared)
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Неподдерживаемая schema version {schemaVersion}; требуется {CurrentSchemaVersion}.");
            }
            if (!string.Equals(privacy, PrivateDataPolicy, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Schema должна явно объявлять privacy={PrivateDataPolicy}.");
            }

            foreach (var set in sets.Values)
            {
                var actual = lines.Count(line => string.Equals(line.Set, set.Name, StringComparison.Ordinal));
                if (set.ExpectedCount is null || actual != set.ExpectedCount.Value)
                {
                    throw new InvalidDataException(
                        $"Набор {set.Name}: объявлено {set.ExpectedCount?.ToString() ?? "нет"}, найдено {actual} фраз.");
                }
            }
        }

        return new CorpusScript(lines, sets, schemaVersion, privacy, schemaDeclared);
    }

    public static CorpusScript Load(string corpusDirectory)
    {
        var path = Path.Combine(corpusDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Не найден {FileName} в {corpusDirectory}. См. tests/corpus/README.md.", path);
        }

        var script = Parse(File.ReadLines(path));
        if (!script.SchemaDeclared)
        {
            throw new InvalidDataException($"{FileName} не содержит обязательный schema manifest.");
        }
        return script;
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

            var entry = new CorpusEntry(
                line.Id,
                line.Audio,
                line.ReferenceText,
                line.Tags,
                line.Entities.Count == 0 ? null : line.Entities,
                line.TranslationCommandExpected,
                line.Boundary,
                line.BoundaryTarget);
            builder.AppendLine(JsonSerializer.Serialize(entry, Json));
        }

        var manifest = new CorpusReferenceManifest(
            "corpus-reference",
            SchemaVersion,
            Privacy,
            Fingerprint);
        var header = JsonSerializer.Serialize(manifest, Json) + Environment.NewLine;
        return header + builder;
    }

    public string BuildReference(string corpusDirectory) =>
        BuildReference(line => File.Exists(Path.Combine(corpusDirectory, line.Audio)));

    public static void ValidateId(string id)
    {
        if (Path.IsPathRooted(id) || id.Contains('\\') || id.Contains("..", StringComparison.Ordinal) ||
            !SafeId.IsMatch(id))
        {
            throw new InvalidDataException($"Небезопасный или некорректный corpus id: {id}");
        }
    }

    private static void ValidateSetName(string set)
    {
        if (!Regex.IsMatch(set, "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException($"Некорректное имя набора: {set}");
        }
    }

    private static IReadOnlyList<string> NormalizeList(
        IReadOnlyList<string>? values,
        IReadOnlyList<string> fallback)
    {
        var normalized = (values ?? fallback)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static string ComputeFingerprint(
        int schemaVersion,
        string privacy,
        IReadOnlyList<CorpusScriptLine> lines,
        IReadOnlyDictionary<string, CorpusScriptSet> sets)
    {
        var canonical = new StringBuilder();
        canonical.Append(schemaVersion).Append('\u001f').Append(privacy).AppendLine();
        foreach (var set in sets.Values.OrderBy(set => set.Name, StringComparer.Ordinal))
        {
            canonical.Append(set.Name).Append('\u001f')
                .Append(set.Title).Append('\u001f')
                .Append(set.Hint).Append('\u001f')
                .Append(set.ExpectedCount).AppendLine();
        }
        foreach (var line in lines)
        {
            canonical.Append(line.Id).Append('\u001f')
                .Append(line.Text).Append('\u001f')
                .Append(line.ReferenceText).Append('\u001f')
                .AppendJoin('\u001e', line.Tags).Append('\u001f')
                .AppendJoin('\u001e', line.Entities).Append('\u001f')
                .Append(line.TranslationCommandExpected).Append('\u001f')
                .Append(line.Boundary).Append('\u001f')
                .Append(line.BoundaryTarget).AppendLine();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private sealed record ScriptNode(
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("version")] int? Version,
        [property: JsonPropertyName("privacy")] string? Privacy,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("set")] string? Set,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("expected")] string? Expected,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("hint")] string? Hint,
        [property: JsonPropertyName("expectedCount")] int? ExpectedCount,
        [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags,
        [property: JsonPropertyName("entities")] IReadOnlyList<string>? Entities,
        [property: JsonPropertyName("translationCommand")] bool? TranslationCommand,
        [property: JsonPropertyName("boundary")] string? Boundary,
        [property: JsonPropertyName("boundaryTarget")] string? BoundaryTarget);
}
