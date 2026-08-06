using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Egoist.Voice.Controls;
using Egoist.Voice.Core;
using Egoist.Voice.Services;
using Microsoft.Win32;

namespace Egoist.Voice;

public partial class MainWindow : Window, IDisposable
{
    // Всё, что раньше было залитыми дисками под иконками, теперь прозрачно. Залитый круг —
    // единственная плотная фигура на капсуле, где всё остальное нарисовано линией: он спорил и с
    // волосяной кривой волны, и с тонким контуром пилюли. Состояние теперь читается по свечению
    // и по цвету контура, а не по плашке под иконкой.
    private static readonly SolidColorBrush ActiveDiscBrush = FrozenBrush("#00000000");
    private static readonly SolidColorBrush SuccessDiscBrush = FrozenBrush("#00000000");
    private static readonly SolidColorBrush IdleBorderBrush = FrozenBrush("#2A2A30");

    /// <summary>
    /// Контур записи. Красный больше не идёт ровной яркой линией по всему периметру — он вспыхивает
    /// в середине и растворяется к обоим концам. Ровный контур на такой толщине выглядел резко и
    /// при этом сливался в одну полосу с краем пилюли.
    /// </summary>
    private static readonly System.Windows.Media.Brush ActiveBorderBrush = CreateDissolvingAccent("#FF3846", 0.86);

    /// <summary>Тот же приём, но приглушённый: распознавание — работа, а не сигнал.</summary>
    private static readonly System.Windows.Media.Brush RefiningBorderBrush = CreateDissolvingAccent("#FF3846", 0.44);
    private static readonly SolidColorBrush SurfaceBrush = FrozenBrush("#08080A");
    private static readonly SolidColorBrush PrimaryTextBrush = FrozenBrush("#F7F7F8");
    private static readonly SolidColorBrush AccentBrush = FrozenBrush("#FF2634");
    // Amber, not red. The brand accent #FF2634 marks normal operation — recording, progress,
    // hover — while #FF4450 marked failure, and the two were indistinguishable at a glance. Error
    // now has a colour of its own, and the brand keeps its meaning.
    private static readonly SolidColorBrush ErrorBrush = FrozenBrush("#F59E0B");

    /// <summary>
    /// Растворяется так же, как контур записи, но с более высоким пиком: отказ должен быть заметнее
    /// нормальной работы, при этом оставаясь в той же визуальной грамматике.
    /// </summary>
    private static readonly System.Windows.Media.Brush ErrorBorderBrush = CreateDissolvingAccent("#F59E0B", 0.92);
    private static readonly SolidColorBrush ProgressTrackBrush = FrozenBrush("#25252A");

    private readonly IAudioCaptureService _audioCapture;
    private readonly ITranscriptionService _transcription;
    private readonly DictationDeliveryService _delivery;
    private readonly IModelManager _modelManager;
    private readonly CapsulePositionService _positionService = new();
    private readonly DictationSettingsService _settingsService = new();
    private readonly FeedbackSoundService _sounds = new();
    private readonly CancelKeyWatcher _cancelKey = new();
    private TranscriptPostProcessor _postProcessor = new();
    private bool _mixedLanguageMode;

    // Голосовая команда «переведи …»: локальный переводчик EGOIST (HY-MT1.5).
    private readonly TranslatorClient _translator = new();
    private readonly ActivationSettingsService _activationSettings = new();
    private readonly DispatcherTimer _hideTimer = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly PushToTalkCoordinator _pushToTalk = new();
    private Storyboard _exitStoryboard = null!;
    private GlobalHotkeyService? _hotkey;
    private KeyboardShortcut? _keyboardShortcut;
    private MousePushToTalkService? _mouseHotkey;
    private MouseSideButton? _mouseButton;
    private ActivationConfiguration _activationConfiguration = ActivationConfiguration.Default;
    private CancellationTokenSource? _operationCancellation;
    private nint _targetWindow;
    private DateTime _recordingStartedUtc;
    private double _wavePhase;
    private double _audioLevelCurrent;
    private volatile float _audioLevelTarget;
    private bool _positionInitialized;
    private bool _hideRequested;
    private bool _forceHideAfterCancellation;
    private bool _isRecording;
    private bool _isProcessing;
    private bool _announceModelDownloads;
    private bool _backgroundDownloadAnnounced;
    private bool _displayingBackgroundModelProgress;
    private ModelTransferProgress? _lastModelProgress;
    private bool _disposed;
    private bool _waveRendering;
    private bool _activationCaptureActive;
    private TimeSpan _lastWaveFrame;
    private CapsuleVisualStateKind? _lastVisualStateKind;
    private string? _lastAnnouncement;
    private bool _timerVisible;
    private static bool IsReducedMotion => !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast;

