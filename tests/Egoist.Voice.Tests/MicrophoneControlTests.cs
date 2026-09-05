using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class MicrophoneControlTests
{
    private static readonly MicrophoneDeviceInfo DefaultA = new("endpoint-a", "Studio A", true);
    private static readonly MicrophoneDeviceInfo DeviceB = new("endpoint-b", "Studio B", false);

    [Fact]
    public void NullSelectionTracksTheCurrentWindowsDefault()
    {
        var devices = new[] { DefaultA, DeviceB };

        Assert.True(MicrophoneSelectionPolicy.IsAvailable(null, devices));
        Assert.Equal("Системный · Studio A", MicrophoneSelectionPolicy.DisplayName(null, devices));
        Assert.Equal(
            MicrophoneTopologyAction.InventoryChanged,
            MicrophoneLifecyclePolicy.EvaluateTopologyChange(null, false, "endpoint-a", devices));
    }

    [Fact]
    public void ChangingTheWindowsDefaultRestartsOnlyDefaultSelection()
    {
        var changed = new[]
        {
            DefaultA with { IsDefault = false },
            DeviceB with { IsDefault = true }
        };

        Assert.Equal(
            MicrophoneTopologyAction.RestartOnDefault,
            MicrophoneLifecyclePolicy.EvaluateTopologyChange(null, false, "endpoint-a", changed));
        Assert.Equal(
            MicrophoneTopologyAction.InventoryChanged,
            MicrophoneLifecyclePolicy.EvaluateTopologyChange("endpoint-a", false, "endpoint-a", changed));
    }

    [Fact]
    public void RemovingAnExplicitEndpointPausesWithoutFallingBack()
    {
        var onlyDefault = new[] { DefaultA };

        Assert.False(MicrophoneSelectionPolicy.IsAvailable("endpoint-b", onlyDefault));
        Assert.Equal(
            MicrophoneTopologyAction.PauseUnavailable,
            MicrophoneLifecyclePolicy.EvaluateTopologyChange(
                "endpoint-b",
                false,
                "endpoint-b",
                onlyDefault));
    }

    [Fact]
    public void ReaddingAnExplicitEndpointDoesNotResumeAStoredPause()
    {
        var restored = new[] { DefaultA, DeviceB };

        Assert.Equal(
            MicrophoneTopologyAction.InventoryChanged,
            MicrophoneLifecyclePolicy.EvaluateTopologyChange(
                "endpoint-b",
                true,
                activeDeviceId: null,
                restored));
    }

    [Theory]
    [InlineData("  ", null)]
    [InlineData(" endpoint-b ", "endpoint-b")]
    public void DeviceIdsAreNormalizedBeforePersistence(string input, string? expected) =>
        Assert.Equal(expected, MicrophoneSelectionPolicy.NormalizeDeviceId(input));

    [Fact]
    public void TrayPresentationNamesPauseAndDisablesStart()
    {
        var state = new AudioCaptureState(
            "endpoint-b",
            "Studio B",
            IsPaused: true,
            IsMonitoring: false,
            IsAvailable: true);

        var presentation = TrayAudioPresentation.From(state);

        Assert.False(presentation.StartEnabled);
        Assert.Contains("пауза", presentation.StartText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Возобновить микрофон", presentation.PauseText);
        Assert.True(presentation.PauseEnabled);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.17, 0)]
    [InlineData(0.18, 1)]
    [InlineData(0.88, 1)]
    [InlineData(0.89, 2)]
    public void LiveLevelUsesThreeStableBands(float level, int expected) =>
        Assert.Equal((AudioLevelBand)expected, AudioLevelBandPolicy.Classify(level));

    [Fact]
    public void SettingsSurfaceHasKeyboardCloseAndNamedAudioControls()
    {
        var xaml = File.ReadAllText(Path.Combine(RepositoryRoot(), "SettingsWindow.xaml"));

        Assert.Contains("Window_OnKeyDown", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Микрофон\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Уровень микрофона\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Track\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_Indicator\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"История\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Сохранять три последние записи", xaml, StringComparison.Ordinal);
        Assert.Contains("Очистить всю историю записей", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"560\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TabStripPlacement=\"Left\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartStopButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TranslationEngineStateText\"", xaml, StringComparison.Ordinal);

        var mainWindow = File.ReadAllText(Path.Combine(RepositoryRoot(), "MainWindow.xaml.cs"));
        Assert.Contains("MessageBoxButton.YesNo", mainWindow, StringComparison.Ordinal);
        Assert.Contains("MessageBoxResult.No", mainWindow, StringComparison.Ordinal);
        Assert.Contains("аудиобуфер очищен", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTrayClickOpensTheSingleControlCenterWithoutLegacyPopup()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services", "TrayService.cs"));

        Assert.Contains("ContextMenuStrip = null", source, StringComparison.Ordinal);
        Assert.Contains("Forms.MouseButtons.Left or Forms.MouseButtons.Right", source, StringComparison.Ordinal);
        Assert.Contains("ShowSettingsWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextMenuStrip = menu", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedRecognitionModelIsActionableOnlyInTheControlCenter()
    {
        var progress = new ModelTransferProgress(
            "GigaAM v3 · ядро",
            1,
            5,
            ModelTransferStage.Failed,
            0,
            318_995_997,
            0,
            0,
            Error: "private diagnostic must not be rendered");

        var presentation = ModelProgressFormatter.ControlCenter(false, progress);

        Assert.True(presentation.IsFailure);
        Assert.True(presentation.CanRetry);
        Assert.False(presentation.ShowProgress);
        Assert.Contains("Повторите загрузку", presentation.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(progress.Error!, presentation.StatusText, StringComparison.Ordinal);

        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "Services", "TrayService.cs"));
        Assert.Contains("центр управления → Распознавание", tray, StringComparison.Ordinal);
        Assert.Contains("BalloonTipClicked", tray, StringComparison.Ordinal);
        Assert.DoesNotContain("Откройте меню и выберите", tray, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Egoist.Voice.csproj")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
