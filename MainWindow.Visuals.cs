using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;
using Egoist.Voice.Core;
using Egoist.Voice.Controls;
using Egoist.Voice.Services;

namespace Egoist.Voice;

public partial class MainWindow
{
    private void BuildWaveform() => Waveform.HighContrast = SystemParameters.HighContrast;

    private void OnAudioLevelChanged(object? sender, float level)
    {
        _audioLevelTarget = Math.Clamp(level, 0, 1);
    }

    private void AnimateWaveformFrame(double deltaSeconds = 1d / 60d)
    {
        if (!_isRecording)
        {
            return;
        }

        _audioLevelCurrent = CapsuleWaveformProfile.SmoothLevel(_audioLevelCurrent, _audioLevelTarget, deltaSeconds);
        var frameFactor = Math.Clamp(deltaSeconds * 60, 0.25, 3);
        _wavePhase += (0.09 + (_audioLevelCurrent * 0.14)) * frameFactor;
        Waveform.Advance(_audioLevelCurrent, _wavePhase, deltaSeconds, IsReducedMotion);
        UpdateRecordingTimer();
    }

    private void SetReadyState()
    {
        ApplyVisualStateLayout(new CapsuleVisualState(CapsuleVisualStateKind.Ready, "Готово"));
        StopWaveformAnimation();
        SetStateDisc(System.Windows.Media.Brushes.Transparent);
        SetStateBorder(IdleBorderBrush);
        StateHalo.Opacity = 0;
        StopStateAnimations();
    }

    private void SetListeningState()
    {
        ApplyVisualStateLayout(new CapsuleVisualState(CapsuleVisualStateKind.Listening, CanCancel: true));
        SetStateDisc(ActiveDiscBrush);
        SetStateBorder(ActiveBorderBrush);
        SetWaveform(CapsuleWaveformProfile.MinimumScale);
        StopStateAnimations();
        BeginStateStoryboard("ListenPulseStoryboard");
        _audioLevelCurrent = 0;
        _audioLevelTarget = 0;
        StartWaveformAnimation();
    }

    private void SetProcessingState(string label, double? percentage)
    {
        ApplyVisualStateLayout(new CapsuleVisualState(CapsuleVisualStateKind.Recognizing, label, percentage, percentage is null));
        StopWaveformAnimation();
        SetStateDisc(System.Windows.Media.Brushes.Transparent);
        SetStateBorder(ActiveBorderBrush);
        StateHalo.Opacity = 0;
        StopStateAnimations();
        BeginStateStoryboard("SpinStoryboard");
        ShowCapsule();
    }

    private void ShowSuccess(string label = "Вставлено")
    {
        _sounds.Play(FeedbackSound.TextInserted);
        ApplyVisualStateLayout(new CapsuleVisualState(CapsuleVisualStateKind.Success, label));
        StopWaveformAnimation();
        SetStateDisc(SuccessDiscBrush);
        SetStateBorder(IdleBorderBrush);
        StateHalo.Opacity = 0;
        StopStateAnimations();
        BeginStateStoryboard("SuccessStoryboard");
        ShowCapsule();
        ScheduleHide();
    }

    private void ShowClipboardFallback()
    {
        ApplyVisualStateLayout(new CapsuleVisualState(CapsuleVisualStateKind.Clipboard, "Ctrl+V"));
        StopWaveformAnimation();
        SetStateDisc(SuccessDiscBrush);
        SetStateBorder(IdleBorderBrush);
        StateHalo.Opacity = 0;
        StopStateAnimations();
        BeginStateStoryboard("SuccessStoryboard");
        ShowCapsule();
        ScheduleHide(TimeSpan.FromSeconds(4));
    }

    private void SetModelTransferState(ModelTransferProgress progress)
    {
        ApplyVisualStateLayout(new CapsuleVisualState(CapsuleVisualStateKind.Downloading, ModelProgressFormatter.Capsule(progress), progress.Percentage));
        StopWaveformAnimation();
        CheckIcon.Visibility = progress.Stage == ModelTransferStage.Ready ? Visibility.Visible : Visibility.Collapsed;
        var downloading = progress.Stage == ModelTransferStage.Downloading;
        SpinnerIcon.Visibility = progress.Stage is ModelTransferStage.Verifying or ModelTransferStage.Loading
            ? Visibility.Visible
            : Visibility.Collapsed;
        DownloadIcon.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        ErrorIcon.Visibility = progress.Stage == ModelTransferStage.Failed ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.Visibility = progress.Stage is ModelTransferStage.Downloading or ModelTransferStage.Verifying or ModelTransferStage.Loading
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetStateDisc(progress.Stage == ModelTransferStage.Ready ? SuccessDiscBrush : System.Windows.Media.Brushes.Transparent);
        SetStateBorder(progress.Stage == ModelTransferStage.Failed ? ActiveBorderBrush : IdleBorderBrush);
        StateHalo.Opacity = 0;
        StopStateAnimations();
        if (downloading)
        {
            BeginStateStoryboard("DownloadStoryboard");
        }
        else if (progress.Stage is ModelTransferStage.Verifying or ModelTransferStage.Loading)
        {
            BeginStateStoryboard("SpinStoryboard");
        }
    }

