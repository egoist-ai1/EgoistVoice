using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Egoist.Voice.Core;

/// <summary>
/// Public, text-only selection of corpus IDs used for a focused benchmark. The profile never
/// contains audio, references or hypotheses; several buckets may point at the same clip.
/// </summary>
public sealed class CorpusBenchmarkProfile
{
    public const string CurrentSchema = "egoist.voice.corpus-profile/v1";

    private static readonly Regex SafeName = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _bucketsById;

    private CorpusBenchmarkProfile(
        string id,
        IReadOnlyDictionary<string, IReadOnlyList<string>> buckets,
        IReadOnlyList<string> selectedIds,
        string fingerprint)
    {
        Id = id;
        Buckets = buckets;
        SelectedIds = selectedIds;
        Fingerprint = fingerprint;
        _bucketsById = selectedIds.ToDictionary(
            clipId => clipId,
            clipId => (IReadOnlyList<string>)buckets
                .Where(bucket => bucket.Value.Contains(clipId, StringComparer.Ordinal))
                .Select(bucket => bucket.Key)
                .OrderBy(bucket => bucket, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
    }

    public string Id { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Buckets { get; }

    public IReadOnlyList<string> SelectedIds { get; }

    public string Fingerprint { get; }

    public IReadOnlyList<string> BucketsFor(string clipId) =>
        _bucketsById.TryGetValue(clipId, out var buckets) ? buckets : [];

    public static CorpusBenchmarkProfile Load(string path, CorpusScript script)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Не найден corpus benchmark profile.", path);
        }

        ProfileDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("Пустой corpus benchmark profile.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Некорректный JSON corpus benchmark profile.", exception);
        }

        if (!string.Equals(document.Schema, CurrentSchema, StringComparison.Ordinal) ||
            !IsSafeName(document.Id) ||
            document.Buckets is null ||
            document.Buckets.Count == 0)
        {
            throw new InvalidDataException("Corpus benchmark profile имеет неверные schema/id/buckets.");
        }

        var knownIds = script.Lines.Select(line => line.Id).ToHashSet(StringComparer.Ordinal);
        var normalized = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (bucket, rawIds) in document.Buckets)
        {
            if (!IsSafeName(bucket) || rawIds is null || rawIds.Count == 0)
            {
                throw new InvalidDataException($"Некорректный или пустой corpus bucket: {bucket}");
            }

            var ids = rawIds
                .Select(id => id?.Trim() ?? string.Empty)
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (ids.Length != rawIds.Count)
            {
                throw new InvalidDataException($"Corpus bucket {bucket} содержит пустые или дублирующиеся id.");
            }
            foreach (var id in ids)
            {
                CorpusScript.ValidateId(id);
                if (!knownIds.Contains(id))
                {
                    throw new InvalidDataException($"Corpus profile ссылается на неизвестный id: {id}");
                }
            }
            normalized[bucket] = ids;
        }

        var selected = script.Lines
            .Select(line => line.Id)
            .Where(id => normalized.Values.Any(ids => ids.Contains(id, StringComparer.Ordinal)))
            .ToArray();
        var fingerprint = ComputeFingerprint(document.Id, normalized);
        return new CorpusBenchmarkProfile(document.Id, normalized, selected, fingerprint);
    }

    private static bool IsSafeName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && SafeName.IsMatch(value);

    private static string ComputeFingerprint(
        string id,
        IReadOnlyDictionary<string, IReadOnlyList<string>> buckets)
    {
        var canonical = new StringBuilder(CurrentSchema).Append('\u001f').Append(id).AppendLine();
        foreach (var (bucket, ids) in buckets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            canonical.Append(bucket).Append('\u001f').AppendJoin('\u001e', ids).AppendLine();
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private sealed record ProfileDocument(
        [property: JsonPropertyName("schema")] string Schema,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("buckets")] IReadOnlyDictionary<string, IReadOnlyList<string>> Buckets);
}
