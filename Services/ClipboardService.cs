using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;
using IDataObject = System.Windows.IDataObject;

namespace Egoist.Voice.Services;

/// <summary>
/// A snapshot of the clipboard taken before a dictation overwrites it, plus the marker that proves
/// the clipboard still holds our text at restore time.
/// </summary>
public sealed record ClipboardSnapshot(IDataObject? Data, string SessionId, string OwnText)
{
    public static ClipboardSnapshot None { get; } = new(null, string.Empty, string.Empty);
}

public sealed class ClipboardService : IRestorableClipboardService
{
    /// <summary>
    /// A private clipboard format carrying a per-dictation id. Restoring blindly is how other
    /// products lose whatever the user copied during the second the transcript was being pasted;
    /// this marker makes "is it still ours?" answerable instead of assumed.
    /// </summary>
    internal const string SessionFormat = "EgoistVoice.DictationSession";

    private const int Attempts = 6;
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;

    public async Task CopyAsync(string text, CancellationToken cancellationToken) =>
        await CopyAsync(text, captureSnapshot: false, cancellationToken).ConfigureAwait(false);

    public async Task<ClipboardSnapshot> CopyAsync(
        string text,
        bool captureSnapshot,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var snapshot = await RunOnStaAsync(
                    () => WriteWithMarker(text, sessionId, captureSnapshot),
                    cancellationToken).ConfigureAwait(false);
                AppLog.Write($"Transcript copied to clipboard, characters={text.Length}");
                return snapshot;
            }
            catch (Exception exception) when (IsTransientClipboardFailure(exception, cancellationToken))
            {
                if (attempt == Attempts)
                {
                    throw new InvalidOperationException("Буфер обмена занят другим приложением.", exception);
                }
                await Task.Delay(35 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        return ClipboardSnapshot.None;
    }

    /// <summary>
    /// Puts back what was on the clipboard before the dictation — unless the user copied something
    /// of their own in the meantime, in which case the right move is to do nothing.
    /// </summary>
    public async Task<bool> TryRestoreAsync(ClipboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Data is null || snapshot.SessionId.Length == 0)
        {
            return false;
        }

        try
        {
            return await RunOnStaAsync(() => Restore(snapshot), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Write("Clipboard restore failed; leaving the transcript in place", exception);
            return false;
        }
    }

    private static ClipboardSnapshot WriteWithMarker(string text, string sessionId, bool captureSnapshot)
    {
        var previous = captureSnapshot ? CaptureSnapshot() : null;

        WriteNativeClipboard(text, sessionId);
        return new ClipboardSnapshot(previous, sessionId, text);
    }

    /// <summary>
    /// Writes the transcript through the Win32 clipboard API instead of WPF/OLE.
    /// </summary>
    /// <remarks>
    /// <c>Clipboard.SetDataObject(..., copy: true)</c> first transfers OLE ownership and then
    /// flushes delayed-rendered data. On affected Windows sessions the ownership transfer succeeds
    /// but the flush repeatedly fails with CLIPBRD_E_CANT_OPEN, leaving Egoist Voice itself as the
    /// clipboard owner and dropping every dictation. A Win32-owned global-memory block is already
    /// durable after <see cref="SetClipboardData"/>, so it has no second flush phase to deadlock.
    /// </remarks>
    private static void WriteNativeClipboard(string text, string sessionId)
    {
        var textHandle = AllocateUnicodeClipboardData(text);
        var sessionHandle = AllocateUnicodeClipboardData(sessionId);

        try
        {
            if (!OpenClipboard(nint.Zero))
            {
                throw CreateClipboardException("OpenClipboard");
            }

            try
            {
                if (!EmptyClipboard())
                {
                    throw CreateClipboardException("EmptyClipboard");
                }

                if (SetClipboardData(CfUnicodeText, textHandle) == nint.Zero)
                {
                    throw CreateClipboardException("SetClipboardData(CF_UNICODETEXT)");
                }
                textHandle = nint.Zero; // Ownership transferred to Windows.

                var sessionFormat = RegisterClipboardFormat(SessionFormat);
                if (sessionFormat != 0 && SetClipboardData(sessionFormat, sessionHandle) != nint.Zero)
                {
                    sessionHandle = nint.Zero; // Marker is advisory; text comparison is the fallback.
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            if (textHandle != nint.Zero)
            {
                GlobalFree(textHandle);
            }
            if (sessionHandle != nint.Zero)
            {
                GlobalFree(sessionHandle);
            }
        }
    }

    private static nint AllocateUnicodeClipboardData(string value)
    {
        var bytes = checked((value.Length + 1) * sizeof(char));
        var handle = GlobalAlloc(GmemMoveable | GmemZeroInit, (nuint)bytes);
        if (handle == nint.Zero)
        {
            throw new OutOfMemoryException("GlobalAlloc failed while preparing clipboard data.");
        }

        var pointer = GlobalLock(handle);
        if (pointer == nint.Zero)
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error(), "GlobalLock failed while preparing clipboard data.");
            GlobalFree(handle);
            throw exception;
        }

        try
        {
            Marshal.Copy(value.ToCharArray(), 0, pointer, value.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        return handle;
    }

    private static ExternalException CreateClipboardException(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return new ExternalException($"{operation} failed: {new Win32Exception(error).Message}", error);
    }

    /// <summary>
    /// Copies the clipboard's contents into an object we own.
    /// </summary>
    /// <remarks>
    /// <see cref="Clipboard.GetDataObject"/> hands back a wrapper around a COM object owned by
    /// whichever application put it there. The moment we take ownership of the clipboard, many
    /// applications release theirs — so a stored wrapper would render nothing on restore, which is
    /// worse than not restoring at all, because by then the original is gone. Materializing the
    /// known formats up front is the only version of this feature that actually works.
    /// </remarks>
    private static DataObject? CaptureSnapshot()
    {
        try
        {
            var current = Clipboard.GetDataObject();
            if (current is null)
            {
                return null;
            }

            var snapshot = new DataObject();
            var copied = 0;
            foreach (var format in PreservedFormats)
            {
                if (!current.GetDataPresent(format))
                {
                    continue;
                }

                var value = current.GetData(format);
                if (value is not null)
                {
                    snapshot.SetData(format, value);
                    copied++;
                }
            }

            return copied > 0 ? snapshot : null;
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not snapshot the clipboard; restore will be skipped", exception);
            return null;
        }
    }

    /// <summary>
    /// The formats worth carrying across a dictation. An open-ended sweep of
    /// <c>GetFormats()</c> would pull in application-private COM formats that cannot be
    /// materialized and would fail on restore.
    /// </summary>
    private static readonly string[] PreservedFormats =
    [
        System.Windows.DataFormats.UnicodeText,
        System.Windows.DataFormats.Text,
        System.Windows.DataFormats.Html,
        System.Windows.DataFormats.Rtf,
        System.Windows.DataFormats.FileDrop,
        System.Windows.DataFormats.Bitmap
    ];

    private static bool Restore(ClipboardSnapshot snapshot)
    {
        var current = Clipboard.GetDataObject();
        if (current is null)
        {
            return false;
        }

        if (!IsStillOurs(current, snapshot))
        {
            AppLog.Write("Clipboard changed during dictation; user content preserved");
            return false;
        }

        // The same OLE flush that broke transcript delivery also breaks restoration after it has
        // already made this process the owner. Keeping the materialized snapshot owned by this
        // long-running tray process preserves every captured format without the failing flush.
        Clipboard.SetDataObject(snapshot.Data!, copy: false);
        return true;
    }

    /// <summary>
    /// Two independent checks. The private session format is the precise one, but custom formats do
    /// not always survive the OLE flush on every Windows build; comparing the text we wrote is the
    /// fallback that always works. Either matching means the clipboard is still ours.
    /// </summary>
    private static bool IsStillOurs(IDataObject current, ClipboardSnapshot snapshot)
    {
        try
        {
            if (current.GetDataPresent(SessionFormat) &&
                current.GetData(SessionFormat) as string == snapshot.SessionId)
            {
                return true;
            }
        }
        catch (Exception exception)
        {
            AppLog.Write("Session marker unreadable; falling back to text comparison", exception);
        }

        return current.GetDataPresent(System.Windows.DataFormats.UnicodeText) &&
            current.GetData(System.Windows.DataFormats.UnicodeText) as string == snapshot.OwnText;
    }

    /// <summary>
    /// OLE clipboard access requires an STA thread. Until now that held only by accident, because
    /// every continuation happened to return to the WPF dispatcher; once the ASR pipeline stopped
    /// capturing the UI context, the requirement had to become explicit.
    /// </summary>
    private static Task<T> RunOnStaAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return Task.FromResult(action());
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
        {
            return dispatcher.InvokeAsync(action, DispatcherPriority.Send, cancellationToken).Task;
        }

        return RunOnDedicatedStaThreadAsync(action);
    }

    /// <summary>Fallback for headless callers and for shutdown, when no dispatcher is available.</summary>
    private static Task<T> RunOnDedicatedStaThreadAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "EgoistVoice.Clipboard"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <remarks>
    /// A dispatcher shutdown aborts the queued operation and surfaces as a cancellation that has
    /// nothing to do with the caller's token. Treating it as transient keeps it from being read
    /// upstream as "the user cancelled", which would drop the transcript without a word — but only
    /// while the caller's own token is still alive, so real cancellation still propagates.
    /// </remarks>
    private static bool IsTransientClipboardFailure(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            ExternalException or ThreadStateException => true,
            _ => false
        };

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memoryHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memoryHandle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memoryHandle);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint memoryHandle);
}
