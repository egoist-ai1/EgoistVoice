using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class AudioCaptureServiceTests
{
    [Fact]
    public void PreRollRingRetainsOnlyNewestAlignedFramesAcrossWraparound()
    {
        var ring = new PcmByteRingBuffer(capacity: 8, blockAlign: 2);
        ring.Write(new byte[] { 0, 1, 2, 3, 4, 5 });
        ring.Write(new byte[] { 6, 7, 8, 9, 10, 11 });

        Assert.Equal(new byte[] { 4, 5, 6, 7, 8, 9, 10, 11 }, ring.Snapshot());
        Assert.Equal(8, ring.Count);
    }

    [Fact]
    public void PreRollRingDropsPartialFramesAndClearsSensitiveBytes()
    {
        var ring = new PcmByteRingBuffer(capacity: 8, blockAlign: 2);
        ring.Write(new byte[] { 1, 2, 3 });
        Assert.Equal(new byte[] { 1, 2 }, ring.Snapshot());

        ring.Clear();

        Assert.Empty(ring.Snapshot());
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void SessionBufferIncludesPreRollTailAndNeverLeaksCancelledSessionIntoNextTake()
    {
        var buffer = new CaptureSessionBuffer(preRollCapacity: 4, blockAlign: 1);
        buffer.Append(new byte[] { 1, 2, 3, 4 });
        buffer.Begin(initialCapacity: 8);
        buffer.Append(new byte[] { 5, 6 });
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, buffer.Complete().Bytes);

        buffer.Begin(initialCapacity: 8);
        buffer.Append(new byte[] { 7 });
        buffer.CancelSession();
        buffer.Begin(initialCapacity: 8);
        buffer.Append(new byte[] { 8 });
        var next = buffer.Complete();

        Assert.Equal(new byte[] { 4, 5, 6, 7, 8 }, next.Bytes);
        Assert.Equal(4, next.PreRollBytes);
    }

    [Fact]
    public void ThreeHundredSessionCyclesRemainBoundedAndOrdered()
    {
        var buffer = new CaptureSessionBuffer(preRollCapacity: 8, blockAlign: 1);
        for (var cycle = 0; cycle < 300; cycle++)
        {
            buffer.Begin(initialCapacity: 16);
            buffer.Append(new byte[] { (byte)cycle });
            var completed = buffer.Complete();
            Assert.InRange(completed.Bytes.Length, 1, 9);
            Assert.Equal((byte)cycle, completed.Bytes[^1]);
        }
    }

    [Fact]
    public void DevicePcmIsDownmixedAndResampledExactlyOnce()
    {
        const int sourceRate = 48_000;
        const int frames = sourceRate / 10;
        var raw = new byte[frames * 4];
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = (short)Math.Round(Math.Sin(frame * 2 * Math.PI * 440 / sourceRate) * 8_000);
            raw[frame * 4] = (byte)sample;
            raw[(frame * 4) + 1] = (byte)(sample >> 8);
            raw[(frame * 4) + 2] = (byte)sample;
            raw[(frame * 4) + 3] = (byte)(sample >> 8);
        }

        var samples = AudioCaptureService.ConvertToMono16Khz(raw, new NAudio.Wave.WaveFormat(sourceRate, 16, 2));

        Assert.InRange(samples.Length, 1_560, 1_640);
        Assert.True(samples.Max(Math.Abs) > 0.15f);
    }

    [Fact]
    public void PreRollNoiseFloorLetsSustainedQuietSpeechThrough()
    {
        var detector = new SpeechActivityDetector();
        detector.Reset(noiseFloorDb: -62);
        for (var index = 0; index < 7; index++)
        {
            detector.Process(0.003, 0.014, 20);
        }

        Assert.True(detector.Snapshot().HasSpeech);
    }

    [Fact]
    public void AdaptiveGateDoesNotPromoteStationaryNoiseIntoSpeech()
    {
        var detector = new SpeechActivityDetector();
        detector.Reset(noiseFloorDb: -48);
        for (var index = 0; index < 30; index++)
        {
            detector.Process(0.004, 0.006, 20);
        }

        Assert.False(detector.Snapshot().HasSpeech);
    }

    [Fact]
    public void NoiseFloorUsesQuietPreRollFramesInsteadOfTriggerClick()
    {
        var samples = new float[3_200];
        Array.Fill(samples, 0.001f);
        Array.Fill(samples, 0.2f, 2_880, 320);

        var noise = AudioSignalAnalyzer.EstimateNoiseFloorDb(samples, samples.Length, 16_000);

        Assert.NotNull(noise);
        Assert.InRange(noise.Value, -60.1, -59.9);
    }

    [Fact]
    public void BoundaryWindowsStaySmallAndExplicit()
    {
        Assert.Equal(200, AudioCaptureService.PreRollDuration.TotalMilliseconds);
        Assert.Equal(350, AudioCaptureService.ReleaseTailDuration.TotalMilliseconds);
    }

    [Theory]
    [InlineData(-80, 0)]
    [InlineData(-58, 0)]
    [InlineData(-36, 0.5)]
    [InlineData(-14, 1)]
    [InlineData(-3, 1)]
    public void DbToLevelMapsAndClampsMicrophoneRange(double decibels, float expected)
    {
        var amplitude = Math.Pow(10, decibels / 20);

        var result = AudioCaptureService.DbToLevel(amplitude, -58, -14);

        Assert.Equal(expected, result, precision: 3);
    }

    [Fact]
    public void DbToLevelIsMonotonicAcrossSpeechRange()
    {
        var quiet = AudioCaptureService.DbToLevel(0.004, -58, -14);
        var normal = AudioCaptureService.DbToLevel(0.04, -58, -14);
        var loud = AudioCaptureService.DbToLevel(0.2, -58, -14);

        Assert.True(quiet < normal);
        Assert.True(normal < loud);
    }

    [Fact]
    public void SpeechGateRejectsSilenceAndSingleTransient()
    {
        var detector = new SpeechActivityDetector();
        for (var index = 0; index < 20; index++)
        {
            detector.Process(0.0002, 0.0008, 32);
        }
        detector.Process(0.2, 0.5, 32);

        var result = detector.Snapshot();

        Assert.False(result.HasSpeech);
        Assert.Equal(672, result.Duration.TotalMilliseconds);
    }

    [Fact]
    public void SpeechGateAcceptsSustainedQuietSpeech()
    {
        var detector = new SpeechActivityDetector();
        for (var index = 0; index < 6; index++)
        {
            detector.Process(0.006, 0.018, 32);
        }

        var result = detector.Snapshot();

        Assert.True(result.HasSpeech);
        Assert.Equal(192, result.DetectedSpeech.TotalMilliseconds);
        Assert.True(result.PeakDecibels > -36);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0.1, -20)]
    [InlineData(0.01, -40)]
    public void AmplitudeToDecibelsIsStable(double amplitude, double expected)
    {
        Assert.Equal(expected, SpeechActivityDetector.AmplitudeToDecibels(amplitude), precision: 3);
    }

    [Fact]
    public void A_silent_microphone_is_reported_rather_than_hidden()
    {
        // Before this, a dead microphone and a deliberate pause produced the same outcome: the
        // capsule vanished and the user was left guessing.
        var detector = new SpeechActivityDetector();
        for (var index = 0; index < 10; index++)
        {
            detector.Process(0.000001, 0.000002, 32);
        }

        var result = detector.Snapshot();

        Assert.False(result.HasSpeech);
        Assert.Equal(SpeechRejection.MicrophoneSilent, result.Rejection);
        Assert.Equal("Микрофон молчит", AudioCaptureService.DescribeRejection(result.Rejection));
    }

    [Fact]
    public void Audible_but_too_short_is_distinguished_from_too_quiet()
    {
        var detector = new SpeechActivityDetector();
        detector.Process(0.2, 0.5, 32);

        var result = detector.Snapshot();

        Assert.Equal(SpeechRejection.TooShort, result.Rejection);
    }

    [Fact]
    public void Room_noise_below_the_gate_is_reported_as_too_quiet()
    {
        var detector = new SpeechActivityDetector();
        for (var index = 0; index < 20; index++)
        {
            detector.Process(0.001, 0.004, 32);
        }

        var result = detector.Snapshot();

        Assert.Equal(SpeechRejection.TooQuiet, result.Rejection);
    }

    [Fact]
    public void A_successful_session_carries_no_rejection()
    {
        var detector = new SpeechActivityDetector();
        for (var index = 0; index < 6; index++)
        {
            detector.Process(0.006, 0.018, 32);
        }

        var result = detector.Snapshot();

        Assert.True(result.HasSpeech);
        Assert.Equal(SpeechRejection.None, result.Rejection);
        Assert.Null(AudioCaptureService.DescribeRejection(result.Rejection));
    }
}
