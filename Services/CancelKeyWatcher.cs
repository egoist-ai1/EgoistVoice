using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Egoist.Voice.Services;

/// <summary>
/// Watches for the cancel key while a dictation is in flight.
/// </summary>
/// <remarks>
/// <para>
/// Every product in this category cancels on Escape, and this one could not: the capsule is
/// <c>WS_EX_NOACTIVATE</c> and never holds keyboard focus, so a normal key handler would never
/// fire. A low-level hook is the only route.
/// </para>
/// <para>
/// The hook is installed only for the duration of a dictation and removed immediately afterwards.
/// A global keyboard hook that lives for the whole session would see every keystroke the user ever
/// types, which is not a reasonable thing for a dictation tool to do — and would put this process
/// on the critical path of the entire system's typing latency.
/// </para>
/// </remarks>
public sealed class CancelKeyWatcher : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmQuit = 0x0012;
    private const int WmUser = 0x0400;
    private const uint PmNoRemove = 0x0000;

    private readonly LowLevelKeyboardProc _callback;
    private readonly Dispatcher? _dispatcher;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private uint _threadId;
    private nint _hook;
    private volatile bool _armed;
    private volatile bool _disposed;

    public CancelKeyWatcher(int virtualKey = 0x1B)
    {
        VirtualKey = virtualKey;
        _callback = HookCallback;
        _dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        _thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = "EgoistVoice.CancelKey"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>Rebindable: Escape collides with Vim, terminals and modal dialogs.</summary>
    public int VirtualKey { get; set; }

    public event EventHandler? Cancelled;

    /// <summary>Starts listening. Called when a dictation begins.</summary>
    public void Arm() => _armed = true;

    /// <summary>Stops listening. Called as soon as the dictation ends, successfully or not.</summary>
    public void Disarm() => _armed = false;

    private void RunLoop()
    {
        try
        {
            PeekMessage(out _, 0, WmUser, WmUser, PmNoRemove);
            Volatile.Write(ref _threadId, GetCurrentThreadId());
            _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
            if (_hook == 0)
            {
                AppLog.Write("Cancel key watcher unavailable", new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
        finally
        {
            TrySignalReady();
        }

        while (!_disposed)
        {
            var result = GetMessage(out var message, 0, 0, 0);
            if (result <= 0)
            {
                break;
            }
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0 && _armed && !_disposed && (message == WmKeyDown || message == WmSysKeyDown))
        {
            try
            {
                var input = Marshal.PtrToStructure<KeyboardHookStruct>(data);
                if (input.VirtualKey == VirtualKey)
                {
                    _armed = false;
                    Raise();

                    // Deliberately not swallowed. The user pressed Escape at whatever they were
                    // working in; consuming it here would break their dialog or their editor.
                }
            }
            catch
            {
                // Never log from inside a low-level hook: it is a global lock and a file append.
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private void Raise()
    {
        var handler = Cancelled;
        if (handler is null)
        {
            return;
        }

        if (_dispatcher is null)
        {
            handler(this, EventArgs.Empty);
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Send, () => handler(this, EventArgs.Empty));
    }

    private void TrySignalReady()
    {
        try
        {
            _ready.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _armed = false;
        var threadId = Volatile.Read(ref _threadId);
        if (threadId != 0)
        {
            PostThreadMessage(threadId, WmQuit, 0, 0);
        }
        if (_thread.Join(TimeSpan.FromSeconds(1)))
        {
            _ready.Dispose();
        }
    }

    private delegate nint LowLevelKeyboardProc(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookStruct
    {
        public int VirtualKey;
        public int ScanCode;
        public int Flags;
        public int Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, nint window, uint min, uint max, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
}
