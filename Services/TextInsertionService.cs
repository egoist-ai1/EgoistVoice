using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Egoist.Voice.Services;

public sealed class TextInsertionService : ITextInsertionService
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;
    private const uint KeyEventExtendedKey = 0x0001;
    private const ushort VkNoName = 0xFC;
    private const uint MapVkToVsc = 0;
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkMenu = 0x12;
    private const ushort VkLwin = 0x5B;
    private const ushort VkRwin = 0x5C;
    private const ushort VkV = 0x56;
    private const ushort VkInsert = 0x2D;

    /// <summary>
    /// Stamped into every synthetic event so applications with their own input hooks — and our own
    /// diagnostics — can tell our injection apart from a real keystroke.
    /// </summary>
    internal const uint InjectionSignature = 0x45474F49;

    /// <summary>Overrides automatic target detection. Null means "decide from the foreground window".</summary>
    public PasteMethod? ForcedMethod { get; set; }

    internal static int NativeInputSize => Marshal.SizeOf<Input>();

    /// <summary>Number of synthetic events a paste method emits. Exposed for tests.</summary>
    internal static int PasteEventCount(PasteMethod method) => BuildPasteInputs(method).Length;

    public async Task InsertAsync(string text, nint targetWindow, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        AppLog.Write($"Text insertion requested: characters={text.Length}, target=0x{targetWindow:X}, foreground=0x{foreground:X}");
        if (targetWindow != 0 && foreground != targetWindow)
        {
            var restored = NativeMethods.SetForegroundWindow(targetWindow);
            AppLog.Write($"Foreground restore requested: success={restored}");

            // Polled instead of a flat 80 ms sleep. The switch usually completes in one or two
            // ticks, and paying the worst case on every dictation is 70 ms of pure waiting.
            if (!await WaitForForegroundAsync(targetWindow, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Исходное поле больше не активно. Текст скопирован: нажмите Ctrl+V.");
            }
        }

        var method = ForcedMethod ?? PasteTargetPolicy.ResolveForWindow(targetWindow).Method;
        if (method == PasteMethod.ClipboardOnly)
        {
            throw new InvalidOperationException("Текст скопирован: нажмите Ctrl+V.");
        }

        // Sanitation and the paste chord go out as one SendInput block. As two calls, real user
        // input could interleave between them and land inside the chord.
        var inputs = BuildModifierRelease().Concat(BuildPasteInputs(method)).ToArray();
        var sent = SendInput((uint)inputs.Length, inputs, NativeInputSize);
        if (sent != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            AppLog.Write($"SendInput incomplete: sent={sent}, expected={inputs.Length}, error={error}");
            throw new Win32Exception(error, DescribeInjectionFailure(targetWindow));
        }
        AppLog.Write($"SendInput succeeded: events={sent}, method={method}");
    }

    /// <summary>
    /// UIPI blocks injection into a process of higher integrity and reports nothing at all — neither
    /// a return value nor a last error. Guessing is the only option, so at least guess out loud
    /// instead of failing with a message that sends the user looking in the wrong place.
    /// </summary>
    private static string DescribeInjectionFailure(nint targetWindow)
    {
        var target = PasteTargetPolicy.ResolveForWindow(targetWindow);
        return target.ProcessName.Length > 0
            ? $"Windows не разрешила вставку в «{target.ProcessName}». Возможно, окно запущено от администратора — нажмите Ctrl+V."
            : "Windows не разрешила вставку в активное поле. Текст скопирован: нажмите Ctrl+V.";
    }

    /// <summary>
    /// Releases modifiers the user is still holding, which would otherwise turn Ctrl+V into
    /// Ctrl+Shift+V or a different chord entirely.
    /// </summary>
    /// <remarks>
    /// Releasing Windows keys needs care: the shell opens the Start menu on WIN keyup when no
    /// other key was pressed while WIN was down. A no-op keystroke is injected first so the shell
    /// sees the combination as consumed and the Start menu does not appear over the target field.
    /// </remarks>
    private static List<Input> BuildModifierRelease()
    {
        var held = new List<ushort>(4);
        foreach (var key in new[] { VkShift, VkMenu, VkLwin, VkRwin })
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                held.Add(key);
            }
        }

        var inputs = new List<Input>(held.Count + 2);
        if (held.Contains(VkLwin) || held.Contains(VkRwin))
        {
            inputs.Add(CreateKeyboardInput(VkNoName, 0));
            inputs.Add(CreateKeyboardInput(VkNoName, KeyEventKeyUp));
        }

        foreach (var key in held)
        {
            inputs.Add(CreateKeyboardInput(key, KeyEventKeyUp));
        }
        return inputs;
    }

    /// <summary>
    /// Keys that live on the extended part of the keyboard. With KEYEVENTF_SCANCODE Windows ignores
    /// the virtual key entirely and reads the scan code, and the extended flag is the only thing
    /// separating grey Insert (scan 0x52 + extended) from Numpad 0 (scan 0x52). Without it the
    /// Shift+Insert fallback typed a literal "0" into the terminal it was meant to rescue.
    /// </summary>
    private static bool IsExtendedKey(ushort virtualKey) => virtualKey is
        VkInsert or VkLwin or VkRwin or
        0x2E or // Delete
        0x24 or // Home
        0x23 or // End
        0x21 or // Page Up
        0x22 or // Page Down
        0x25 or 0x26 or 0x27 or 0x28 or // arrows
        0xA3 or // Right Control
        0xA5 or // Right Alt
        0x90;   // Num Lock

    private const int ForegroundPollIntervalMs = 10;
    private const int ForegroundPollTimeoutMs = 300;

    private static async Task<bool> WaitForForegroundAsync(nint targetWindow, CancellationToken cancellationToken)
    {
        for (var waited = 0; waited < ForegroundPollTimeoutMs; waited += ForegroundPollIntervalMs)
        {
            if (NativeMethods.GetForegroundWindow() == targetWindow)
            {
                return true;
            }
            await Task.Delay(ForegroundPollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        return NativeMethods.GetForegroundWindow() == targetWindow;
    }

    /// <summary>
    /// Built from virtual key codes, never from characters: sending the character 'v' resolves
    /// through the active layout and lands on a different physical key under Russian, AZERTY or
    /// Dvorak. Scan codes are filled in too, because DirectInput games, RDP and some virtual
    /// machines read the scan code and ignore the virtual key entirely.
    /// </summary>
    private static Input[] BuildPasteInputs(PasteMethod method) => method switch
    {
        PasteMethod.ControlShiftV =>
        [
            CreateKeyboardInput(VkControl, 0),
            CreateKeyboardInput(VkShift, 0),
            CreateKeyboardInput(VkV, 0),
            CreateKeyboardInput(VkV, KeyEventKeyUp),
            CreateKeyboardInput(VkShift, KeyEventKeyUp),
            CreateKeyboardInput(VkControl, KeyEventKeyUp)
        ],
        PasteMethod.ShiftInsert =>
        [
            CreateKeyboardInput(VkShift, 0),
            CreateKeyboardInput(VkInsert, 0),
            CreateKeyboardInput(VkInsert, KeyEventKeyUp),
            CreateKeyboardInput(VkShift, KeyEventKeyUp)
        ],
        _ =>
        [
            CreateKeyboardInput(VkControl, 0),
            CreateKeyboardInput(VkV, 0),
            CreateKeyboardInput(VkV, KeyEventKeyUp),
            CreateKeyboardInput(VkControl, KeyEventKeyUp)
        ]
    };

    private static Input CreateKeyboardInput(ushort keyCode, uint flags)
    {
        var scanCode = (ushort)MapVirtualKey(keyCode, MapVkToVsc);
        var effectiveFlags = flags;

        // Only take the scan-code path when the layout actually produced one. MapVirtualKey returns
        // 0 for keys the current layout does not expose, and a scan code of 0 with the flag set is
        // an event that reaches nobody.
        if (scanCode != 0)
        {
            effectiveFlags |= KeyEventScanCode;
            if (IsExtendedKey(keyCode))
            {
                effectiveFlags |= KeyEventExtendedKey;
            }
        }

        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = keyCode,
                    ScanCode = scanCode,
                    Flags = effectiveFlags,
                    Time = 0,
                    ExtraInfo = InjectionSignature
                }
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    // INPUT is a discriminated union. Including its largest member is required
    // so Marshal.SizeOf<Input>() is 40 bytes on x64, as SendInput expects.
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
