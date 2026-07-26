using System.IO;
using System.Text.Json;

namespace Egoist.Voice.Services;

/// <summary>
/// A saved capsule position. <see cref="WindowWidth"/> was added when the window stopped resizing
/// itself and grew to hold the widest state plus a shadow gutter; a file written before that has
/// zero here, which is what lets the old coordinate be re-centred instead of shifting the capsule
/// sideways on first launch after an update.
/// </summary>
internal sealed record CapsulePosition(double Left, double Top, double WindowWidth = 0);

internal sealed class CapsulePositionService
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EgoistVoice",
        "settings.json");

    internal CapsulePosition? Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CapsulePosition>(File.ReadAllText(_settingsPath));
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not load capsule position", exception);
            return null;
        }
    }

    internal void Save(double left, double top, double windowWidth)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(new CapsulePosition(left, top, windowWidth)));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not save capsule position", exception);
        }
    }

    /// <summary>
    /// Keeps the visual centre of the capsule where the user put it when the window size changes
    /// between versions. Without this the capsule silently jumps by half the size difference.
    /// </summary>
    internal static CapsulePosition Recentre(CapsulePosition saved, double currentWindowWidth, double legacyWindowWidth)
    {
        var previousWidth = saved.WindowWidth > 0 ? saved.WindowWidth : legacyWindowWidth;
        if (Math.Abs(previousWidth - currentWindowWidth) < 0.5)
        {
            return saved with { WindowWidth = currentWindowWidth };
        }

        return new CapsulePosition(
            saved.Left - ((currentWindowWidth - previousWidth) / 2),
            saved.Top,
            currentWindowWidth);
    }

    internal static CapsulePosition Clamp(
        CapsulePosition position,
        double width,
        double height,
        double boundsLeft,
        double boundsTop,
        double boundsWidth,
        double boundsHeight)
    {
        var maxLeft = Math.Max(boundsLeft, boundsLeft + boundsWidth - width);
        var maxTop = Math.Max(boundsTop, boundsTop + boundsHeight - height);
        return new CapsulePosition(
            Math.Clamp(position.Left, boundsLeft, maxLeft),
            Math.Clamp(position.Top, boundsTop, maxTop));
    }
}