    /// <summary>Window width up to and including 1.6.5, used to re-centre positions saved back then.</summary>
    private const double LegacyWindowWidth = 242;

    public MainWindow(
        IAudioCaptureService audioCapture,
        ITranscriptionService transcription,
        DictationDeliveryService delivery,
        IModelManager modelManager)
    {
        InitializeComponent();
        _audioCapture = audioCapture;
        _transcription = transcription;
        _delivery = delivery;
        _modelManager = modelManager;
        ApplyDictationSettings();

        BuildWaveform();
        _audioCapture.LevelChanged += OnAudioLevelChanged;
        _modelManager.ProgressChanged += OnModelProgressChanged;
        _exitStoryboard = (Storyboard)Resources["ExitStoryboard"];
        _exitStoryboard.Completed += (_, _) =>
        {
            if (CapsuleHidePolicy.CanComplete(
                    _hideRequested,
                    _isRecording,
                    _isProcessing,
                    _forceHideAfterCancellation))
            {
                _hideRequested = false;
                _forceHideAfterCancellation = false;
                _displayingBackgroundModelProgress = false;
                Hide();
            }
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_isRecording && !_isProcessing)
            {
                HideCapsuleAnimated();
            }
        };

        SourceInitialized += (_, _) =>
        {
            AppLog.Write("Capsule SourceInitialized");
            NativeMethods.MakeWindowNonActivating(new WindowInteropHelper(this).Handle);
            InitializeCapsulePosition();
        };

