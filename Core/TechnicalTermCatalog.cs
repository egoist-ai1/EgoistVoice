namespace Egoist.Voice.Core;

internal static class TechnicalTermCatalog
{
    /// <summary>
    /// Terms the candidate selector treats as evidence that the fallback engine preserved English
    /// the primary one lost. Kept broad but not endless: every entry here is a word that a Russian
    /// speaker routinely says in Latin script mid-sentence.
    /// </summary>
    internal static readonly IReadOnlyList<string> Terms = BuiltInVocabulary.Terms
        .Select(term => term.Written)
        .Where(term => term.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
        .Concat(
        [
            "API", "Arc", "Bash", "branch", "cache", "CHANGELOG", "Chrome", "CI", "CLI", "commit",
            "CUDA", "deploy", "dependency injection", "desktop", "Discord", "Egoist Shield", "Egoist Voice",
            "Excel", "exception", "framework",
            "Git", "GitHub Copilot", "Google Chrome", "health check", "issue", "LICENSE", "logs",
            "main", "Neon", "overlay", "package", "Paper", "pipeline", "plugin", "pod", "Radeon",
            "README", "Redis", "REST", "rollback", "serverless", "signature", "Steam", "Swift",
            "Telegram", "texture", "Vite", "webhook", "Windows 11"
        ])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(term => term.Length)
        .ToArray();

    /// <summary>
    /// Seeds Whisper's decoder with the domain instead of the language.
    /// </summary>
    /// <remarks>
    /// The prompt matters more now than it used to: the fallback no longer forces Russian, so this
    /// is what keeps language detection from drifting to English on a Russian sentence that happens
    /// to contain "GitHub". It reads as ordinary bilingual prose on purpose — whisper conditions on
    /// it as text, so a bare comma-separated list would bias the output toward producing lists.
    /// </remarks>
    internal static string WhisperPrompt =>
        "Точная русская речь с правильной пунктуацией, в которой встречаются английские " +
        "технические названия в латинице. Например: открой Discord и Egoist Shield, проверь API, " +
        "попроси Claude Code проверить проект, открой GitHub и создай pull request, " +
        "запусти Docker Compose и Kubernetes, напиши скрипт на Python и TypeScript, " +
        "обнови JSON-конфиг, открой Visual Studio Code, отправь сообщение в Telegram, " +
        "сравни ChatGPT и Gemini, разверни backend на AWS, посмотри Grafana; в играх сохраняй " +
        "Steam, Unreal Engine, Minecraft и Counter-Strike.";
}
