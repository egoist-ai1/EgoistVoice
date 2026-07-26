using Egoist.Voice.Controls;

namespace Egoist.Voice.Tests;

public sealed class CapsuleWaveformProfileTests
{
    [Fact]
    public void WaveformOccupiesMostOfListeningContentSlotWithoutClipping()
    {
        const double listeningBodyWidth = 218;
        const double fixedColumns = 36 + 1 + 1 + 32;
        const double contentMargins = 18;
        var availableWidth = listeningBodyWidth - fixedColumns - contentMargins;

        Assert.InRange(CapsuleWaveformProfile.TotalWidth, availableWidth * 0.90, availableWidth);
    }

    [Fact]
    public void AudioEnvelopeHasFastAttackAndSofterRelease()
    {
        var attack = CapsuleWaveformProfile.SmoothLevel(0.1, 0.9, 1d / 60d);
        var release = CapsuleWaveformProfile.SmoothLevel(0.9, 0.1, 1d / 60d);

        Assert.True(attack - 0.1 > 0.9 - release);
        Assert.InRange(attack, 0.1, 0.9);
        Assert.InRange(release, 0.1, 0.9);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(144)]
    public void WaveformRemainsBoundedAcrossCommonRefreshRates(int refreshRate)
    {
        var level = 0d;
        var phase = 0d;
        for (var frame = 0; frame < refreshRate * 3; frame++)
        {
            var target = frame < refreshRate ? 0.82 : 0.12;
            var delta = 1d / refreshRate;
            level = CapsuleWaveformProfile.SmoothLevel(level, target, delta);
            phase += (0.09 + (level * 0.14)) * delta * 60;
            for (var index = 0; index < CapsuleWaveformProfile.BarCount; index++)
            {
                var scale = CapsuleWaveformProfile.TargetScale(
                    index,
                    CapsuleWaveformProfile.BarCount,
                    level,
                    phase,
                    reducedMotion: false);
                Assert.InRange(scale, CapsuleWaveformProfile.MinimumScale, 1);
                Assert.False(double.IsNaN(scale));
            }
        }
    }
}
