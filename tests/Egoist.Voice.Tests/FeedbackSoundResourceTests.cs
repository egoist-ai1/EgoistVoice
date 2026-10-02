using System.Collections;
using System.Reflection;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class FeedbackSoundResourceTests
{
    [Fact]
    public void Disabled_feedback_does_not_preload_players_on_startup_or_settings_refresh()
    {
        using var sounds = new FeedbackSoundService();
        var cues = (IDictionary)typeof(FeedbackSoundService)
            .GetField("_cues", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sounds)!;
        Assert.False(sounds.Enabled);
        Assert.Empty(cues);
        for (var index = 0; index < 8; index++)
        {
            sounds.Volume = index / 10d;
            sounds.Invalidate();
            Assert.Empty(cues);
        }
    }
}
