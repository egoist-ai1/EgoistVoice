using System.Windows.Media;

namespace Egoist.Voice.Controls;

internal static class CapsuleWaveformProfile
{
    internal const int BarCount = 24;
    internal const double BarWidth = 2;
    internal const double BarMargin = 1.5;
    internal const double BarHeight = 22;
    internal const double MinimumScale = 0.12;

    internal static double TotalWidth => BarCount * (BarWidth + (BarMargin * 2));

    /// <summary>
    /// Width the curve asks for when the parent offers none. The curve stretches to whatever it is
    /// given — unlike the fixed comb of bars it replaced — so this is a starting point, not a limit.
    /// </summary>
    internal static double PreferredWidth => TotalWidth;

    internal static double SmoothLevel(double current, double target, double deltaSeconds)
    {
        var timeConstant = target > current ? 0.045 : 0.105;
        var alpha = 1 - Math.Exp(-Math.Clamp(deltaSeconds, 1d / 240d, 0.05) / timeConstant);
        return current + ((target - current) * alpha);
    }

    /// <summary>
    /// Gamma applied to the level before it drives bar height. A linear mapping looks lifeless on
    /// quiet speech — most of the signal sits in the bottom third of the range and the bars barely
    /// move. An exponent below one lifts that region without touching the top.
    /// </summary>
    internal const double AmplitudeGamma = 0.7;

    /// <summary>
    /// Shape of the curve at one sample.
    /// </summary>
    /// <remarks>
    /// Three travelling waves rather than two, at deliberately incommensurable rates: two components
    /// visibly repeat, and a meter that repeats stops looking like it is responding to anything. The
    /// third is fast and narrow, which is what produces the small irregular crests that read as
    /// individual syllables.
    /// </remarks>
    internal static double TargetScale(int index, int count, double level, double phase, bool reducedMotion)
    {
        var normalizedIndex = count <= 1
            ? 0
            : (index - ((count - 1) / 2d)) / (count / 2d);
        var distance = Math.Abs(normalizedIndex);

        // Flatter through the middle and falling off sharply only near the ends: the previous
        // envelope tapered the whole way out, which made everything but the centre look damped.
        var centerEnvelope = 0.62 + (0.38 * Math.Pow(1 - distance, 0.65));

        // Low floors on purpose. With the previous ones the combined motion could never fall below
        // about half of full amplitude, so the curve had crests but no troughs and read as a solid
        // slab. Real speech has gaps; the meter has to be able to collapse.
        var primary = 0.10 + (0.90 * Math.Abs(Math.Sin(phase + (index * 0.55))));
        var secondary = 0.25 + (0.75 * Math.Abs(Math.Sin((phase * 0.61) - (index * 0.31))));
        var detail = 0.45 + (0.55 * Math.Abs(Math.Sin((phase * 1.73) + (index * 1.21))));

        var activity = Math.Pow(Math.Clamp(level, 0, 1), AmplitudeGamma);
        var idle = reducedMotion ? MinimumScale : 0.115 + (0.022 * Math.Sin((phase * 0.24) + (index * 0.42)));
        var motion = (primary * 0.54) + (secondary * 0.28) + (detail * 0.18);

        return Math.Clamp(idle + (activity * centerEnvelope * motion), MinimumScale, 1);
    }

    internal static double OpacityForLevel(double level) =>
        0.62 + (Math.Min(1, Math.Pow(Math.Clamp(level, 0, 1), AmplitudeGamma) + 0.08) * 0.38);

    /// <summary>
    /// Centre bars are brighter than the edges. The previous spread was 224→255 in red, which is
    /// invisible; widening it to a real luminance ramp is what makes the meter read as a shape
    /// rather than a flat comb.
    /// </summary>
    internal static SolidColorBrush CreateBarBrush(int index, int count)
    {
        var normalizedIndex = count <= 1
            ? 0
            : Math.Abs((index - ((count - 1) / 2d)) / (count / 2d));
        var centerWeight = 1 - Math.Clamp(normalizedIndex, 0, 1);
        var luminance = 0.55 + (0.45 * centerWeight);
        var color = System.Windows.Media.Color.FromRgb(
            (byte)Math.Round(255 * luminance),
            (byte)Math.Round((38 * luminance) + (9 * centerWeight)),
            (byte)Math.Round((52 * luminance) + (8 * centerWeight)));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
