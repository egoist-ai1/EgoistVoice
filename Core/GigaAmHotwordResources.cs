using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace Egoist.Voice.Core;

internal sealed record GigaAmHotwordFiles(string BpeVocabularyPath, string HotwordsPath, int PhraseCount);

/// <summary>
/// Builds only deterministic public vocabulary resources. It never consumes recognized/user text.
/// Any tokenizer mismatch fails closed and the caller keeps baseline GigaAM decoding.
/// </summary>
internal static class GigaAmHotwordResources
{
    internal const string Version = "3";
    internal const float GlobalScore = 1.15f;

    internal static GigaAmHotwordFiles Prepare(string tokenizerModelPath, string tokensPath)
    {
        var directory = Path.GetDirectoryName(tokenizerModelPath)
            ?? throw new InvalidDataException("Tokenizer path has no directory.");
        var modelPieces = SentencePieceModelReader.ReadVocabulary(tokenizerModelPath);
        var recognizerPieces = ReadRecognizerPieces(tokensPath);
        ValidateCompatibility(modelPieces, recognizerPieces);

        var bpePath = Path.Combine(directory, "gigaam-v3-e2e.bpe.vocab");
        var hotwordsPath = Path.Combine(directory, $"egoist-hotwords-v{Version}.txt");
        WriteAtomicallyIfChanged(
            bpePath,
            modelPieces.Select(piece => $"{piece.Piece}\t{piece.Score.ToString("R", CultureInfo.InvariantCulture)}"));

        var phrases = BuildPhrases();
        WriteAtomicallyIfChanged(
            hotwordsPath,
            phrases.Select(phrase => $"{phrase} :{GlobalScore.ToString("R", CultureInfo.InvariantCulture)}"));
        return new GigaAmHotwordFiles(bpePath, hotwordsPath, phrases.Count);
    }

    internal static IReadOnlyList<string> BuildPhrases()
    {
        // Contextual bias is intentionally narrower than deterministic post-repair. Ambiguous
        // aliases such as «клауд» and ordinary Russian words are never boosted globally.
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "клауд", "клауд код", "кодекс"
        };
        return BuiltInVocabulary.Terms
            .Where(term => (term.Profiles & EntityProfile.General) != 0)
            .SelectMany(term => term.Spoken ?? [])
            .Select(NormalizePhrase)
            .Where(phrase => phrase.Length >= 3 && !blocked.Contains(phrase))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(phrase => phrase, StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizePhrase(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split(
            [' ', '\t', '\r', '\n', '-'], StringSplitOptions.RemoveEmptyEntries));

    private static IReadOnlyList<string> ReadRecognizerPieces(string path)
    {
        var indexed = new SortedDictionary<int, string>();
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            var separator = line.LastIndexOf(' ');
            if (separator <= 0 || !int.TryParse(line[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                throw new InvalidDataException("GigaAM tokens contain an invalid row.");
            }
            if (!indexed.TryAdd(id, line[..separator]))
            {
                throw new InvalidDataException("GigaAM tokens contain a duplicate id.");
            }
        }
        if (indexed.Count == 0 || indexed.Keys.First() != 0 || indexed.Keys.Last() != indexed.Count - 1)
        {
            throw new InvalidDataException("GigaAM token ids are not contiguous.");
        }
        return indexed.Values.ToArray();
    }

    private static void ValidateCompatibility(
        IReadOnlyList<SentencePieceEntry> modelPieces,
        IReadOnlyList<string> recognizerPieces)
    {
        var hasTransducerBlank = recognizerPieces.Count == modelPieces.Count + 1 &&
            string.Equals(recognizerPieces[^1], "<blk>", StringComparison.Ordinal);
        if (modelPieces.Count != recognizerPieces.Count && !hasTransducerBlank)
        {
            throw new InvalidDataException(
                $"Tokenizer/token count mismatch: {modelPieces.Count} != {recognizerPieces.Count}.");
        }
        for (var index = 0; index < modelPieces.Count; index++)
        {
            if (!string.Equals(modelPieces[index].Piece, recognizerPieces[index], StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Tokenizer mismatch at id {index}.");
            }
        }
    }

    private static void WriteAtomicallyIfChanged(string path, IEnumerable<string> lines)
    {
        var content = string.Join('\n', lines) + "\n";
        if (File.Exists(path) && string.Equals(File.ReadAllText(path, Encoding.UTF8), content, StringComparison.Ordinal))
        {
            return;
        }
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }
}

internal sealed record SentencePieceEntry(string Piece, float Score);

internal static class SentencePieceModelReader
{
    internal static IReadOnlyList<SentencePieceEntry> ReadVocabulary(string path) =>
        ReadVocabulary(File.ReadAllBytes(path));

    internal static IReadOnlyList<SentencePieceEntry> ReadVocabulary(ReadOnlySpan<byte> model)
    {
        var result = new List<SentencePieceEntry>();
        var offset = 0;
        while (offset < model.Length)
        {
            var tag = ReadVarint(model, ref offset);
            var field = (int)(tag >> 3);
            var wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
            {
                var length = checked((int)ReadVarint(model, ref offset));
                EnsureAvailable(model, offset, length);
                result.Add(ReadPiece(model.Slice(offset, length)));
                offset += length;
            }
            else
            {
                Skip(model, ref offset, wire);
            }
        }
        if (result.Count == 0)
        {
            throw new InvalidDataException("SentencePiece model has no vocabulary.");
        }
        return result;
    }

    private static SentencePieceEntry ReadPiece(ReadOnlySpan<byte> message)
    {
        string? piece = null;
        float? score = null;
        var offset = 0;
        while (offset < message.Length)
        {
            var tag = ReadVarint(message, ref offset);
            var field = (int)(tag >> 3);
            var wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
            {
                var length = checked((int)ReadVarint(message, ref offset));
                EnsureAvailable(message, offset, length);
                piece = Encoding.UTF8.GetString(message.Slice(offset, length));
                offset += length;
            }
            else if (field == 2 && wire == 5)
            {
                EnsureAvailable(message, offset, 4);
                score = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(message[offset..]));
                offset += 4;
            }
            else
            {
                Skip(message, ref offset, wire);
            }
        }
        if (piece is null || score is null || !float.IsFinite(score.Value))
        {
            throw new InvalidDataException("SentencePiece vocabulary entry is incomplete.");
        }
        return new SentencePieceEntry(piece, score.Value);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            EnsureAvailable(data, offset, 1);
            var current = data[offset++];
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }
        throw new InvalidDataException("Invalid protobuf varint.");
    }

    private static void Skip(ReadOnlySpan<byte> data, ref int offset, int wire)
    {
        switch (wire)
        {
            case 0:
                _ = ReadVarint(data, ref offset);
                break;
            case 1:
                EnsureAvailable(data, offset, 8);
                offset += 8;
                break;
            case 2:
                var length = checked((int)ReadVarint(data, ref offset));
                EnsureAvailable(data, offset, length);
                offset += length;
                break;
            case 5:
                EnsureAvailable(data, offset, 4);
                offset += 4;
                break;
            default:
                throw new InvalidDataException($"Unsupported protobuf wire type {wire}.");
        }
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException("Truncated SentencePiece model.");
        }
    }
}
