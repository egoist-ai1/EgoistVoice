namespace Egoist.Voice.Core;

public static class RecognitionProgressPolicy
{
    public static bool ShouldRenderEngineProgress(string? label) =>
        string.IsNullOrWhiteSpace(label) ||
        !label.TrimStart().StartsWith("Распознаю", StringComparison.OrdinalIgnoreCase);
}
