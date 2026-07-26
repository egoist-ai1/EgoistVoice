namespace Egoist.Voice.Services;

public enum DictationDeliveryStatus
{
    Inserted,
    ClipboardFallback,
    ClipboardFailed,

    /// <summary>The target refuses injection by policy — a password manager or a secure prompt.</summary>
    SuppressedForSensitiveTarget
}

public sealed record DictationDeliveryResult(
    DictationDeliveryStatus Status,
    string? Error = null,
    /// <summary>
    /// Whether a restore was queued — not whether it succeeded. The restore is deliberately
    /// detached so it cannot add its settle delay to every dictation, so by the time this result
    /// is returned the outcome is genuinely unknown. Whether the clipboard was actually put back
    /// is decided later, and recorded in the log.
    /// </summary>
    bool ClipboardRestoreScheduled = false);

public sealed class DictationDeliveryService(
    IClipboardService clipboard,
    ITextInsertionService textInsertion)
{
    /// <summary>
    /// Restoring the previous clipboard contents. On by default: silently keeping whatever the user
    /// last copied is the behaviour every competitor converged on, and losing it is the complaint
    /// they all still get.
    /// </summary>
    public bool RestoreClipboard { get; set; } = true;

    /// <summary>
    /// Inserts one phrase of a live dictation without disturbing the clipboard afterwards.
    /// </summary>
    /// <remarks>
    /// Restoring between phrases would mean a snapshot-write-restore cycle several times a
    /// sentence, each with its own settle delay — the clipboard would spend the whole dictation in
    /// flux and the user would lose whatever they had copied anyway. The clipboard is put back once,
    /// at the end, by <see cref="RestoreAfterLiveDictationAsync"/>.
    /// </remarks>
    public async Task<DictationDeliveryResult> DeliverPhraseAsync(
        string text,
        nint targetWindow,
        CancellationToken cancellationToken)
    {
        var target = PasteTargetPolicy.ResolveForWindow(targetWindow);
        if (target.IsSensitive)
        {
            AppLog.Write($"Live phrase suppressed for sensitive target: {target.ProcessName}");
            return new DictationDeliveryResult(
                DictationDeliveryStatus.SuppressedForSensitiveTarget,
                target.ProcessName);
        }

        try
        {
            await clipboard.CopyAsync(text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Write("Live phrase could not reach the clipboard", exception);
            return new DictationDeliveryResult(DictationDeliveryStatus.ClipboardFailed, exception.Message);
        }

        try
        {
            await textInsertion.InsertAsync(text, targetWindow, cancellationToken).ConfigureAwait(false);
            return new DictationDeliveryResult(DictationDeliveryStatus.Inserted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Write("Live phrase insertion failed", exception);
            return new DictationDeliveryResult(DictationDeliveryStatus.ClipboardFallback, exception.Message);
        }
    }

    /// <summary>Puts the clipboard back once a live dictation has finished.</summary>
    public async Task RestoreAfterLiveDictationAsync(ClipboardSnapshot snapshot)
    {
        if (!RestoreClipboard || clipboard is not IRestorableClipboardService restorable)
        {
            return;
        }

        ScheduleRestore(restorable, snapshot);
        await Task.CompletedTask;
    }

    /// <summary>Takes the clipboard snapshot that a live dictation will restore when it ends.</summary>
    public async Task<ClipboardSnapshot> CaptureClipboardAsync(CancellationToken cancellationToken)
    {
        if (!RestoreClipboard || clipboard is not IRestorableClipboardService restorable)
        {
            return ClipboardSnapshot.None;
        }

        try
        {
            // An empty write purely to capture what was there: the snapshot is taken by the same
            // call that overwrites, so there is no window in which another application can slip in.
            return await restorable
                .CopyAsync(string.Empty, captureSnapshot: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not snapshot the clipboard before live dictation", exception);
            return ClipboardSnapshot.None;
        }
    }

    public async Task<DictationDeliveryResult> DeliverAsync(
        string text,
        nint targetWindow,
        CancellationToken cancellationToken)
    {
        var target = PasteTargetPolicy.ResolveForWindow(targetWindow);
        if (target.IsSensitive)
        {
            // Not even copied: the point is that this transcript should not travel anywhere near a
            // credential field, and the clipboard is the widest travel route there is.
            AppLog.Write($"Delivery suppressed for sensitive target: {target.ProcessName}");
            return new DictationDeliveryResult(
                DictationDeliveryStatus.SuppressedForSensitiveTarget,
                target.ProcessName);
        }

        var restorable = RestoreClipboard ? clipboard as IRestorableClipboardService : null;
        var snapshot = ClipboardSnapshot.None;

        try
        {
            if (restorable is not null)
            {
                snapshot = await restorable
                    .CopyAsync(text, captureSnapshot: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await clipboard.CopyAsync(text, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Write("Clipboard copy failed", exception);
            return new DictationDeliveryResult(DictationDeliveryStatus.ClipboardFailed, exception.Message);
        }

        try
        {
            await textInsertion.InsertAsync(text, targetWindow, cancellationToken).ConfigureAwait(false);

            // Restore runs detached. Waiting for it would add the settle delay to every dictation,
            // and at a p95 budget of 180 ms that is most of the budget spent doing nothing.
            ScheduleRestore(restorable, snapshot);
            return new DictationDeliveryResult(DictationDeliveryStatus.Inserted, null, snapshot.Data is not null);
        }
        catch (OperationCanceledException)
        {
            // Cancelled mid-insert: the clipboard has already been overwritten, so it still has to
            // be put back. Previously this path skipped restore entirely and the transcript stayed
            // in the user's clipboard for good.
            ScheduleRestore(restorable, snapshot);
            throw;
        }
        catch (Exception exception)
        {
            // The transcript stays on the clipboard on purpose: the user is about to press Ctrl+V
            // themselves, so restoring the previous contents now would take it away from them.
            AppLog.Write("Direct insertion failed; clipboard fallback is ready", exception);
            return new DictationDeliveryResult(DictationDeliveryStatus.ClipboardFallback, exception.Message);
        }
    }

    private static void ScheduleRestore(IRestorableClipboardService? restorable, ClipboardSnapshot snapshot)
    {
        if (restorable is null || snapshot.Data is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Settle first: restoring before the target has actually read the clipboard makes
                // the paste land on the previous contents, which is the classic failure here.
                await Task.Delay(ClipboardSettleMilliseconds).ConfigureAwait(false);
                await restorable.TryRestoreAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppLog.Write("Deferred clipboard restore failed", exception);
            }
        });
    }

    private const int ClipboardSettleMilliseconds = 120;
}
