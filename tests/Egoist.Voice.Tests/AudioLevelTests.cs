using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class AudioLevelTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.001, 0)]
    [InlineData(0.01, 0.454545)]
    [InlineData(0.2, 1)]
    public void DbToLevelMapsMicrophoneAmplitudeIntoVisualRange(double amplitude, double expected)
    {
        var actual = AudioCaptureService.DbToLevel(amplitude, -60, -16);
        Assert.InRange(actual, expected - 0.0001, expected + 0.0001);
    }
}
