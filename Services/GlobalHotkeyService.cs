using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Egoist.Voice.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private static int _nextHotkeyId = 0x6600;

    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private readonly DispatcherTimer _releasePoller;
    private readonly KeyboardShortcut _shortcut;
    private readonly int _hotkeyId;
    private bool _isHeld;
    private bool _disposed;

    public GlobalHotkeyService(nint windowHandle, KeyboardShortcut shortcut)
    {
        if (!shortcut.IsValid)
        {
            throw new ArgumentException("Некорректная горячая клавиша.", nameof(shortcut));
        }

        _windowHandle = windowHandle;
        _shortcut = shortcut;
        _hotkeyId = Interlocked.Increment(ref _nextHotkeyId);
        AppLog.Write($"Registering hotkey {_shortcut.DisplayName}, hwnd=0x{windowHandle:X}, id=0x{_hotkeyId:X}");
        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("Не удалось создать обработчик горячей клавиши.");
        _source.AddHook(WindowProc);
        _releasePoller = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Input,
            OnReleasePoll,
            _source.Dispatcher);
        _releasePoller.Stop();

        if (!RegisterHotKey(
                _windowHandle,
                _hotkeyId,
                ToNativeModifiers(shortcut.Modifiers) | ModNoRepeat,
                (uint)shortcut.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            AppLog.Write($"RegisterHotKey failed, Win32Error={error}");
            _source.RemoveHook(WindowProc);
            throw new InvalidOperationException($"{shortcut.DisplayName} уже используется другим приложением.");
        }
        AppLog.Write("RegisterHotKey succeeded");
    }

    public event EventHandler? Pressed;
    public event EventHandler? Released;

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam == _hotkeyId)
        {
            AppLog.Write("WM_HOTKEY received");
            handled = true;
            if (!_isHeld)
            {
                _isHeld = true;
                Pressed?.Invoke(this, EventArgs.Empty);
                _releasePoller.Start();
            }
        }
        return 0;
    }

    private void OnReleasePoll(object? sender, EventArgs args)
    {
        // Wait until the whole configured chord is released. This prevents a
        // still-held modifier from changing the automatic Ctrl+V delivery.
        if (!_isHeld || _shortcut.HeldVirtualKeys().Any(IsKeyDown))
        {
            return;
        }

        _isHeld = false;
        _releasePoller.Stop();
        AppLog.Write("Hotkey release detected");
        Released?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _isHeld = false;
        _releasePoller.Stop();
        AppLog.Write("Unregistering hotkey");
        UnregisterHotKey(_windowHandle, _hotkeyId);
        _source.RemoveHook(WindowProc);
    }

    internal static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        var native = 0u;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) native |= ModAlt;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) native |= ModControl;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) native |= ModShift;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) native |= ModWin;
        return native;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
