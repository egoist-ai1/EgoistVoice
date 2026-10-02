using System.Windows;
using System.Windows.Media;
using Egoist.Voice.Services;
using Brush = System.Windows.Media.Brush;
using Size = System.Windows.Size;

namespace Egoist.Voice.Controls;

/// <summary>A flat speech meter with a fast attack and a softer release on each bar.</summary>
public sealed class CapsuleWaveform : FrameworkElement
{
    private readonly double[] _levels = new double[CapsuleWaveformProfile.BarCount];
    private readonly double[] _drawnLevels = new double[CapsuleWaveformProfile.BarCount];
    private bool _highContrast;
    internal int RedrawRequestCount { get; private set; }
    private static readonly Brush ScarletBrush = CapsuleWaveformProfile.CreateBarBrush(0, 1);

    public CapsuleWaveform()
    {
        Array.Fill(_levels, CapsuleWaveformProfile.MinimumScale);
        Array.Fill(_drawnLevels, CapsuleWaveformProfile.MinimumScale);
        Height = CapsuleWaveformProfile.BarHeight;
        IsHitTestVisible = false;
    }

    public bool HighContrast
    {
        get => _highContrast;
        set
        {
            if (_highContrast == value) return;
            _highContrast = value;
            InvalidateVisual();
        }
    }

    public void SetUniformScale(double scale)
    {
        Array.Fill(_levels, Math.Clamp(scale, CapsuleWaveformProfile.MinimumScale, 1));
        RequestRedrawIfChanged();
    }

    public void Advance(double level, double phase, double deltaSeconds, bool reducedMotion,
        double bass = 0, double mid = 0, double treble = 0, VoiceSpectrum spectrum = default)
    {
        for (var index = 0; index < _levels.Length; index++)
        {
            var target = CapsuleWaveformProfile.TargetScale(
                index, _levels.Length, level, phase, reducedMotion, bass, mid, treble, spectrum);
            var frameTime = reducedMotion ? deltaSeconds * 0.35 : deltaSeconds;
            _levels[index] = CapsuleWaveformProfile.SmoothLevel(_levels[index], target, frameTime);
        }
        RequestRedrawIfChanged();
    }

    private void RequestRedrawIfChanged()
    {
        // Sub-pixel settling and silence do not need a new retained drawing on every tick.
        var height = ActualHeight > 0 ? ActualHeight : CapsuleWaveformProfile.BarHeight;
        for (var index = 0; index < _levels.Length; index++)
        {
            if (Math.Abs(_levels[index] - _drawnLevels[index]) * height < 0.15) continue;
            Array.Copy(_levels, _drawnLevels, _levels.Length);
            RedrawRequestCount++;
            InvalidateVisual();
            return;
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? CapsuleWaveformProfile.PreferredWidth : availableSize.Width,
        CapsuleWaveformProfile.BarHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var cell = ActualWidth / _levels.Length;
        var brush = _highContrast ? System.Windows.SystemColors.WindowTextBrush : ScarletBrush;
        for (var index = 0; index < _levels.Length; index++)
        {
            var barWidth = Math.Min(5, cell * 0.58) * (0.4 + 0.6 * CapsuleWaveformProfile.EdgeEnvelope(index, _levels.Length));
            var height = Math.Clamp(_levels[index] * ActualHeight, barWidth, ActualHeight);
            drawingContext.DrawRoundedRectangle(brush, null,
                new Rect(cell * (index + 0.5) - barWidth / 2, (ActualHeight - height) / 2, barWidth, height),
                barWidth / 2, barWidth / 2);
        }
    }
}
