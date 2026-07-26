namespace Egoist.Voice.Core;

internal static class TechnicalTermCatalog
{
    /// <summary>
    /// Terms the candidate selector treats as evidence that the fallback engine preserved English
    /// the primary one lost. Kept broad but not endless: every entry here is a word that a Russian
    /// speaker routinely says in Latin script mid-sentence.
    /// </summary>
    internal static readonly IReadOnlyList<string> Terms =
    [
        "Adobe", "AMD", "API", "API endpoint", "Apple", "AWS", "Azure", "Bash", "C#", "ChatGPT",
        "Chrome", "Claude", "CI", "CLI", "Codex", "Copilot", "CSS", "Cursor", "Discord", "Docker",
        "Docker Compose", "Dota 2", "Epic Games", "Excel", "Figma", "Gemini", "Git", "GitHub",
        "GitLab", "Go", "Google", "Google Chrome", "Grafana", "HTML", "HTTP", "Intel", "iOS",
        "JavaScript", "Jira", "JSON", "Kotlin", "Kubernetes", "Linux", "macOS", "Microsoft",
        "Node.js", "Notion", "npm", "NVIDIA", "OpenAI", "PostgreSQL", "PowerShell", "Prometheus",
        "Python", "React", "Redis", "Reddit", "REST", "Rust", "Slack", "Spotify", "SQL", "Steam",
        "Swift", "Telegram", "Terraform", "TypeScript", "Ubuntu", "Unity", "Unreal Engine",
        "Visual Studio Code", "VS Code", "Windows", "YouTube", "Zoom",
        "pull request", "merge request", "code review", "backend", "frontend", "deploy",
        "commit", "branch", "rollback", "endpoint", "framework", "pipeline"
    ];

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
        "технические термины. Например: открой GitHub и создай pull request, запусти Docker " +
        "Compose, проверь логи в Kubernetes, напиши скрипт на Python, обнови JSON-конфиг, " +
        "открой Visual Studio Code, спроси у ChatGPT, разверни backend на AWS, посмотри метрики " +
        "в Grafana, задеплой ветку через CI.";
}