    private void ShowModelsReady()
    {
        ShowSuccess("Модель готова");
        ScheduleHide(TimeSpan.FromSeconds(3));
    }

    private void ShowError(string title)
    {
        _sounds.Play(FeedbackSound.Error);
        ApplyVisualStateLayout(new CapsuleVisualState(CapsuleVisualStateKind.Error, title));
        StopWaveformAnimation();
        _isRecording = false;
        _isProcessing = false;
        SetStateDisc(SuccessDiscBrush);

        // The error outline is amber rather than the brand red, so a failure is distinguishable
        // from an active recording without reading the label.
        SetStateBorder(SystemParameters.HighContrast ? ActiveBorderBrush : ErrorBorderBrush);
        StateHalo.Opacity = 0;
        StopStateAnimations();
        BeginStateStoryboard("ErrorStoryboard");
        ShowCapsule();
        ScheduleHide(TimeSpan.FromSeconds(5));
    }

    private void SetCancelActionVisible(bool visible)
    {
        CloseButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.Opacity = visible ? 1 : 0;
        ActionColumn.Width = visible ? new GridLength(30) : new GridLength(0);
        ActionDividerColumn.Width = visible ? new GridLength(6) : new GridLength(0);
    }

    /// <summary>
    /// Shows the elapsed time once a dictation stops being a quick phrase.
    /// </summary>
    /// <remarks>
    /// Deliberately delayed rather than shown from the first second: on a two-second dictation a
    /// timer is noise, and on a two-minute one it is the difference between "is this still
    /// recording?" and knowing. It doubles as diagnostics — a timer that stopped advancing says
    /// the capture died, which used to be invisible.
    /// </remarks>
    private void UpdateRecordingTimer()
    {
        // The start time is only meaningful once a real capture has begun. Without this guard the
        // preview and diagnostic paths, which never set it, formatted the span since year zero and
        // rendered a nine-digit minute count that shoved the waveform out of the capsule.
        if (!_isRecording || _recordingStartedUtc == default)
        {
            SetRecordingTimerVisible(false);
            return;
        }

        var elapsed = DateTime.UtcNow - _recordingStartedUtc;

        // A hard ceiling on how long one dictation may run. Nothing legitimate reaches it; what
        // does is a trigger stuck in the "pressed" state, and without a stop the recording grows
        // until memory or disk runs out. Stopping normally means the audio is still transcribed.
        if (elapsed > MaximumRecordingDuration)
        {
            AppLog.Write($"Recording exceeded {MaximumRecordingDuration.TotalMinutes:0} minutes; stopping");
            _pushToTalk.Reset();
            _ = StopAndTranscribeAsync();
            return;
        }

        if (elapsed < TimerAppearsAfter || elapsed > MaximumDisplayedDuration)
        {
            SetRecordingTimerVisible(false);
            return;
        }

        RecordingTimer.Text = $"{(int)elapsed.TotalMinutes:0}:{elapsed.Seconds:00}";
        SetRecordingTimerVisible(true);
    }

    private void SetRecordingTimerVisible(bool visible)
    {
        if (visible == _timerVisible)
        {
            return;
        }

        _timerVisible = visible;
        RecordingTimer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        TimerColumn.Width = visible ? GridLength.Auto : new GridLength(0);
    }

    private static readonly TimeSpan TimerAppearsAfter = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Elapsed time the listening preview pretends to be at. Past <see cref="TimerAppearsAfter"/>
    /// so the timer is actually drawn, and two digits wide, which is the common case.
    /// </summary>
    private static readonly TimeSpan PreviewElapsedTime = TimeSpan.FromSeconds(27);

    /// <summary>Beyond this the clock is wrong, not the dictation long.</summary>
    private static readonly TimeSpan MaximumDisplayedDuration = TimeSpan.FromHours(2);

    /// <summary>
    /// Ten minutes is far past any real dictation and far short of anything that hurts. It exists
    /// for the case where the release event was lost, not for the user who talks a lot.
    /// </summary>
    private static readonly TimeSpan MaximumRecordingDuration = TimeSpan.FromMinutes(10);

    private void BeginStateStoryboard(string resourceKey)
    {
        if (!IsReducedMotion)
        {
            ((Storyboard)Resources[resourceKey]).Begin(this, true);
        }
    }

