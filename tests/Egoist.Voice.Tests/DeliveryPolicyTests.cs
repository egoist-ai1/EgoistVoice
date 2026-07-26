using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class DeliveryPolicyTests
{
    [Theory]
    [InlineData("notepad", PasteMethod.ControlV)]
    [InlineData("chrome", PasteMethod.ControlV)]
    [InlineData("Code", PasteMethod.ControlV)]
    [InlineData("WindowsTerminal", PasteMethod.ControlShiftV)]
    [InlineData("powershell", PasteMethod.ControlShiftV)]
    [InlineData("pwsh.exe", PasteMethod.ControlShiftV)]
    [InlineData("wezterm-gui", PasteMethod.ControlShiftV)]
    [InlineData("mintty", PasteMethod.ControlShiftV)]
    public void Console_hosts_get_the_shortcut_they_actually_listen_for(string process, PasteMethod expected) =>
        Assert.Equal(expected, PasteTargetPolicy.Resolve(process).Method);

    [Theory]
    [InlineData("1Password")]
    [InlineData("bitwarden")]
    [InlineData("KeePassXC")]
    [InlineData("LogonUI")]
    public void Password_managers_are_marked_sensitive(string process)
    {
        var decision = PasteTargetPolicy.Resolve(process);

        Assert.True(decision.IsSensitive);
        Assert.Equal(PasteMethod.ClipboardOnly, decision.Method);
    }

    [Fact]
    public void An_unknown_target_falls_back_to_the_universal_shortcut() =>
        Assert.Equal(PasteMethod.ControlV, PasteTargetPolicy.Resolve(null).Method);

    [Theory]
    [InlineData(PasteMethod.ControlV, 4)]
    [InlineData(PasteMethod.ControlShiftV, 6)]
    [InlineData(PasteMethod.ShiftInsert, 4)]
    public void Every_paste_method_emits_balanced_key_events(PasteMethod method, int expected) =>
        Assert.Equal(expected, TextInsertionService.PasteEventCount(method));

    [Fact]
    public async Task Delivery_restores_the_clipboard_after_a_successful_paste()
    {
        var clipboard = new FakeRestorableClipboard();
        var service = new DictationDeliveryService(clipboard, new FakeInsertion());

        var result = await service.DeliverAsync("Привет", 0, CancellationToken.None);

        Assert.Equal(DictationDeliveryStatus.Inserted, result.Status);
        Assert.True(result.ClipboardRestoreScheduled);
        Assert.True(clipboard.SnapshotRequested);
        Assert.Equal(1, await clipboard.WaitForRestoreAsync());
    }

    [Fact]
    public async Task Restore_does_not_delay_the_result()
    {
        // The settle delay is real but it must not sit in the user's critical path: at a p95 budget
        // of 180 ms, waiting 120 ms for a clipboard restore would be most of the budget.
        var clipboard = new FakeRestorableClipboard();
        var service = new DictationDeliveryService(clipboard, new FakeInsertion());

        var started = System.Diagnostics.Stopwatch.StartNew();
        await service.DeliverAsync("Привет", 0, CancellationToken.None);
        started.Stop();

        Assert.True(started.ElapsedMilliseconds < 100, $"Доставка заняла {started.ElapsedMilliseconds} мс.");
    }

    [Fact]
    public async Task Delivery_keeps_the_transcript_on_the_clipboard_when_insertion_failed()
    {
        // The user is about to press Ctrl+V themselves. Restoring the previous contents now would
        // take the transcript away at exactly the wrong moment.
        var clipboard = new FakeRestorableClipboard();
        var service = new DictationDeliveryService(clipboard, new FakeInsertion { Fail = true });

        var result = await service.DeliverAsync("Привет", 0, CancellationToken.None);

        Assert.Equal(DictationDeliveryStatus.ClipboardFallback, result.Status);
        Assert.False(result.ClipboardRestoreScheduled);
        Assert.Equal(0, clipboard.RestoreCalls);
    }

    [Fact]
    public async Task Cancellation_during_insertion_still_puts_the_clipboard_back()
    {
        // Previously this path skipped restore entirely and the transcript stayed in the user's
        // clipboard permanently.
        var clipboard = new FakeRestorableClipboard();
        var service = new DictationDeliveryService(clipboard, new FakeInsertion { Cancel = true });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DeliverAsync("Привет", 0, CancellationToken.None));

        Assert.Equal(1, await clipboard.WaitForRestoreAsync());
    }

    [Fact]
    public async Task Restore_is_skipped_when_the_user_copied_something_of_their_own()
    {
        var clipboard = new FakeRestorableClipboard { UserOverwroteClipboard = true };
        var service = new DictationDeliveryService(clipboard, new FakeInsertion());

        var result = await service.DeliverAsync("Привет", 0, CancellationToken.None);

        Assert.Equal(DictationDeliveryStatus.Inserted, result.Status);
        Assert.Equal(1, await clipboard.WaitForRestoreAsync());
        Assert.False(clipboard.LastRestoreSucceeded);
    }

    [Fact]
    public async Task Restore_can_be_turned_off()
    {
        var clipboard = new FakeRestorableClipboard();
        var service = new DictationDeliveryService(clipboard, new FakeInsertion()) { RestoreClipboard = false };

        var result = await service.DeliverAsync("Привет", 0, CancellationToken.None);

        Assert.False(result.ClipboardRestoreScheduled);
        Assert.False(clipboard.SnapshotRequested);
    }

    private sealed class FakeRestorableClipboard : IRestorableClipboardService
    {
        private readonly TaskCompletionSource<int> _restored =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _restoreCalls;

        public bool SnapshotRequested { get; private set; }
        public int RestoreCalls => Volatile.Read(ref _restoreCalls);
        public bool UserOverwroteClipboard { get; init; }
        public bool LastRestoreSucceeded { get; private set; }
        public List<string> Values { get; } = [];

        /// <summary>Restore is detached, so tests have to wait for it rather than assume it ran.</summary>
        public Task<int> WaitForRestoreAsync() => _restored.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public Task CopyAsync(string text, CancellationToken cancellationToken)
        {
            Values.Add(text);
            return Task.CompletedTask;
        }

        public Task<ClipboardSnapshot> CopyAsync(
            string text,
            bool captureSnapshot,
            CancellationToken cancellationToken)
        {
            SnapshotRequested = captureSnapshot;
            Values.Add(text);
            return Task.FromResult(new ClipboardSnapshot(new System.Windows.DataObject(), "session", text));
        }

        public Task<bool> TryRestoreAsync(ClipboardSnapshot snapshot, CancellationToken cancellationToken)
        {
            var calls = Interlocked.Increment(ref _restoreCalls);
            LastRestoreSucceeded = !UserOverwroteClipboard;
            _restored.TrySetResult(calls);
            return Task.FromResult(LastRestoreSucceeded);
        }
    }

    private sealed class FakeInsertion : ITextInsertionService
    {
        public bool Fail { get; init; }
        public bool Cancel { get; init; }

        public Task InsertAsync(string text, nint targetWindow, CancellationToken cancellationToken)
        {
            if (Cancel)
            {
                throw new OperationCanceledException();
            }
            return Fail ? throw new InvalidOperationException("window lost") : Task.CompletedTask;
        }
    }
}
