namespace Egoist.Voice;

// Internal test seam: production captures the foreground target at intent and owns the cancel hook.
// Tests supply inert callbacks, so exercising the real window never observes global input.
internal sealed record MainWindowInteractionHooks(
    Func<nint> CaptureForegroundTarget,
    Action ArmCancel,
    Action DisarmCancel,
    Func<bool> ConfirmDeviceSwitch);
