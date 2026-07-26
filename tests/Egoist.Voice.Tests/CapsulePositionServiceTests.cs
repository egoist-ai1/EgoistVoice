using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class CapsuleRecentreTests
{
    [Fact]
    public void A_position_saved_before_the_window_grew_keeps_its_visual_centre()
    {
        // The window went from 242 to 344 when it stopped resizing itself. Without this the capsule
        // silently jumps 51 px left on the first launch after an update.
        var saved = new CapsulePosition(1000, 500);

        var moved = CapsulePositionService.Recentre(saved, currentWindowWidth: 344, legacyWindowWidth: 242);

        Assert.Equal(949, moved.Left);
        Assert.Equal(500, moved.Top);
        Assert.Equal(344, moved.WindowWidth);
    }

    [Fact]
    public void A_position_saved_by_the_current_version_is_left_alone()
    {
        var saved = new CapsulePosition(1000, 500, 344);

        var moved = CapsulePositionService.Recentre(saved, currentWindowWidth: 344, legacyWindowWidth: 242);

        Assert.Equal(1000, moved.Left);
        Assert.Equal(344, moved.WindowWidth);
    }

    [Fact]
    public void Recentring_is_idempotent()
    {
        var once = CapsulePositionService.Recentre(new CapsulePosition(1000, 500), 344, 242);
        var twice = CapsulePositionService.Recentre(once, 344, 242);

        Assert.Equal(once, twice);
    }
}

public sealed class CapsulePositionServiceTests
{
    [Fact]
    public void ClampKeepsSavedPositionWhenItIsVisible()
    {
        var result = CapsulePositionService.Clamp(new CapsulePosition(240, 180), 286, 72, 0, 0, 1920, 1080);

        Assert.Equal(new CapsulePosition(240, 180), result);
    }

    [Theory]
    [InlineData(-900, -100, -500, 0)]
    [InlineData(1900, 1200, 1634, 1008)]
    public void ClampMovesOffscreenPositionBackIntoVirtualDesktop(
        double left,
        double top,
        double expectedLeft,
        double expectedTop)
    {
        var result = CapsulePositionService.Clamp(
            new CapsulePosition(left, top),
            286,
            72,
            -500,
            0,
            2420,
            1080);

        Assert.Equal(new CapsulePosition(expectedLeft, expectedTop), result);
    }
}
