using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using Egoist.Voice.Services;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Egoist.Voice;

public partial class CustomShortcutDialog : Window
{
    private KeyboardShortcut? _selectedShortcut;

    internal CustomShortcutDialog(KeyboardShortcut? currentShortcut)
    {
        InitializeComponent();
        if (currentShortcut is { IsValid: true } shortcut)
        {
            _selectedShortcut = shortcut;
            ShortcutText.Text = shortcut.DisplayName;
            SaveButton.IsEnabled = true;
            HintText.Text = "Можно заменить";
        }

        Loaded += (_, _) => CaptureSurface.Focus();
    }

    internal KeyboardShortcut SelectedShortcut =>
        _selectedShortcut ?? throw new InvalidOperationException("Горячая клавиша не выбрана.");

    internal void RenderPreview(string outputPath)
    {
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private void CaptureSurface_OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            DialogResult = false;
            args.Handled = true;
            return;
        }

        if (IsModifierKey(key))
        {
            ShortcutText.Text = ModifierPreview(Keyboard.Modifiers);
            args.Handled = true;
            return;
        }

        var shortcut = KeyboardShortcut.FromKey(key, Keyboard.Modifiers);
        if (!shortcut.IsValid)
        {
            HintText.Text = shortcut.Modifiers == HotkeyModifiers.None
                ? "Для букв добавьте Ctrl, Alt, Shift или Win"
                : "Эта клавиша не поддерживается";
            args.Handled = true;
            return;
        }

        _selectedShortcut = shortcut;
        ShortcutText.Text = shortcut.DisplayName;
        HintText.Text = "Готово к сохранению";
        SaveButton.IsEnabled = true;
        SaveButton.Focus();
        args.Handled = true;
    }

    private void CaptureSurface_OnPreviewKeyUp(object sender, KeyEventArgs args)
    {
        if (_selectedShortcut is null && Keyboard.Modifiers == ModifierKeys.None)
        {
            ShortcutText.Text = "Нажмите сочетание…";
        }
        args.Handled = true;
    }

    private void CaptureSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        CaptureSurface.Focus();
        args.Handled = true;
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ButtonState == MouseButtonState.Pressed)
        {
            NativeMethods.BeginWindowDrag(new WindowInteropHelper(this).Handle);
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs args)
    {
        if (_selectedShortcut is { IsValid: true })
        {
            DialogResult = true;
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs args) => DialogResult = false;

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;

    private static string ModifierPreview(ModifierKeys modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return parts.Count == 0 ? "Нажмите сочетание…" : string.Join(" + ", parts) + " + …";
    }
}
