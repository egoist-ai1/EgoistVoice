using System.Windows;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using DrawingContext = System.Windows.Media.DrawingContext;
using GradientStop = System.Windows.Media.GradientStop;
using GradientStopCollection = System.Windows.Media.GradientStopCollection;
using LinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using Pen = System.Windows.Media.Pen;
using PenLineCap = System.Windows.Media.PenLineCap;
using PenLineJoin = System.Windows.Media.PenLineJoin;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using StreamGeometry = System.Windows.Media.StreamGeometry;

namespace Egoist.Voice.Controls;

/// <summary>
/// The recording level meter, drawn as a single mirrored curve.
/// </summary>
/// <remarks>
/// <para>
/// This replaced twenty-four separate rectangles. The comb of bars was the most dated thing on the
/// capsule: it read as a 2010s equaliser and, being flat, sat oddly next to a rounded outline. A
/// mirrored curve with a filled body reads as one object with the capsule instead of decoration
/// placed inside it.
/// </para>
/// <para>
/// Depth comes from stacking rather than from an <c>Effect</c>: a wide translucent halo underneath,
/// the gradient body, then a bright hairline along the top edge. A real blur effect would be
/// re-evaluated on every frame of a 144 Hz animation, which is the one thing this control cannot
/// afford; three passes of geometry cost a fraction of that and give the same impression.
/// </para>
/// </remarks>
public sealed class CapsuleWaveform : FrameworkElement
{
    /// <summary>
    /// Sample count for the curve. Far more than the twenty-four bars it replaces — the whole point
    /// is that no individual segment is distinguishable.
    /// </summary>
    private const int SampleCount = 72;

    private const double MaximumAmplitude = 11.5;

    private readonly double[] _levels = new double[SampleCount];
    private double _opacityFactor = 1;
    private bool _highContrast;

    private static readonly Brush BodyBrush = CreateBodyBrush();
    private static readonly Brush HaloBrush = CreateHaloBrush();
    private static readonly Pen CrestPen = CreateCrestPen();
    private static readonly Pen HaloPen = CreateHaloPen();

    public CapsuleWaveform()
    {
        for (var index = 0; index < _levels.Length; index++)
        {
            _levels[index] = CapsuleWaveformProfile.MinimumScale;
        }

        Height = CapsuleWaveformProfile.BarHeight;
        IsHitTestVisible = false;

        // Both ends dissolve instead of stopping at a hard edge. This is what makes the curve feel
        // part of the capsule rather than an object sitting on top of it.
        OpacityMask = CreateEdgeFade();
    }

    /// <summary>
    /// In a high-contrast theme the brand gradient is replaced by the system highlight colour. The
    /// point of that theme is that the user chose their own palette, not that ours is prettier.
    /// </summary>
    public bool HighContrast
    {
        get => _highContrast;
        set
        {
            if (_highContrast == value)
            {
                return;
            }
            _highContrast = value;
            InvalidateVisual();
        }
    }

    /// <summary>Flattens the curve, used when leaving the listening state.</summary>
    public void SetUniformScale(double scale)
    {
        var clamped = Math.Clamp(scale, CapsuleWaveformProfile.MinimumScale, 1);
        for (var index = 0; index < _levels.Length; index++)
        {
            _levels[index] = clamped;
        }
        _opacityFactor = 1;
        InvalidateVisual();
    }

    /// <summary>Advances the animation by one frame and repaints.</summary>
    public void Advance(double level, double phase, double deltaSeconds, bool reducedMotion)
    {
        // Computed once per frame rather than once per sample: it depends only on elapsed time.
        var alpha = 1 - Math.Exp(-Math.Clamp(deltaSeconds, 1d / 240d, 0.05) / 0.035);
        for (var index = 0; index < _levels.Length; index++)
        {
            var target = CapsuleWaveformProfile.TargetScale(index, _levels.Length, level, phase, reducedMotion);
            _levels[index] += (target - _levels[index]) * alpha;
        }

        _opacityFactor = CapsuleWaveformProfile.OpacityForLevel(level);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? CapsuleWaveformProfile.PreferredWidth
            : availableSize.Width;
        return new Size(width, CapsuleWaveformProfile.BarHeight);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth > 0 ? ActualWidth : CapsuleWaveformProfile.PreferredWidth;
        if (width <= 0)
        {
            return;
        }

        var centre = CapsuleWaveformProfile.BarHeight / 2;
        var crest = BuildCrest(width, centre);
        var body = BuildBody(width, centre);

        drawingContext.PushOpacity(_highContrast ? 1 : _opacityFactor);

        if (_highContrast)
        {
            drawingContext.DrawGeometry(System.Windows.SystemColors.HighlightBrush, null, body);
        }
        else
        {
            // Halo, body, crest — back to front.
            drawingContext.DrawGeometry(HaloBrush, HaloPen, body);
            drawingContext.DrawGeometry(BodyBrush, null, body);
            drawingContext.DrawGeometry(null, CrestPen, crest);
        }

        drawingContext.Pop();
    }

