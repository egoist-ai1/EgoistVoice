namespace Egoist.Voice.Services;

internal static class CapsuleHidePolicy
{
    internal static bool CanComplete(
        bool hideRequested,
        bool isRecording,
        bool isProcessing,
        bool forceAfterCancellation) =>
        hideRequested && (forceAfterCancellation || (!isRecording && !isProcessing));
}