    /// <summary>
    /// Publishes the capsule's state to assistive technology.
    /// </summary>
    /// <remarks>
    /// The window is <c>WS_EX_NOACTIVATE</c> and non-focusable by design — it must never steal
    /// focus from what the user is dictating into — which also means a screen reader will never
    /// land on it by navigation. A polite live region is therefore the only channel: the state is
    /// announced where the user already is, without moving them.
    /// </remarks>
    private void AnnounceState(CapsuleVisualState state)
    {
        var announcement = state.Kind switch
        {
            CapsuleVisualStateKind.Listening => "Запись идёт",
            CapsuleVisualStateKind.Recognizing => state.Progress is null
                ? state.Label ?? "Распознавание"
                : $"{state.Label} {state.Progress:0} процентов",
            CapsuleVisualStateKind.Success => "Текст вставлен",
            CapsuleVisualStateKind.Clipboard => "Текст скопирован, нажмите Ctrl+V",
            CapsuleVisualStateKind.Error => $"Ошибка: {state.Label}",
            CapsuleVisualStateKind.Downloading => state.Label ?? "Загрузка модели",
            _ => "Готово"
        };

        System.Windows.Automation.AutomationProperties.SetName(CapsuleShell, announcement);
        if (_lastAnnouncement == announcement)
        {
            return;
        }

        _lastAnnouncement = announcement;
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(CapsuleShell)
            ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(CapsuleShell);
        peer?.RaiseAutomationEvent(
            System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    private void ApplyVisualStateLayout(CapsuleVisualState state)
    {
        var stateChanged = _lastVisualStateKind != state.Kind;
        _lastVisualStateKind = state.Kind;
        MicIcon.Visibility = state.Kind is CapsuleVisualStateKind.Ready or CapsuleVisualStateKind.Listening
            ? Visibility.Visible : Visibility.Collapsed;
        CheckIcon.Visibility = state.Kind == CapsuleVisualStateKind.Success
            ? Visibility.Visible : Visibility.Collapsed;
        ClipboardIcon.Visibility = state.Kind == CapsuleVisualStateKind.Clipboard
            ? Visibility.Visible : Visibility.Collapsed;
        SpinnerIcon.Visibility = state.Kind == CapsuleVisualStateKind.Recognizing
            ? Visibility.Visible : Visibility.Collapsed;
        DownloadIcon.Visibility = Visibility.Collapsed;
        ErrorIcon.Visibility = state.Kind == CapsuleVisualStateKind.Error
            ? Visibility.Visible : Visibility.Collapsed;
        ProcessingDots.Visibility = state.Kind == CapsuleVisualStateKind.Recognizing && state.Progress is null
            ? Visibility.Visible : Visibility.Collapsed;
        Waveform.Visibility = state.Kind == CapsuleVisualStateKind.Listening
            ? Visibility.Visible : Visibility.Collapsed;
        var showProgress = state.Progress is not null && state.Kind is CapsuleVisualStateKind.Recognizing or CapsuleVisualStateKind.Downloading;
        DownloadProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        if (state.Progress is not null)
        {
            DownloadProgress.Value = state.Progress.Value;
        }
        ProcessingLabel.Text = state.Label ?? string.Empty;
        DetailText.Text = state.Kind == CapsuleVisualStateKind.Recognizing && state.Progress is not null
            ? $"{state.Label} {state.Progress:0}%"
            : state.Label ?? string.Empty;
        DetailText.Visibility = state.Kind == CapsuleVisualStateKind.Listening ||
                                state.Kind == CapsuleVisualStateKind.Recognizing && state.Progress is null
            ? Visibility.Collapsed : Visibility.Visible;
        SetCancelActionVisible(state.CanCancel);
        AnnounceState(state);

        var bodyWidth = state.Kind switch
        {
            CapsuleVisualStateKind.Ready => 218d,
            CapsuleVisualStateKind.Listening => 218d,
            CapsuleVisualStateKind.Recognizing => 232d,
            CapsuleVisualStateKind.Success => 224d,
            CapsuleVisualStateKind.Clipboard => 224d,
            CapsuleVisualStateKind.Downloading => 280d,
            CapsuleVisualStateKind.Error => 232d,
            _ => 232d
        };
        AnimateCapsuleWidth(bodyWidth);

        if (SystemParameters.HighContrast)
        {
            RootBorder.Background = System.Windows.SystemColors.WindowBrush;
            RootBorder.BorderBrush = System.Windows.SystemColors.WindowTextBrush;
            DetailText.Foreground = System.Windows.SystemColors.WindowTextBrush;
            ProcessingLabel.Foreground = System.Windows.SystemColors.WindowTextBrush;
            SetMicStroke(System.Windows.SystemColors.WindowTextBrush);
            SetStroke(CheckIcon, System.Windows.SystemColors.WindowTextBrush);
            ClipboardIcon.Foreground = System.Windows.SystemColors.WindowTextBrush;
            SpinnerIcon.Opacity = 1;
            DownloadIcon.Foreground = System.Windows.SystemColors.HighlightBrush;
            SetStroke(ErrorIcon, System.Windows.SystemColors.HighlightBrush);
            DownloadProgress.Foreground = System.Windows.SystemColors.HighlightBrush;
            DownloadProgress.Background = System.Windows.SystemColors.ControlBrush;
            SurfaceGradient.Visibility = Visibility.Collapsed;
            HoverSurface.Visibility = Visibility.Collapsed;
            SuccessFlash.Visibility = Visibility.Collapsed;
            ShadowSurface.Visibility = Visibility.Collapsed;
        }
        else
        {
            RootBorder.Background = SurfaceBrush;
            DetailText.Foreground = PrimaryTextBrush;
            ProcessingLabel.Foreground = PrimaryTextBrush;
            SetMicStroke(PrimaryTextBrush);
            SetStroke(CheckIcon, PrimaryTextBrush);
            ClipboardIcon.Foreground = PrimaryTextBrush;
            DownloadIcon.Foreground = AccentBrush;
            SetStroke(ErrorIcon, ErrorBrush);
            DownloadProgress.Foreground = AccentBrush;
            DownloadProgress.Background = ProgressTrackBrush;
            SurfaceGradient.Visibility = Visibility.Visible;
            HoverSurface.Visibility = Visibility.Visible;
            SuccessFlash.Visibility = Visibility.Visible;
            ShadowSurface.Visibility = Visibility.Visible;
        }

        if (stateChanged && IsVisible && !IsReducedMotion)
        {
            ((Storyboard)Resources["StateTransitionStoryboard"]).Begin(this, true);
        }
    }

    /// <summary>
    /// Recolours the vector microphone. It is a stroked path rather than a glyph now, so the colour
    /// lives on <c>Stroke</c> and has to be pushed down to the children.
    /// </summary>
    private void SetMicStroke(System.Windows.Media.Brush brush) => SetStroke(MicIcon, brush);

    private static void SetStroke(System.Windows.Controls.Canvas icon, System.Windows.Media.Brush brush)
    {
        foreach (var path in icon.Children.OfType<System.Windows.Shapes.Path>())
        {
            path.Stroke = brush;
        }
    }

    private void SetStateBorder(System.Windows.Media.Brush brush) =>
        RootBorder.BorderBrush = SystemParameters.HighContrast ? System.Windows.SystemColors.WindowTextBrush : brush;

    private void SetStateDisc(System.Windows.Media.Brush brush) =>
        StateDisc.Background = SystemParameters.HighContrast && !ReferenceEquals(brush, System.Windows.Media.Brushes.Transparent)
            ? System.Windows.SystemColors.HighlightBrush
            : brush;

    /// <summary>
    /// Morphs the capsule between state widths.
    /// </summary>
    /// <remarks>
    /// The animation now runs on an element inside a fixed-size window. Animating
    /// <c>Window.Width</c> meant a HWND resize plus a separate, non-atomic <c>SetWindowPos</c> to
    /// re-centre it on every frame — at 144 Hz that is 144 resizes a second, each dragging a full
    /// layout pass and a re-rasterization of the supersampled chrome behind it. The window is
    /// simply large enough for the widest state now, and the body is centred inside it.
    /// </remarks>
    private void AnimateCapsuleWidth(double targetWidth)
    {
        var currentWidth = CapsuleBody.ActualWidth > 0 ? CapsuleBody.ActualWidth : CapsuleBody.Width;
        if (Math.Abs(currentWidth - targetWidth) < 0.1)
        {
            return;
        }

        if (IsReducedMotion || !IsVisible)
        {
            CapsuleBody.BeginAnimation(FrameworkElement.WidthProperty, null);
            CapsuleBody.Width = targetWidth;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = currentWidth,
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        CapsuleBody.BeginAnimation(
            FrameworkElement.WidthProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void StartWaveformAnimation()
    {
        if (_waveRendering) return;
        _lastWaveFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnWaveformRendering;
        _waveRendering = true;
    }

    private void OnWaveformRendering(object? sender, EventArgs args)
    {
        if (args is not RenderingEventArgs rendering)
        {
            return;
        }

        var elapsed = _lastWaveFrame == TimeSpan.Zero
            ? TimeSpan.FromSeconds(1d / 60d)
            : rendering.RenderingTime - _lastWaveFrame;
        if (_lastWaveFrame != TimeSpan.Zero && elapsed < TimeSpan.FromMilliseconds(12))
        {
            return;
        }
        _lastWaveFrame = rendering.RenderingTime;
        AnimateWaveformFrame(Math.Clamp(elapsed.TotalSeconds, 1d / 240d, 0.05));
    }
}
