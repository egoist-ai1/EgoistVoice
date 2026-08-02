using System.IO;
using Egoist.Voice.Core;

namespace Egoist.Voice.Services;

/// <summary>
/// Selects only the extra entity domains justified by the local target or the current utterance.
/// No window title or transcript leaves the process, and neither is logged.
/// </summary>
internal static class EntityProfilePolicy
{
    private static readonly HashSet<string> TechnologyProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "codex", "cursor", "windsurf", "devenv", "rider64", "idea64",
        "pycharm64", "webstorm64", "clion64", "datagrip64", "androidstudio64",
        "windowsterminal", "openconsole", "conhost", "cmd", "powershell", "pwsh",
        "bash", "wsl", "wt", "claude"
    };

    private static readonly string[] TechnologyAnchors =
    [
        "антропик", "клод", "chatgpt", "чат джипити", "openai", "опенай", "github",
        "гитхаб", "gitlab", "гитлаб", "docker", "докер", "kubernetes", "кубернетес",
        "python", "пайтон", "javascript", "джаваскрипт", "visual studio", "vs code",
        "api", "эй пи ай", "pull request", "пул реквест", "бэкенд", "backend",
        "кодекс", "codex", "клауд", "cloud code", "курсор", "cursor", "мета"
    ];

    private static readonly string[] GamingAnchors =
    [
        "игра", "игровой", "матч", "геймплей", "playstation", "плейстейшен", "xbox",
        "иксбокс", "minecraft", "майнкрафт", "fortnite", "фортнайт", "counter strike",
        "контр страйк", "dota", "дота", "epic games", "эпик геймс", "unreal engine",
        "анриал энджин", "unity", "юнити"
    ];

    internal static EntityProfile ResolveForWindow(
        nint window,
        string transcript,
        bool technologyRequested)
    {
        var application = ForegroundApplication.Unknown;
        if (window != 0 &&
            GameForegroundPolicy.GetWindowThreadProcessId(window, out var processId) != 0 &&
            processId != 0)
        {
            application = GameForegroundPolicy.Describe(processId);
        }

        return Resolve(
            transcript,
            application.ProcessName,
            application.IsGame,
            technologyRequested);
    }

    internal static EntityProfile Resolve(
        string transcript,
        string? processName,
        bool isGame,
        bool technologyRequested)
    {
        var profile = EntityProfile.General;
        var normalizedProcess = Path.GetFileNameWithoutExtension(processName ?? string.Empty);

        if (technologyRequested ||
            TechnologyProcesses.Contains(normalizedProcess) ||
            ContainsAnyCompletePhrase(transcript, TechnologyAnchors))
        {
            profile |= EntityProfile.Technology;
        }

        if (isGame || ContainsAnyCompletePhrase(transcript, GamingAnchors))
        {
            profile |= EntityProfile.Gaming;
        }

        return profile;
    }

    private static bool ContainsAnyCompletePhrase(string text, IEnumerable<string> phrases) =>
        phrases.Any(phrase => ContainsCompletePhrase(text, phrase));

    private static bool ContainsCompletePhrase(string text, string phrase)
    {
        var start = 0;
        while ((start = text.IndexOf(phrase, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = start == 0 || !char.IsLetterOrDigit(text[start - 1]);
            var end = start + phrase.Length;
            var after = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after)
            {
                return true;
            }
            start++;
        }

        return false;
    }
}
