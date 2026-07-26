using Egoist.Voice.Controls;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class VisualBehaviorTests
{
    [Fact]
    public void Amplitude_gamma_lifts_quiet_levels_more_than_loud_ones()
    {
        // The point of the gamma curve: a linear mapping leaves quiet speech in the bottom of the
        // range where the bars barely move, which reads as an unresponsive meter.
        var quietGain = CapsuleWaveformProfile.OpacityForLevel(0.1) - CapsuleWaveformProfile.OpacityForLevel(0);
        var loudGain = CapsuleWaveformProfile.OpacityForLevel(1.0) - CapsuleWaveformProfile.OpacityForLevel(0.9);

        Assert.True(quietGain > loudGain, "Тихий диапазон должен растягиваться сильнее громкого.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.35)]
    [InlineData(1)]
    public void Bar_scales_stay_inside_their_bounds(double level)
    {
        for (var index = 0; index < CapsuleWaveformProfile.BarCount; index++)
        {
            var scale = CapsuleWaveformProfile.TargetScale(
                index, CapsuleWaveformProfile.BarCount, level, phase: 1.3, reducedMotion: false);

            Assert.InRange(scale, CapsuleWaveformProfile.MinimumScale, 1);
        }
    }

    [Fact]
    public void Centre_bars_are_visibly_brighter_than_the_edges()
    {
        // The previous ramp went 224→255 in red only, a difference nobody can see. The meter has
        // to read as a shape, not a flat comb.
        var centre = CapsuleWaveformProfile.CreateBarBrush(
            CapsuleWaveformProfile.BarCount / 2, CapsuleWaveformProfile.BarCount).Color;
        var edge = CapsuleWaveformProfile.CreateBarBrush(0, CapsuleWaveformProfile.BarCount).Color;

        Assert.True(centre.R - edge.R > 60, $"Разброс яркости слишком мал: {edge.R} → {centre.R}.");
    }

    [Fact]
    public void Reduced_motion_flattens_the_idle_shape()
    {
        var animated = CapsuleWaveformProfile.TargetScale(3, 24, level: 0, phase: 2.1, reducedMotion: false);
        var still = CapsuleWaveformProfile.TargetScale(3, 24, level: 0, phase: 2.1, reducedMotion: true);

        Assert.Equal(CapsuleWaveformProfile.MinimumScale, still, precision: 6);
        Assert.True(animated > still);
    }

    [Fact]
    public void Level_smoothing_rises_faster_than_it_falls()
    {
        // Speech onsets must be immediate; decays must not flicker.
        var rise = CapsuleWaveformProfile.SmoothLevel(0.2, 0.8, 1d / 60d) - 0.2;
        var fall = 0.8 - CapsuleWaveformProfile.SmoothLevel(0.8, 0.2, 1d / 60d);

        Assert.True(rise > fall, $"Атака {rise:0.###} должна быть быстрее спада {fall:0.###}.");
    }

    [Fact]
    public void Waveform_fills_the_space_the_capsule_gives_it()
    {
        const double availableWidth = 218 - 70 - 18;

        Assert.InRange(CapsuleWaveformProfile.TotalWidth / availableWidth, 0.9, 1.0);
    }

    [Theory]
    [InlineData(1.0, 4)]
    [InlineData(1.5, 4)]
    [InlineData(2.0, 4)]
    public void Raster_profile_keeps_the_stroke_one_physical_pixel(double dpiScale, int rasterScale)
    {
        var profile = PixelPerfectCapsuleBorder.CalculateRasterProfile(
            218, 48, dpiScale, dpiScale, physicalStroke: 1.6, rasterScale);

        Assert.Equal(1.6, profile.StrokeDip * dpiScale, precision: 6);
        Assert.True(profile.PixelWidth >= 218);
    }

    [Theory]
    [InlineData(FeedbackSound.RecordingStarted)]
    [InlineData(FeedbackSound.RecordingStopped)]
    [InlineData(FeedbackSound.TextInserted)]
    [InlineData(FeedbackSound.Error)]
    public void Every_cue_is_a_valid_wave_file(FeedbackSound sound)
    {
        var payload = FeedbackSoundService.Synthesize(sound, 0.4);

        Assert.True(payload.Length > 44, "Файл должен содержать данные, а не только заголовок.");
        Assert.Equal("RIFF"u8.ToArray(), payload[..4]);
        Assert.Equal("WAVE"u8.ToArray(), payload[8..12]);
        Assert.Equal("data"u8.ToArray(), payload[36..40]);
    }

    [Fact]
    public void Cues_start_and_end_at_silence()
    {
        // Without the fade envelope, a tone that begins at full amplitude clicks — which is exactly
        // the kind of cheapness the sound is meant to avoid.
        var payload = FeedbackSoundService.Synthesize(FeedbackSound.RecordingStarted, 1.0);

        Assert.Equal(0, BitConverter.ToInt16(payload, 44));
        Assert.InRange(Math.Abs(BitConverter.ToInt16(payload, payload.Length - 2)), 0, 400);
    }

    [Fact]
    public void Volume_scales_the_generated_amplitude()
    {
        var quiet = Peak(FeedbackSoundService.Synthesize(FeedbackSound.Error, 0.2));
        var loud = Peak(FeedbackSoundService.Synthesize(FeedbackSound.Error, 1.0));

        Assert.True(loud > quiet * 3, $"Громкость не масштабируется: {quiet} → {loud}.");
    }

    [Fact]
    public void A_silent_cue_produces_no_signal() =>
        Assert.Equal(0, Peak(FeedbackSoundService.Synthesize(FeedbackSound.Error, 0)));

    private static int Peak(byte[] wave)
    {
        var peak = 0;
        for (var offset = 44; offset + 1 < wave.Length; offset += 2)
        {
            peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(wave, offset)));
        }
        return peak;
    }
}
