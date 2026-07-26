using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class DictationDeliveryServiceTests
{
    [Fact]
    public async Task Copies_and_inserts_transcript()
    {
        var clipboard = new FakeClipboard();
        var insertion = new FakeInsertion();
        var service = new DictationDeliveryService(clipboard, insertion);

        var result = await service.DeliverAsync("Привет, мир!", 42, CancellationToken.None);

        Assert.Equal(DictationDeliveryStatus.Inserted, result.Status);
        Assert.Equal(["Привет, мир!"], clipboard.Values);
        Assert.Equal(clipboard.Values, insertion.Values);
    }

    [Fact]
    public async Task Keeps_clipboard_ready_when_active_window_is_lost()
    {
        var clipboard = new FakeClipboard();
        var insertion = new FakeInsertion { Fail = true };
        var service = new DictationDeliveryService(clipboard, insertion);

        var result = await service.DeliverAsync("Текст", 42, CancellationToken.None);

        Assert.Equal(DictationDeliveryStatus.ClipboardFallback, result.Status);
        Assert.Equal(["Текст"], clipboard.Values);
    }

    [Fact]
    public async Task Never_inserts_when_clipboard_copy_failed()
    {
        var clipboard = new FakeClipboard { Fail = true };
        var insertion = new FakeInsertion();
        var service = new DictationDeliveryService(clipboard, insertion);

        var result = await service.DeliverAsync("Текст", 42, CancellationToken.None);

        Assert.Equal(DictationDeliveryStatus.ClipboardFailed, result.Status);
        Assert.Empty(insertion.Values);
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public List<string> Values { get; } = [];
        public bool Fail { get; init; }
        public Task CopyAsync(string text, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("busy");
            Values.Add(text);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInsertion : ITextInsertionService
    {
        public List<string> Values { get; } = [];
        public bool Fail { get; init; }
        public Task InsertAsync(string text, nint targetWindow, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("window lost");
            Values.Add(text);
            return Task.CompletedTask;
        }
    }
}
