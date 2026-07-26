using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Egoist.Voice.Core;
using Forms = System.Windows.Forms;

namespace Egoist.Voice.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly MainWindow _window;
    private readonly IModelManager _modelManager;
    private readonly Forms.ToolStripMenuItem _modelStatus;
    private readonly Forms.ToolStripMenuItem _showDownload;
    private readonly Forms.ToolStripMenuItem _retryDownload;
    private readonly Forms.ToolStripMenuItem _customActivationItem;
    private readonly Dictionary<ActivationBinding, Forms.ToolStripMenuItem> _activationItems = [];
    private readonly DictationSettingsService _settingsService = new();
    private readonly Forms.ToolStripMenuItem _mixedLanguageItem;
    private readonly Forms.ToolStripMenuItem _numbersItem;
    private readonly Forms.ToolStripMenuItem _voiceCommandsItem;
    private readonly Forms.ToolStripMenuItem _restoreClipboardItem;
    private readonly Forms.ToolStripMenuItem _soundItem;

    public TrayService(MainWindow window, IModelManager modelManager, Action quit)
    {
        _window = window;
        _modelManager = modelManager;

        var renderer = new EgoistTrayRenderer();
        var menu = CreateDropDown<Forms.ContextMenuStrip>(renderer);
        menu.ShowCheckMargin = true;
        menu.Items.Add(CreateItem("Начать / остановить", async (_, _) => await window.ToggleRecordingAsync()));

        var activationMenu = CreateItem("Кнопка запуска");
        ConfigureDropDown(activationMenu.DropDown, renderer);
        AddActivationItem(activationMenu, ActivationBinding.Mouse5AndKeyboard);
        AddActivationItem(activationMenu, ActivationBinding.Mouse5);
        AddActivationItem(activationMenu, ActivationBinding.Mouse4);
        AddActivationItem(activationMenu, ActivationBinding.Keyboard);
        activationMenu.DropDownItems.Add(CreateSeparator());
        _customActivationItem = CreateItem("Своя…", (_, _) => ConfigureCustomShortcut());
        _customActivationItem.CheckOnClick = false;
        activationMenu.DropDownItems.Add(_customActivationItem);
        menu.Items.Add(activationMenu);

        // Everything the post-processing pipeline can do was previously unreachable: the settings
        // existed only as a JSON file nobody knew about. A feature the user cannot find is a
        // feature that does not exist.
        var settingsMenu = CreateItem("Настройки");
        ConfigureDropDown(settingsMenu.DropDown, renderer);
        _mixedLanguageItem = AddToggle(settingsMenu, "Смешанная русско-английская речь",
            settings => settings with { MixedLanguageMode = !settings.MixedLanguageMode });
        _numbersItem = AddToggle(settingsMenu, "Числа цифрами",
            settings => settings with { ApplyNumberNormalization = !settings.ApplyNumberNormalization });
        _voiceCommandsItem = AddToggle(settingsMenu, "Голосовые команды",
            settings => settings with { ApplyVoiceCommands = !settings.ApplyVoiceCommands });
        _restoreClipboardItem = AddToggle(settingsMenu, "Возвращать буфер обмена",
            settings => settings with { RestoreClipboard = !settings.RestoreClipboard });
        _soundItem = AddToggle(settingsMenu, "Звуковые сигналы",
            settings => settings with { SoundFeedback = !settings.SoundFeedback });
        settingsMenu.DropDownItems.Add(CreateSeparator());
        var dictionaryItem = CreateItem("Открыть словарь…", (_, _) => OpenDictionary());
        dictionaryItem.CheckOnClick = false;
        settingsMenu.DropDownItems.Add(dictionaryItem);
        menu.Items.Add(settingsMenu);
        menu.Items.Add(CreateSeparator());
        RefreshSettingsChecks();

        _modelStatus = CreateItem(modelManager.AreAllModelsReady
            ? "GigaAM + Whisper · готовы"
            : "GigaAM + Whisper · подготовка…");
        _modelStatus.Enabled = false;
        menu.Items.Add(_modelStatus);

        _showDownload = CreateItem("Показать загрузку", (_, _) => window.ShowModelDownloadStatus());
        _showDownload.Visible = !modelManager.AreAllModelsReady;
        menu.Items.Add(_showDownload);

        _retryDownload = CreateItem("Повторить загрузку", (_, _) => window.RetryModelDownloads());
        _retryDownload.Visible = false;
        menu.Items.Add(_retryDownload);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(CreateItem("Выход", (_, _) => quit()));

        _icon = LoadApplicationIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Egoist Voice — локальная диктовка",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
        _modelManager.ProgressChanged += OnModelProgressChanged;
        _window.ActivationBindingChanged += OnActivationBindingChanged;
        UpdateActivationChecks();
    }

    /// <summary>
    /// A checkable item that writes the setting and re-applies it immediately. Nothing here needs a
    /// restart — the pipeline is rebuilt from disk on every change.
    /// </summary>
    private Forms.ToolStripMenuItem AddToggle(
        Forms.ToolStripMenuItem parent,
        string text,
        Func<DictationSettings, DictationSettings> toggle)
    {
        var item = CreateItem(text, (_, _) =>
        {
            _settingsService.Save(toggle(_settingsService.Load()));
            _window.ApplyDictationSettings();
            RefreshSettingsChecks();
        });
        item.CheckOnClick = false;
        parent.DropDownItems.Add(item);
        return item;
    }

    private void RefreshSettingsChecks()
    {
        var settings = _settingsService.Load();
        _mixedLanguageItem.Checked = settings.MixedLanguageMode;
        _numbersItem.Checked = settings.ApplyNumberNormalization;
        _voiceCommandsItem.Checked = settings.ApplyVoiceCommands;
        _restoreClipboardItem.Checked = settings.RestoreClipboard;
        _soundItem.Checked = settings.SoundFeedback;
    }

    /// <summary>
    /// Opens the dictionary in whatever handles .json, creating a commented template first. The
    /// alternative — a bespoke editor — is a lot of UI for a file most users will touch twice.
    /// </summary>
    private void OpenDictionary()
    {
        try
        {
            _settingsService.EnsureDictionaryTemplate();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _settingsService.DictionaryPath,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not open the user dictionary", exception);
        }
    }

    private void AddActivationItem(Forms.ToolStripMenuItem parent, ActivationBinding binding)
    {
        var item = CreateItem(ActivationBindingInfo.DisplayName(binding), (_, _) => ChangeActivationBinding(binding));
        item.CheckOnClick = false;
        item.Tag = binding;
        _activationItems[binding] = item;
        parent.DropDownItems.Add(item);
    }

    private void ChangeActivationBinding(ActivationBinding binding)
    {
        if (_window.TrySetActivationBinding(binding, out var error))
        {
            UpdateActivationChecks();
            _notifyIcon.Text = TruncateTooltip($"Egoist Voice — {_window.CurrentActivationDisplayName}");
            return;
        }

        _notifyIcon.ShowBalloonTip(
            3500,
            "Egoist Voice",
            error ?? "Не удалось сменить кнопку запуска.",
            Forms.ToolTipIcon.Warning);
    }

    private void ConfigureCustomShortcut()
    {
        _window.SetActivationCaptureActive(true);
        CustomShortcutDialog dialog;
        bool accepted;
        try
        {
            dialog = new CustomShortcutDialog(_window.CurrentCustomShortcut);
            accepted = dialog.ShowDialog() == true;
        }
        finally
        {
            _window.SetActivationCaptureActive(false);
        }

        if (!accepted)
        {
            return;
        }

        if (_window.TrySetCustomShortcut(dialog.SelectedShortcut, out var error))
        {
            UpdateActivationChecks();
            _notifyIcon.Text = TruncateTooltip($"Egoist Voice — {_window.CurrentActivationDisplayName}");
            return;
        }

        _notifyIcon.ShowBalloonTip(
            3500,
            "Egoist Voice",
            error ?? "Не удалось назначить сочетание.",
            Forms.ToolTipIcon.Warning);
    }

    private async void OnNotifyIconMouseClick(object? sender, Forms.MouseEventArgs args)
    {
        if (args.Button == Forms.MouseButtons.Left)
        {
            await _window.ToggleRecordingAsync();
        }
    }

    private void OnActivationBindingChanged(object? sender, EventArgs args) => UpdateActivationChecks();

    private void UpdateActivationChecks()
    {
        foreach (var (binding, item) in _activationItems)
        {
            item.Checked = binding == _window.CurrentActivationBinding;
        }

        _customActivationItem.Checked = _window.CurrentActivationBinding == ActivationBinding.CustomKeyboard;
        _customActivationItem.Text = _window.CurrentCustomShortcut is { IsValid: true } custom
            ? $"Своя…  ·  {custom.DisplayName}"
            : "Своя…";
    }

    private void OnModelProgressChanged(object? sender, ModelTransferProgress progress)
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _ = _window.Dispatcher.BeginInvoke(() => UpdateModelProgress(progress));
            return;
        }

        UpdateModelProgress(progress);
    }

    private void UpdateModelProgress(ModelTransferProgress progress)
    {
        var failed = progress.Stage == ModelTransferStage.Failed;
        var ready = progress.Stage == ModelTransferStage.Ready && _modelManager.AreAllModelsReady;
        _modelStatus.Text = failed
            ? "GigaAM + Whisper · ошибка загрузки"
            : ready
                ? "GigaAM + Whisper · готовы"
                : ModelProgressFormatter.Detail(progress);
        _showDownload.Visible = !ready && !failed;
        _retryDownload.Visible = failed;
        _notifyIcon.Text = ModelProgressFormatter.TrayTooltip(progress);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _modelManager.ProgressChanged -= OnModelProgressChanged;
        _window.ActivationBindingChanged -= OnActivationBindingChanged;
        _notifyIcon.MouseClick -= OnNotifyIconMouseClick;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private static T CreateDropDown<T>(Forms.ToolStripRenderer renderer)
        where T : Forms.ToolStripDropDown, new()
    {
        var dropDown = new T();
        ConfigureDropDown(dropDown, renderer);
        return dropDown;
    }

    internal static void ConfigureDropDown(Forms.ToolStripDropDown dropDown, Forms.ToolStripRenderer renderer)
    {
        dropDown.RenderMode = Forms.ToolStripRenderMode.ManagerRenderMode;
        dropDown.Renderer = renderer;
        dropDown.BackColor = EgoistTrayPalette.Background;
        dropDown.ForeColor = EgoistTrayPalette.Primary;
        dropDown.Font = CreateMenuFont();
        dropDown.Padding = new Forms.Padding(5);
        if (dropDown is Forms.ToolStripDropDownMenu menu)
        {
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
        }
    }

    internal static Forms.ToolStripMenuItem CreateItem(string text, EventHandler? click = null)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            BackColor = EgoistTrayPalette.Background,
            ForeColor = EgoistTrayPalette.Primary,
            Padding = new Forms.Padding(5, 3, 5, 3),
            AutoToolTip = false
        };
        if (click is not null)
        {
            item.Click += click;
        }
        return item;
    }

    internal static Forms.ToolStripSeparator CreateSeparator() => new()
    {
        BackColor = EgoistTrayPalette.Background,
        ForeColor = EgoistTrayPalette.Separator,
        Margin = new Forms.Padding(0, 3, 0, 3)
    };

    private static Font CreateMenuFont()
    {
        using var variable = new Font("Segoe UI Variable Text", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        return string.Equals(variable.Name, "Segoe UI Variable Text", StringComparison.OrdinalIgnoreCase)
            ? (Font)variable.Clone()
            : new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
    }

    /// <summary>
    /// Picks the frame the notification area actually asks for.
    /// </summary>
    /// <remarks>
    /// <c>Icon.ExtractAssociatedIcon</c> returns a single frame — normally 32×32 — and discards the
    /// rest. EgoistVoice.ico carries seven sizes, so at 125 % or 150 % scaling the tray was
    /// requesting 20 or 24 pixels and receiving a downscaled 32, which is why the icon looked soft.
    /// Now the required size is stated and Windows selects the matching frame.
    /// </remarks>
    private static Icon LoadApplicationIcon()
    {
        var required = SystemInformation.SmallIconSize;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "EgoistVoice.ico");

        try
        {
            if (File.Exists(iconPath))
            {
                using var stream = File.OpenRead(iconPath);
                return new Icon(stream, required);
            }
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not load the multi-resolution tray icon", exception);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                // The embedded resource still carries every frame, so asking for a size works here
                // as well — unlike ExtractAssociatedIcon, which always hands back one.
                using var embedded = Icon.ExtractIcon(Environment.ProcessPath, 0, required.Width);
                if (embedded is not null)
                {
                    return (Icon)embedded.Clone();
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not extract a sized icon from the executable", exception);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static string TruncateTooltip(string value) => value.Length <= 63 ? value : value[..62] + "…";
}
