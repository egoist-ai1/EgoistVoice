using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice;

public partial class MainWindow
{
    public void BeginWarmUp(bool showProgress = true, bool announceModelDownloads = false)
    {
        _announceModelDownloads = announceModelDownloads;
        _ = WarmUpAsync(showProgress);
    }

    private async Task WarmUpAsync(bool showProgress)
    {
        var progress = new Progress<ModelProgress>(value =>
        {
            if (showProgress && !_isRecording && !_isProcessing)
            {
                SetProcessingState(value.Label, value.Percentage);
            }
        });

        try
        {
            await _transcription.WarmUpAsync(progress, _lifetimeCancellation.Token);
            if (showProgress && !_isRecording && !_isProcessing)
            {
                SetReadyState();
                ShowCapsule();
                ScheduleHide();
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutdown.
        }
        catch (Exception exception)
        {
            AppLog.Write("Model warm-up failed", exception);
            if (showProgress && !_isRecording && !_isProcessing)
            {
                ShowError("Модель не готова");
            }
            return;
        }
    }

    private void OnModelProgressChanged(object? sender, ModelTransferProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => HandleModelProgress(progress));
            return;
        }
        HandleModelProgress(progress);
    }

    private void HandleModelProgress(ModelTransferProgress progress)
    {
        _lastModelProgress = progress;
        if (_isRecording || _isProcessing)
        {
            return;
        }

        if (_announceModelDownloads && !_backgroundDownloadAnnounced &&
            progress.Stage is ModelTransferStage.Downloading or ModelTransferStage.Verifying)
        {
            _backgroundDownloadAnnounced = true;
            _displayingBackgroundModelProgress = true;
            SetModelTransferState(progress);
            ShowCapsule();
            ScheduleHide(TimeSpan.FromSeconds(4));
            return;
        }

        if (_displayingBackgroundModelProgress && IsVisible)
        {
            SetModelTransferState(progress);
        }

        if (progress.Stage == ModelTransferStage.Ready && progress.ModelIndex == progress.ModelCount)
        {
            ShowModelsReady();
        }
        else if (progress.Stage == ModelTransferStage.Failed &&
                 progress.ModelName.StartsWith("Whisper", StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Write("Whisper fallback download failed; GigaAM remains available");
            SetReadyState();
            ShowCapsule();
            ScheduleHide();
        }
        else if (progress.Stage == ModelTransferStage.Failed)
        {
            ShowError("Модель не загружена");
        }
    }

    public void ShowModelDownloadStatus()
    {
        _displayingBackgroundModelProgress = true;
        if (_lastModelProgress is not null)
        {
            SetModelTransferState(_lastModelProgress);
        }
        else if (_modelManager.AreAllModelsReady)
        {
            ShowModelsReady();
            return;
        }
        else
        {
            SetProcessingState("Готовлю загрузку", null);
        }
        ShowCapsule();
        ScheduleHide(TimeSpan.FromSeconds(7));
    }

    public void RetryModelDownloads()
    {
        _displayingBackgroundModelProgress = true;
        ShowModelDownloadStatus();
        _ = WarmUpAsync(showProgress: true);
    }
}