    /// <summary>
    /// The upper half of the curve. Midpoint smoothing rather than a spline through every sample:
    /// it cannot overshoot, so a loud syllable never pushes the curve outside the capsule.
    /// </summary>
    private StreamGeometry BuildCrest(double width, double centre)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var first = Sample(0, width, centre, above: true);
            context.BeginFigure(first, isFilled: false, isClosed: false);
            for (var index = 1; index < _levels.Length; index++)
            {
                var previous = Sample(index - 1, width, centre, above: true);
                var current = Sample(index, width, centre, above: true);
                context.QuadraticBezierTo(
                    previous,
                    new Point((previous.X + current.X) / 2, (previous.Y + current.Y) / 2),
                    isStroked: true,
                    isSmoothJoin: true);
            }
            context.LineTo(Sample(_levels.Length - 1, width, centre, above: true), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private StreamGeometry BuildBody(double width, double centre)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Sample(0, width, centre, above: true), isFilled: true, isClosed: true);
            for (var index = 1; index < _levels.Length; index++)
            {
                var previous = Sample(index - 1, width, centre, above: true);
                var current = Sample(index, width, centre, above: true);
                context.QuadraticBezierTo(
                    previous,
                    new Point((previous.X + current.X) / 2, (previous.Y + current.Y) / 2),
                    true,
                    true);
            }

            for (var index = _levels.Length - 1; index >= 0; index--)
            {
                var current = Sample(index, width, centre, above: false);
                if (index == _levels.Length - 1)
                {
                    context.LineTo(current, true, true);
                    continue;
                }

                var previous = Sample(index + 1, width, centre, above: false);
                context.QuadraticBezierTo(
                    previous,
                    new Point((previous.X + current.X) / 2, (previous.Y + current.Y) / 2),
                    true,
                    true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private Point Sample(int index, double width, double centre, bool above)
    {
        var x = _levels.Length == 1 ? width / 2 : index * width / (_levels.Length - 1);
        var amplitude = _levels[index] * MaximumAmplitude;
        return new Point(x, above ? centre - amplitude : centre + amplitude);
    }

    /// <summary>
    /// Bright at the crests, nearly clear through the middle. A uniformly opaque fill turned the
    /// curve into a solid lozenge — the transparent waist is what lets the shape read as a wave
    /// rather than a bar, and lets the capsule surface show through it.
    /// </summary>
    private static Brush CreateBodyBrush()
    {
        var brush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(0x96, 0xFF, 0x5A, 0x68), 0),
                new(Color.FromArgb(0x3C, 0xFF, 0x30, 0x3E), 0.28),
                new(Color.FromArgb(0x18, 0xFF, 0x26, 0x34), 0.5),
                new(Color.FromArgb(0x3C, 0xFF, 0x30, 0x3E), 0.72),
                new(Color.FromArgb(0x96, 0xFF, 0x5A, 0x68), 1)
            },
            new Point(0, 0),
            new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateHaloBrush()
    {
        var brush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(0x1C, 0xFF, 0x26, 0x34), 0),
                new(Color.FromArgb(0x08, 0xFF, 0x26, 0x34), 0.5),
                new(Color.FromArgb(0x1C, 0xFF, 0x26, 0x34), 1)
            },
            new Point(0, 0),
            new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>A wide, faint stroke around the body. Cheap stand-in for a blur.</summary>
    private static Pen CreateHaloPen()
    {
        var pen = new Pen(new System.Windows.Media.SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0x3A, 0x48)), 2.4)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }

    /// <summary>The bright hairline along the top. This is what makes the curve read as lit.</summary>
    private static Pen CreateCrestPen()
    {
        var pen = new Pen(new System.Windows.Media.SolidColorBrush(Color.FromArgb(0xDE, 0xFF, 0xDC, 0xE0)), 1.15)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }

    private static Brush CreateEdgeFade()
    {
        var brush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Colors.Transparent, 0),
                new(Colors.Black, 0.10),
                new(Colors.Black, 0.90),
                new(Colors.Transparent, 1)
            },
            new Point(0, 0.5),
            new Point(1, 0.5));
        brush.Freeze();
        return brush;
    }

    private static class Colors
    {
        internal static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);
        internal static readonly Color Black = Color.FromArgb(0xFF, 0, 0, 0);
    }
}
