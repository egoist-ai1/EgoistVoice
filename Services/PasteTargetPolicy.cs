using System.IO;

namespace Egoist.Voice.Services;

public enum PasteMethod
{
    /// <summary>Ctrl+V — correct almost everywhere.</summary>
    ControlV,

    /// <summary>Ctrl+Shift+V — what console hosts actually listen for.</summary>
    ControlShiftV,

    /// <summary>Shift+Insert — the escape hatch for anything that intercepts both of the above.</summary>
    ShiftInsert,

    /// <summary>Clipboard only: the transcript is copied and the user pastes it.</summary>
    ClipboardOnly
}

public sealed record PasteTargetDecision(
    PasteMethod Method,
    bool IsSensitive,
    string ProcessName)
{
    public static PasteTargetDecision Default { get; } = new(PasteMethod.ControlV, false, string.Empty);
}

/// <summary>
/// Chooses how to paste into whatever currently owns the foreground, and refuses to paste at all
/// into a password manager.
/// </summary>
/// <remarks>
/// Ctrl+V is not universal: console hosts have used Ctrl+Shift+V for years, and sending Ctrl+V
/// there does nothing at best. Auto-detecting the target is the approach OpenWhispr settled on and
/// it is the best solution visible on the market — every other product asks the user to configure
/// it by hand, which means it stays broken for everyone who never finds the setting.
/// </remarks>
public static class PasteTargetPolicy
{
    private static readonly HashSet<string> ConsoleHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "windowsterminal", "openconsole", "conhost", "cmd", "powershell", "pwsh",
        "mintty", "putty", "alacritty", "wezterm-gui", "wezterm", "kitty",
        "hyper", "mobaxterm", "conemu", "conemu64", "cmder", "tabby", "warp"
    };

    /// <summary>
    /// Never inject into a password manager. The transcript is not worth the risk of typing into a
    /// vault, and unlike everything else in this file it is not a formatting question.
    /// </summary>
    private static readonly HashSet<string> SensitiveApplications = new(StringComparer.OrdinalIgnoreCase)
    {
        "1password", "1password7", "1password8", "agilebits",
        "bitwarden", "keepass", "keepass2", "keepassxc", "keeper",
        "dashlane", "lastpass", "nordpass", "roboform", "enpass",
        "credentialuibroker", "logonui", "consent"
    };

    public static PasteTargetDecision Resolve(string? processName)
    {
        var normalized = Path.GetFileNameWithoutExtension(processName ?? string.Empty);
        if (normalized.Length == 0)
        {
            return PasteTargetDecision.Default;
        }

        if (SensitiveApplications.Contains(normalized))
        {
            return new PasteTargetDecision(PasteMethod.ClipboardOnly, true, normalized);
        }

        return ConsoleHosts.Contains(normalized)
            ? new PasteTargetDecision(PasteMethod.ControlShiftV, false, normalized)
            : new PasteTargetDecision(PasteMethod.ControlV, false, normalized);
    }

    public static PasteTargetDecision ResolveForWindow(nint window)
    {
        if (window == 0 ||
            GameForegroundPolicy.GetWindowThreadProcessId(window, out var processId) == 0 ||
            processId == 0)
        {
            return PasteTargetDecision.Default;
        }

        return Resolve(GameForegroundPolicy.Describe(processId).ProcessName);
    }
}
