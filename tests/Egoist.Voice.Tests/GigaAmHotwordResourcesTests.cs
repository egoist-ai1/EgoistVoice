using System.Text;
using Egoist.Voice.Core;

namespace Egoist.Voice.Tests;

public sealed class GigaAmHotwordResourcesTests
{
    [Fact]
    public void PublicHotwordsAreStableDistinctAndExcludeAmbiguousAliases()
    {
        var phrases = GigaAmHotwordResources.BuildPhrases();

        Assert.Contains("антропик", phrases);
        Assert.Contains("клод код", phrases);
        Assert.DoesNotContain("клауд", phrases);
        Assert.DoesNotContain("клауд код", phrases);
        Assert.DoesNotContain("кодекс", phrases);
        Assert.DoesNotContain("курсор", phrases);
        Assert.DoesNotContain("мета", phrases);
        Assert.DoesNotContain("стим", phrases);
        Assert.Equal(phrases.Count, phrases.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(phrases.OrderBy(value => value, StringComparer.Ordinal), phrases);
    }

    [Fact]
    public void MinimalSentencePieceReaderPreservesPiecesAndScores()
    {
        var model = BuildModel(("<unk>", 0f), ("▁антропик", -1.25f), ("а", -2.5f));

        var vocabulary = SentencePieceModelReader.ReadVocabulary(model);

        Assert.Collection(
            vocabulary,
            item => Assert.Equal(new SentencePieceEntry("<unk>", 0f), item),
            item => Assert.Equal(new SentencePieceEntry("▁антропик", -1.25f), item),
            item => Assert.Equal(new SentencePieceEntry("а", -2.5f), item));
    }

    [Fact]
    public void PrepareRejectsTokenizerThatDoesNotExactlyMatchRecognizerIds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "egoist-voice-hotword-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var modelPath = Path.Combine(directory, "tokenizer.model");
            var tokensPath = Path.Combine(directory, "tokens.txt");
            File.WriteAllBytes(modelPath, BuildModel(("<unk>", 0f), ("а", -1f)));
            File.WriteAllText(tokensPath, "<unk> 0\nб 1\n", new UTF8Encoding(false));

            Assert.Throws<InvalidDataException>(() => GigaAmHotwordResources.Prepare(modelPath, tokensPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PrepareWritesDeterministicBpeAndHotwordFilesForCompatibleVocabulary()
    {
        var directory = Path.Combine(Path.GetTempPath(), "egoist-voice-hotword-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var modelPath = Path.Combine(directory, "tokenizer.model");
            var tokensPath = Path.Combine(directory, "tokens.txt");
            File.WriteAllBytes(modelPath, BuildModel(("<unk>", 0f), ("▁антропик", -1.25f), ("а", -2.5f)));
            File.WriteAllText(tokensPath, "<unk> 0\n▁антропик 1\nа 2\n", new UTF8Encoding(false));

            var first = GigaAmHotwordResources.Prepare(modelPath, tokensPath);
            var bpeBefore = File.ReadAllBytes(first.BpeVocabularyPath);
            var hotwordsBefore = File.ReadAllBytes(first.HotwordsPath);
            var second = GigaAmHotwordResources.Prepare(modelPath, tokensPath);

            Assert.Equal(bpeBefore, File.ReadAllBytes(second.BpeVocabularyPath));
            Assert.Equal(hotwordsBefore, File.ReadAllBytes(second.HotwordsPath));
            Assert.Contains("▁антропик\t-1.25", File.ReadAllText(first.BpeVocabularyPath));
            Assert.Contains("антропик :1.15", File.ReadAllText(first.HotwordsPath));
            Assert.True(first.PhraseCount > 20);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PrepareAcceptsRecognizerOnlyTransducerBlankAtFinalId()
    {
        var directory = Path.Combine(Path.GetTempPath(), "egoist-voice-hotword-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var modelPath = Path.Combine(directory, "tokenizer.model");
            var tokensPath = Path.Combine(directory, "tokens.txt");
            File.WriteAllBytes(modelPath, BuildModel(("<unk>", 0f), ("а", -1f)));
            File.WriteAllText(tokensPath, "<unk> 0\nа 1\n<blk> 2\n", new UTF8Encoding(false));

            var prepared = GigaAmHotwordResources.Prepare(modelPath, tokensPath);

            Assert.True(File.Exists(prepared.BpeVocabularyPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TruncatedSentencePieceModelFailsClosed()
    {
        Assert.Throws<InvalidDataException>(() => SentencePieceModelReader.ReadVocabulary(new byte[] { 0x0A, 0x7F, 0x01 }));
    }

    private static byte[] BuildModel(params (string Piece, float Score)[] entries)
    {
        using var model = new MemoryStream();
        foreach (var entry in entries)
        {
            using var message = new MemoryStream();
            var text = Encoding.UTF8.GetBytes(entry.Piece);
            message.WriteByte(0x0A);
            WriteVarint(message, (ulong)text.Length);
            message.Write(text);
            message.WriteByte(0x15);
            message.Write(BitConverter.GetBytes(entry.Score));

            var bytes = message.ToArray();
            model.WriteByte(0x0A);
            WriteVarint(model, (ulong)bytes.Length);
            model.Write(bytes);
        }
        return model.ToArray();
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }
}
