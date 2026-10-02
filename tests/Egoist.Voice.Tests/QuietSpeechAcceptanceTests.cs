using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class QuietSpeechAcceptanceTests
{
    [Theory]
    [InlineData(-72, -59, -53)]
    [InlineData(-70, -58, -52)]
    public void Low_gain_speech_with_clear_background_separation_is_accepted(double noise, double rms, double peak)
    {
        var detector = new SpeechActivityDetector();
        detector.Reset(noise);
        for (var frame = 0; frame < 10; frame++)
            detector.Process(Amplitude(rms), Amplitude(peak), 20);
        Assert.True(detector.Snapshot().HasSpeech);
    }

    [Theory]
    [InlineData(-72, -70, -65)]
    [InlineData(-62, -60, -54)]
    public void Stationary_background_is_rejected_at_low_gain(double noise, double rms, double peak)
    {
        var detector = new SpeechActivityDetector();
        detector.Reset(noise);
        for (var frame = 0; frame < 50; frame++)
            detector.Process(Amplitude(rms), Amplitude(peak), 20);
        Assert.False(detector.Snapshot().HasSpeech);
    }

    [Fact]
    public void One_quiet_transient_does_not_become_speech()
    {
        var detector = new SpeechActivityDetector();
        detector.Reset(-72);
        detector.Process(Amplitude(-59), Amplitude(-53), 20);
        for (var frame = 0; frame < 50; frame++) detector.Process(Amplitude(-72), Amplitude(-68), 20);
        Assert.False(detector.Snapshot().HasSpeech);
    }

    private static double Amplitude(double decibels) => Math.Pow(10, decibels / 20);
}