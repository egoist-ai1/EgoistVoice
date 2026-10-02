using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class TrayCaptureStateTests
{
    [Fact]
    public void TransientDeviceLossAllowsRetryWithoutPretendingTheUserPaused()
    {
        var state = new AudioCaptureState(null, "Системный", true, false, false)
            { IsTransientlyUnavailable = true };
        var presentation = TrayAudioPresentation.From(state);
        Assert.False(state.IsUserPaused);
        Assert.True(presentation.StartEnabled);
        Assert.True(presentation.PauseEnabled);
        Assert.Equal("Приостановить микрофон", presentation.PauseText);
    }

    [Fact]
    public void ManualPauseCanBeResumedWhileTheEndpointIsMissing()
    {
        var state = new AudioCaptureState("missing", "Выбранный", true, false, false);
        var presentation = TrayAudioPresentation.From(state);
        Assert.True(state.IsUserPaused);
        Assert.False(presentation.StartEnabled);
        Assert.True(presentation.PauseEnabled);
        Assert.Equal("Возобновить микрофон", presentation.PauseText);
    }

    [Fact]
    public void ActorBusyStatePreventsAnotherStartButPendingTakeStillAllowsStop()
    {
        var state = new AudioCaptureState(null, "Системный", false, true, true);
        Assert.False(TrayAudioPresentation.From(state, canStartRecording: false).StartEnabled);
        var recording = TrayAudioPresentation.From(state, canStartRecording: false, isRecording: true);
        Assert.True(recording.StartEnabled);
        Assert.Equal("Остановить диктовку", recording.StartText);
    }
}
