using System.Runtime.InteropServices;

namespace Egoist.Voice.Services;

internal static class NativeMethods
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int SwShowNoActivate = 4;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int HwndTopmost = -1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 0x0002;

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int message, nint wParam, nint lParam);

    internal static void MakeWindowNonActivating(nint handle)
    {
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExToolWindow | WsExNoActivate);
    }

    internal static void ShowWithoutActivation(nint handle) => ShowWindow(handle, SwShowNoActivate);

    /// <summary>
    /// Topmost declared once in XAML is not enough: SW_SHOWNOACTIVATE does not reorder the window
    /// inside the topmost band, so any overlay shown afterwards (Discord, Steam, another tool)
    /// covers the capsule until something else moves it.
    /// </summary>
    internal static void ReassertTopmost(nint handle)
    {
        if (handle != 0)
        {
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    /// <summary>
    /// Keeps the capsule inside the work area of the monitor it actually sits on. Doing this in
    /// physical pixels avoids the mixed-DPI trap of comparing window-local units against
    /// primary-monitor virtual-screen units, and MONITOR_DEFAULTTONEAREST recovers a capsule left
    /// behind on a monitor that has since been disconnected.
    /// </summary>
    internal static bool TryClampToMonitorWorkArea(nint handle)
    {
        if (handle == 0 || !GetWindowRect(handle, out var window))
        {
            return false;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return false;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        var width = window.Right - window.Left;
        var height = window.Bottom - window.Top;
        var work = info.Work;
        var left = Math.Clamp(window.Left, work.Left, Math.Max(work.Left, work.Right - width));
        var top = Math.Clamp(window.Top, work.Top, Math.Max(work.Top, work.Bottom - height));
        if (left != window.Left || top != window.Top)
        {
            SetWindowPos(handle, 0, left, top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }

        return true;
    }

    internal static bool ActivateForDiagnostics(nint handle)
    {
        if (handle == 0)
        {
            return false;
        }

        var currentThread = GetCurrentThreadId();
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
                       AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(handle);
            return SetForegroundWindow(handle);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    internal static void BeginWindowDrag(nint handle)
    {
        ReleaseCapture();
        SendMessage(handle, WmNcLButtonDown, HtCaption, 0);
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