        // Without this the capsule stays on a monitor that no longer exists — the previous
        // clamp only ran on the next show, which may never come if the capsule is off-screen.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _cancelKey.Cancelled += OnCancelKeyPressed;
        Loaded += (_, _) => AppLog.Write("Capsule Loaded");
    }

    public event EventHandler? ActivationBindingChanged;

    public ActivationBinding CurrentActivationBinding => _activationConfiguration.Binding;

    public KeyboardShortcut? CurrentCustomShortcut => _activationConfiguration.CustomShortcut;

    public string CurrentActivationDisplayName => ActivationBindingInfo.DisplayName(_activationConfiguration);

    public void SetActivationCaptureActive(bool active)
    {
        _activationCaptureActive = active;
        if (active)
        {
            _pushToTalk.Reset();
        }
    }

    public void InitializeHotkey()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        AppLog.Write($"InitializeHotkey handle=0x{handle:X}");
        NativeMethods.MakeWindowNonActivating(handle);

        var requested = _activationSettings.Load();
        if (TryApplyActivationBinding(requested, persist: false, out var error))
        {
            return;
        }

        AppLog.Write($"Requested activation binding unavailable: {error}");
        foreach (var fallback in new[] { ActivationBinding.Mouse5, ActivationBinding.Keyboard })
        {
            if (TryApplyActivationBinding(requested.WithBinding(fallback), persist: false, out _))
            {
                return;
            }
        }

        throw new InvalidOperationException("Не удалось подключить ни Mouse 5, ни Ctrl + Alt + Space.");
    }

    public bool TrySetActivationBinding(ActivationBinding binding, out string? error) =>
        TryApplyActivationBinding(_activationConfiguration.WithBinding(binding), persist: true, out error);

    public bool TrySetCustomShortcut(KeyboardShortcut shortcut, out string? error) =>
        TryApplyActivationBinding(
            _activationConfiguration with
            {
                Binding = ActivationBinding.CustomKeyboard,
                CustomShortcut = shortcut
            },
            persist: true,
            out error);

    private bool TryApplyActivationBinding(ActivationConfiguration configuration, bool persist, out string? error)
    {
        var previousConfiguration = _activationConfiguration;
        var settingsChanged = false;
        var desiredKeyboard = ActivationBindingInfo.Keyboard(configuration);
        var desiredMouse = ActivationBindingInfo.MouseButton(configuration.Binding);
        GlobalHotkeyService? createdKeyboard = null;
        MousePushToTalkService? createdMouse = null;
        try
        {
            if (configuration.Binding == ActivationBinding.CustomKeyboard && desiredKeyboard is not { IsValid: true })
            {
                throw new InvalidDataException("Сначала задайте пользовательскую горячую клавишу.");
            }

            if (desiredKeyboard is not null && (_hotkey is null || _keyboardShortcut != desiredKeyboard))
            {
                var handle = new WindowInteropHelper(this).EnsureHandle();
                createdKeyboard = new GlobalHotkeyService(handle, desiredKeyboard.Value);
                createdKeyboard.Pressed += OnHotkeyPressed;
                createdKeyboard.Released += OnHotkeyReleased;
            }

            if (desiredMouse is not null && (_mouseHotkey is null || _mouseButton != desiredMouse))
            {
                createdMouse = new MousePushToTalkService(desiredMouse.Value);
                createdMouse.Pressed += OnMouseHotkeyPressed;
                createdMouse.Released += OnMouseHotkeyReleased;
            }

            // Persist before swapping live hooks. If the atomic settings write fails,
            // the existing working binding remains untouched.
            if (persist)
            {
                _activationSettings.Save(configuration);
                settingsChanged = true;
            }

            var oldKeyboard = _hotkey;
            var oldMouse = _mouseHotkey;
            if (createdKeyboard is not null)
            {
                _hotkey = createdKeyboard;
                _keyboardShortcut = desiredKeyboard;
                createdKeyboard = null;
            }
            if (createdMouse is not null)
            {
                _mouseHotkey = createdMouse;
                _mouseButton = desiredMouse;
                createdMouse = null;
            }

            if (desiredKeyboard is null && oldKeyboard is not null)
            {
                oldKeyboard.Pressed -= OnHotkeyPressed;
                oldKeyboard.Released -= OnHotkeyReleased;
                oldKeyboard.Dispose();
                _hotkey = null;
                _keyboardShortcut = null;
            }
            else if (desiredKeyboard is not null && oldKeyboard is not null && !ReferenceEquals(oldKeyboard, _hotkey))
            {
                oldKeyboard.Pressed -= OnHotkeyPressed;
                oldKeyboard.Released -= OnHotkeyReleased;
                oldKeyboard.Dispose();
            }
            if (desiredMouse is null && oldMouse is not null)
            {
                oldMouse.Pressed -= OnMouseHotkeyPressed;
                oldMouse.Released -= OnMouseHotkeyReleased;
                oldMouse.Dispose();
                _mouseHotkey = null;
                _mouseButton = null;
            }
            else if (desiredMouse is not null && oldMouse is not null && !ReferenceEquals(oldMouse, _mouseHotkey))
            {
                oldMouse.Pressed -= OnMouseHotkeyPressed;
                oldMouse.Released -= OnMouseHotkeyReleased;
                oldMouse.Dispose();
            }

            _pushToTalk.Reset();
            _activationConfiguration = configuration;
            ActivationBindingChanged?.Invoke(this, EventArgs.Empty);
            AppLog.Write($"Activation binding changed: {ActivationBindingInfo.DisplayName(configuration)}");
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            if (settingsChanged)
            {
                try
                {
                    _activationSettings.Save(previousConfiguration);
                }
                catch (Exception rollbackException)
                {
                    AppLog.Write("Activation settings rollback failed", rollbackException);
                }
            }
            if (createdKeyboard is not null)
            {
                createdKeyboard.Pressed -= OnHotkeyPressed;
                createdKeyboard.Released -= OnHotkeyReleased;
                createdKeyboard.Dispose();
            }
            if (createdMouse is not null)
            {
                createdMouse.Pressed -= OnMouseHotkeyPressed;
                createdMouse.Released -= OnMouseHotkeyReleased;
                createdMouse.Dispose();
            }
            error = exception.Message;
            AppLog.Write($"Activation binding rejected: {ActivationBindingInfo.DisplayName(configuration)}", exception);
            return false;
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs args)
    {
        if (_activationCaptureActive)
        {
            return;
        }
        BeginPushToTalk(PushToTalkSource.Keyboard);
    }

    private async void OnHotkeyReleased(object? sender, EventArgs args)
    {
        if (_activationCaptureActive)
        {
            return;
        }
        await EndPushToTalkAsync(PushToTalkSource.Keyboard);
    }

    private void OnMouseHotkeyPressed(object? sender, EventArgs args)
    {
        if (_activationCaptureActive)
        {
            return;
        }
        BeginPushToTalk(PushToTalkSource.Mouse);
    }

    private async void OnMouseHotkeyReleased(object? sender, EventArgs args)
    {
        if (_activationCaptureActive)
        {
            return;
        }
        await EndPushToTalkAsync(PushToTalkSource.Mouse);
    }

    private void BeginPushToTalk(PushToTalkSource source)
    {
        if (_pushToTalk.Press(source) && !_isRecording && !_isProcessing)
        {
            StartRecording();
        }
    }

    private async Task EndPushToTalkAsync(PushToTalkSource source)
    {
        if (_pushToTalk.Release(source) && _isRecording && !_isProcessing)
        {
            await StopAndTranscribeAsync();
        }
    }

    public void ShowReadyBriefly()
    {
        SetReadyState();
        ShowCapsule();
        ScheduleHide();
    }

    public void ShowListeningPreview()
    {
        _isRecording = true;
        SetListeningState();

        // Backdate the start so the preview renders the timer instead of an empty slot. Without
        // this the visual regression never exercised the digits at all, which is precisely the
        // element most likely to collide with the waveform when either one is resized.
        _recordingStartedUtc = DateTime.UtcNow - PreviewElapsedTime;
        _audioLevelTarget = 0.72f;
        for (var frame = 0; frame < 18; frame++)
        {
            AnimateWaveformFrame();
        }

        UpdateRecordingTimer();
        ShowCapsule();
    }

    public void ShowStatePreview(string state)
    {
        switch (state.ToLowerInvariant())
        {
            case "ready":
                SetReadyState();
                ShowCapsule();
                break;
            case "listening":
                ShowListeningPreview();
                break;
            case "processing":
            case "recognizing":
                _isProcessing = true;
                SetProcessingState("Распознаю", null);
                break;
            case "success":
                ShowSuccess();
                break;
            case "clipboard":
                ShowClipboardFallback();
                break;
            case "error":
                ShowError("Не услышал");
                break;
            case "download":
                SetModelTransferState(new ModelTransferProgress(
                    "GigaAM v3 · ядро", 1, 5, ModelTransferStage.Downloading,
                    133_978_319, 318_995_997, 42, 14, 38_000_000, TimeSpan.FromSeconds(5)));
                break;
            default:
                ShowListeningPreview();
                break;
        }
    }

    public void RenderPreview(string outputPath)
    {
        // RenderTargetBitmap can sample separate animated WPF layers between
        // composition ticks. Freeze diagnostic previews on one coherent frame;
        // this path is used only by visual QA and never changes runtime motion.
        StopStateAnimations();
        ((Storyboard)Resources["EnterStoryboard"]).Stop(this);
        _exitStoryboard.Stop(this);
        BeginAnimation(WidthProperty, null);
        CapsuleShell.Opacity = 1;
        ShadowSurface.Opacity = 1;
        ShellScale.ScaleX = 1;
        ShellScale.ScaleY = 1;
        ShellTranslate.Y = 0;
        CenterContent.Opacity = 1;
        StateContentTranslate.X = 0;
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(this);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    public async Task ToggleRecordingAsync()
    {
        if (_isProcessing)
        {
            return;
        }

        if (_isRecording)
        {
            await StopAndTranscribeAsync();
        }
        else
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        AppLog.Write("StartRecording requested");
        _forceHideAfterCancellation = false;
        _hideTimer.Stop();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _targetWindow = NativeMethods.GetForegroundWindow();
        _recordingStartedUtc = DateTime.UtcNow;

        try
        {
            _audioCapture.Start();
            AppLog.Write($"Audio capture started, target=0x{_targetWindow:X}");
            _sounds.Play(FeedbackSound.RecordingStarted);

            // The keyboard hook is armed only for the duration of a dictation: a dictation tool
            // has no business watching every keystroke of the session.
            _cancelKey.Arm();
            _isRecording = true;
            SetListeningState();
            ShowCapsule();
        }
        catch (Exception exception)
        {
            AppLog.Write("StartRecording failed", exception);
            ShowError(GetMicrophoneError(exception));
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        AppLog.Write($"StopAndTranscribe requested, held={(DateTime.UtcNow - _recordingStartedUtc).TotalSeconds:0.00}s");
        _isRecording = false;
        _isProcessing = true;
        _sounds.Play(FeedbackSound.RecordingStopped);
        SetProcessingState("Распознаю", null);
        var cancellationToken = _operationCancellation?.Token ?? CancellationToken.None;
        var trace = new DictationTrace();
        trace.Mark(DictationStage.CaptureStarted);
        string? audioPath = null;

        try
        {
            var capture = await _audioCapture.StopAsync(cancellationToken);
            trace.Mark(DictationStage.CaptureStopped);
            audioPath = capture.Path;
            AppLog.Write(
                $"Audio capture stopped: samples={capture.Samples.Length}, " +
                $"duration={capture.Duration.TotalSeconds:0.00}s, speech={capture.DetectedSpeech.TotalSeconds:0.00}s, " +
                $"peak={capture.PeakDecibels:0.0}dBFS");
            trace.Mark(DictationStage.SpeechChecked);
            if (!capture.HasSpeech)
            {
                AppLog.Write($"No speech detected ({capture.RejectionMessage ?? "unspecified"}); delivery skipped");
                _isProcessing = false;

                // Silence used to be indistinguishable from a broken microphone: the capsule simply
                // disappeared. Say which one it was.
                if (capture.RejectionMessage is { Length: > 0 } reason)
                {
                    ShowError(reason);
                }
                else
                {
                    HideCapsuleAnimated();
                }
                return;
            }
            var progress = new Progress<ModelProgress>(value =>
            {
                if (!cancellationToken.IsCancellationRequested &&
                    RecognitionProgressPolicy.ShouldRenderEngineProgress(value.Label))
                {
                    // Recognition already owns a continuous orbit state. Chunk
                    // counts and percentages are engine details; repainting the
                    // state for every long-form chunk also restarts its motion.
                    SetProcessingState(value.Label, value.Percentage);
                }
            });
            var result = _transcription is ISampleTranscriptionService sampleTranscription
                ? await sampleTranscription.TranscribeSamplesAsync(
                    capture.Samples, capture.SampleRate, cancellationToken)
                : audioPath is not null
                    ? await _transcription.TranscribeAsync(audioPath, progress, cancellationToken)
                    : throw new NotSupportedException("Движок не поддерживает распознавание из памяти.");
            trace.Mark(DictationStage.PrimaryDecoded);
            var entityProfile = EntityProfilePolicy.ResolveForWindow(
                _targetWindow,
                result.Text,
                _mixedLanguageMode);
            var text = _postProcessor.Process(result.Text, entityProfile);
            trace.Mark(DictationStage.TextFormatted);
            AppLog.Write($"Transcription complete: characters={text.Length}, elapsed={result.Elapsed.TotalSeconds:0.00}s");

            if (string.IsNullOrWhiteSpace(text))
            {
                ShowError("Не услышал");
                return;
            }

            // Голосовая команда «переведи …» / «… переведи на немецкий» идёт
            // только через проверенный current-user Engine Host. При ошибке
            // ничего не вставляем: оригинал нельзя выдавать за успешный перевод.
            var directive = TranslateCommandParser.TryParse(text);
            if (directive is not null)
            {
                AppLog.Write($"Команда перевода: → {directive.TargetLanguage}, {directive.Payload.Length} симв.");
                SetProcessingState("Перевожу", null);
                var translation = await _translator.TranslateAsync(
                    directive.Payload,
                    directive.TargetLanguage,
                    label => Dispatcher.Invoke(() => SetProcessingState(label, null)),
                    cancellationToken);

                if (translation.Succeeded)
                {
                    text = translation.Text!;
                    AppLog.Write($"Перевод готов: {text.Length} симв.");
                }
                else
                {
                    AppLog.Write($"Перевод не вставлен: {translation.Failure}");
                    ShowError(translation.UserMessage);
                    return;
                }
            }

            var deliveryResult = await _delivery.DeliverAsync(text, _targetWindow, cancellationToken);
            trace.Mark(DictationStage.Delivered);
            AppLog.Write($"Dictation timing: {trace.Format()}");
            switch (deliveryResult.Status)
            {
                case DictationDeliveryStatus.Inserted:
                    ShowSuccess("Вставлено");
                    break;
                case DictationDeliveryStatus.ClipboardFallback:
                    ShowClipboardFallback();
                    break;
                case DictationDeliveryStatus.ClipboardFailed:
                    ShowError("Буфер занят");
                    break;

                // Без этой ветки капсула оставалась в состоянии «Распознаю» навсегда: ни один из
                // показов не вызывался, а значит не вызывался и ScheduleHide. Пользователь при
                // этом вообще не узнавал, почему текст не появился.
                case DictationDeliveryStatus.SuppressedForSensitiveTarget:
                    ShowError("Не вставляю в пароли");
                    break;

                default:
                    AppLog.Write($"Unhandled delivery status: {deliveryResult.Status}");
                    ShowError("Ошибка");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Write("Recording operation cancelled");
            SetReadyState();
            ScheduleHide();
        }
        catch (Exception exception)
        {
            AppLog.Write("StopAndTranscribe failed", exception);
            ShowError("Ошибка");
        }
        finally
        {
            _isProcessing = false;
            _cancelKey.Disarm();

            // Diagnostic/corpus mode can still return an explicit temporary WAV. Normal dictation
            // is memory-only, so cancellation normally has no path to resolve or delete.
            audioPath ??= await TryResolveDiscardedRecordingAsync();
            if (audioPath is not null)
            {
                TryDelete(audioPath);
            }
        }
    }

    private async Task<string?> TryResolveDiscardedRecordingAsync()
    {
        try
        {
            return await _audioCapture.CancelAsync();
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not resolve discarded recording path", exception);
            return null;
        }
    }

    private async void CloseButton_OnClick(object sender, RoutedEventArgs e) => await CancelDictationAsync();

    private async void OnCancelKeyPressed(object? sender, EventArgs e)
    {
        AppLog.Write("Dictation cancelled with the cancel key");
        await CancelDictationAsync();
    }

    private async Task CancelDictationAsync()
    {
        _cancelKey.Disarm();
        _operationCancellation?.Cancel();
        _pushToTalk.Reset();

        // Checked separately from _isRecording: during recognition that flag is already false, so
        // the old condition silently skipped the discard and the temporary file survived.
        if (_isRecording || _isProcessing)
        {
            _isRecording = false;
            var discardedPath = await CancelCaptureAsync();
            TryDelete(discardedPath);
        }

        HideCapsuleAnimated(forceAfterCancellation: true);
    }

    private async Task<string?> CancelCaptureAsync()
    {
        try
        {
            return await _audioCapture.CancelAsync();
        }
        catch (Exception exception)
        {
            AppLog.Write("Cancel of the active capture failed", exception);
            return null;
        }
    }

    private void RootBorder_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        e.Handled = true;
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.BeginWindowDrag(handle);
        KeepCapsuleOnScreen();
        _positionService.Save(Left, Top, Width);
        AppLog.Write($"Capsule moved: left={Left:0}, top={Top:0}");
    }

    private void ShowCapsule()
    {
        if (_forceHideAfterCancellation)
        {
            return;
        }

        var foregroundBefore = NativeMethods.GetForegroundWindow();
        KeepCapsuleOnScreen();
        _hideRequested = false;
        _forceHideAfterCancellation = false;
        _exitStoryboard.Stop(this);
        var wasVisible = IsVisible;
        var handle = new WindowInteropHelper(this).Handle;
        if (!IsVisible)
        {
            Show();
            NativeMethods.ShowWithoutActivation(handle);

            // Re-apply position after Show(): a WM_DPICHANGED raised while the window becomes
            // visible on a differently scaled monitor overwrites the placement set above.
            NativeMethods.TryClampToMonitorWorkArea(handle);
        }

        NativeMethods.ReassertTopmost(handle);
        if (!wasVisible)
        {
            if (IsReducedMotion)
            {
                CapsuleShell.Opacity = 1;
                ShadowSurface.Opacity = 1;
                ShellScale.ScaleX = 1;
                ShellScale.ScaleY = 1;
                ShellTranslate.Y = 0;
            }
            else
            {
                ((Storyboard)Resources["EnterStoryboard"]).Begin(this, true);
            }
        }
        AppLog.Write($"Capsule shown: foregroundBefore=0x{foregroundBefore:X}, foregroundAfter=0x{NativeMethods.GetForegroundWindow():X}");
    }

    private void HideCapsuleAnimated(bool forceAfterCancellation = false)
    {
        if (!IsVisible || _hideRequested)
        {
            return;
        }

        _hideRequested = true;
        _forceHideAfterCancellation = forceAfterCancellation;
        StopWaveformAnimation();
        if (IsReducedMotion)
        {
            _hideRequested = false;
            _forceHideAfterCancellation = false;
            _displayingBackgroundModelProgress = false;
            Hide();
        }
        else
        {
            _exitStoryboard.Begin(this, true);
        }
    }

    private void InitializeCapsulePosition()
    {
        if (_positionInitialized)
        {
            return;
        }

        var saved = _positionService.Load();
        if (saved is null)
        {
            var workArea = SystemParameters.WorkArea;
            saved = new CapsulePosition(
                workArea.Left + ((workArea.Width - Width) / 2),
                workArea.Bottom - Height - 20,
                Width);
        }
        else
        {
            saved = CapsulePositionService.Recentre(saved, Width, LegacyWindowWidth);
        }

        var clamped = ClampToVirtualScreen(saved);
        Left = clamped.Left;
        Top = clamped.Top;
        _positionInitialized = true;
    }

    private void KeepCapsuleOnScreen()
    {
        InitializeCapsulePosition();
        if (NativeMethods.TryClampToMonitorWorkArea(new WindowInteropHelper(this).Handle))
        {
            // SetWindowPos already moved the window; WPF picks Left/Top up from the position
            // change, so no second, competing assignment is needed here.
            return;
        }

        var clamped = ClampToVirtualScreen(new CapsulePosition(Left, Top));
        Left = clamped.Left;
        Top = clamped.Top;
    }

    private CapsulePosition ClampToVirtualScreen(CapsulePosition position) => CapsulePositionService.Clamp(
        position,
        Width,
        Height,
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// Rebuilds the post-processing pipeline from disk. Called at start-up and whenever the user
    /// edits the dictionary, so a new term takes effect without restarting the application.
    /// </summary>
    public void ApplyDictationSettings()
    {
        var settings = _settingsService.Load();
        var dictionary = _settingsService.LoadDictionary();
        _postProcessor = new TranscriptPostProcessor(dictionary, settings.ToPostProcessingOptions());
        _mixedLanguageMode = settings.MixedLanguageMode;
        _delivery.RestoreClipboard = settings.RestoreClipboard;
        _sounds.Enabled = settings.SoundFeedback;
        _sounds.Volume = settings.SoundVolume;
        _sounds.Invalidate();

        if (_transcription is HybridTranscriptionService hybrid)
        {
            hybrid.MixedLanguageMode = settings.MixedLanguageMode;

            // Every dictionary term also becomes a suspicion for the mixed-speech detector, so a
            // user-added word starts pulling in the fallback without a second list to maintain.
            // The built-in entries are included: they are the terms most likely to appear.
            hybrid.UpdateVocabulary(dictionary.SpokenForms);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_disposed || !_positionInitialized)
            {
                return;
            }
            KeepCapsuleOnScreen();
            _positionService.Save(Left, Top, Width);
        });
    }

    /// <summary>
    /// PerMonitorV2 means the capsule really does change scale when it crosses monitors, so the
    /// supersampled chrome has to be re-rasterized and the placement re-checked.
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_disposed)
        {
            return;
        }

        AppLog.Write($"Capsule DPI changed: {oldDpi.DpiScaleX:0.##} -> {newDpi.DpiScaleX:0.##}");
        InvalidateCapsuleChrome();

        // Deferred: Windows sends WM_DPICHANGED with a suggested rectangle and expects WPF to
        // apply it. Calling SetWindowPos from inside the same handler fights that placement and
        // can flip the window back and forth between two monitors.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!_disposed)
            {
                KeepCapsuleOnScreen();
            }
        });
    }

    /// <summary>
    /// Forces the supersampled chrome to re-rasterize. Only <see cref="RootBorder"/> needs it now:
    /// the shadow is a blurred effect rather than a stack of hairline borders, and the effect
    /// scales with the visual on its own.
    /// </summary>
    private void InvalidateCapsuleChrome() => RootBorder.InvalidateVisual();

    private void ScheduleHide(TimeSpan? delay = null)
    {
        _hideTimer.Stop();
        _hideTimer.Interval = delay ?? TimeSpan.FromSeconds(2.2);
        _hideTimer.Start();
    }

    private void StopStateAnimations()
    {
        ((Storyboard)Resources["SpinStoryboard"]).Stop(this);
        ((Storyboard)Resources["ListenPulseStoryboard"]).Stop(this);
        ((Storyboard)Resources["SuccessStoryboard"]).Stop(this);
        ((Storyboard)Resources["ErrorStoryboard"]).Stop(this);
        ((Storyboard)Resources["DownloadStoryboard"]).Stop(this);
    }

    private void StopWaveformAnimation()
    {
        if (_waveRendering)
        {
            CompositionTarget.Rendering -= OnWaveformRendering;
            _waveRendering = false;
        }
        _audioLevelTarget = 0;
        _audioLevelCurrent = 0;
    }

    private void SetWaveform(double scaleY)
    {
        Waveform.HighContrast = SystemParameters.HighContrast;
        Waveform.SetUniformScale(scaleY);
    }

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    /// <summary>
    /// Turns a capture failure into something the user can act on.
    /// </summary>
    /// <remarks>
    /// This existed but was never called: every failure showed "Нет микрофона", including a denied
    /// microphone permission and a denied write to %LOCALAPPDATA%. Somebody whose disk permissions
    /// are wrong was being told to check their microphone.
    /// </remarks>
    private static string GetMicrophoneError(Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return "Нет доступа к папке приложения";
        }

        if (exception is InvalidOperationException && exception.Message.Contains("уже запущена", StringComparison.Ordinal))
        {
            return "Запись уже идёт";
        }

        var message = exception.Message;
        return message.Contains("NoDriver", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Access", StringComparison.OrdinalIgnoreCase)
            ? "Разрешите доступ к микрофону"
            : "Нет микрофона";
    }

    private static SolidColorBrush FrozenBrush(string color)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Строит горизонтальный градиент, у которого акцент разгорается к середине и уходит в ноль на
    /// концах. Применяется как кисть пера контура: перо с градиентом даёт линию переменной
    /// прозрачности, чего не получить сплошным цветом.
    /// </summary>
    private static System.Windows.Media.Brush CreateDissolvingAccent(string color, double peakOpacity)
    {
        var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        System.Windows.Media.Color At(double opacity) => System.Windows.Media.Color.FromArgb(
            (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255),
            accent.R,
            accent.G,
            accent.B);

        var brush = new System.Windows.Media.LinearGradientBrush(
            new System.Windows.Media.GradientStopCollection
            {
                new(At(0), 0),
                new(At(peakOpacity * 0.28), 0.16),
                new(At(peakOpacity), 0.5),
                new(At(peakOpacity * 0.28), 0.84),
                new(At(0), 1)
            },
            new System.Windows.Point(0, 0.5),
            new System.Windows.Point(1, 0.5));
        brush.Freeze();
        return brush;
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary audio is also cleared on the next startup.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _cancelKey.Cancelled -= OnCancelKeyPressed;
        _cancelKey.Dispose();
        _hideTimer.Stop();
        StopWaveformAnimation();
        if (_positionInitialized)
        {
            _positionService.Save(Left, Top, Width);
        }
        _operationCancellation?.Cancel();
        _pushToTalk.Reset();
        _lifetimeCancellation.Cancel();
        _operationCancellation?.Dispose();
        _lifetimeCancellation.Dispose();
        _hotkey?.Dispose();
        _mouseHotkey?.Dispose();
        _modelManager.ProgressChanged -= OnModelProgressChanged;
        _sounds.Dispose();
        _audioCapture.Dispose();
        _transcription.Dispose();
        _translator.Dispose();
        _modelManager.Dispose();
    }
}
