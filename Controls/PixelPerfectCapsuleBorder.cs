using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Egoist.Voice.Controls;

public sealed class PixelPerfectCapsuleBorder : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(System.Windows.Media.Brush), typeof(PixelPerfectCapsuleBorder),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(System.Windows.Media.Brush), typeof(PixelPerfectCapsuleBorder),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(PixelPerfectCapsuleBorder),
        new FrameworkPropertyMetadata(26d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PhysicalStrokeProperty = DependencyProperty.Register(
        nameof(PhysicalStroke), typeof(double), typeof(PixelPerfectCapsuleBorder),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RasterScaleProperty = DependencyProperty.Register(
        nameof(RasterScale), typeof(int), typeof(PixelPerfectCapsuleBorder),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender));

    public System.Windows.Media.Brush Background { get => (System.Windows.Media.Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public System.Windows.Media.Brush BorderBrush { get => (System.Windows.Media.Brush)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public double PhysicalStroke { get => (double)GetValue(PhysicalStrokeProperty); set => SetValue(PhysicalStrokeProperty, value); }
    public int RasterScale { get => (int)GetValue(RasterScaleProperty); set => SetValue(RasterScaleProperty, value); }

    private CacheKey _cacheKey;
    private BitmapSource? _cachedChrome;

    public PixelPerfectCapsuleBorder()
    {
        // Set once, in the constructor. Assigning a dependency property from inside OnRender is
        // both wasteful and a good way to invalidate the very render that is in progress.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    /// <summary>Diagnostics for the render tests: how many times the chrome was rasterized.</summary>
    internal int RasterizationCount { get; private set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);

        // Rasterizing on every OnRender was the single most expensive thing the capsule did. At
        // 150 % DPI and RasterScale 4 the chrome bitmap alone is about 1.5 MB, and OnRender fires
        // for every frame of the width animation — tens of megabytes per state change, part of it
        // straight into the large object heap. The geometry only actually changes when the size,
        // the DPI or one of the brushes does.
        var key = new CacheKey(
            ActualWidth,
            ActualHeight,
            dpi.DpiScaleX,
            dpi.DpiScaleY,
            CornerRadius,
            PhysicalStroke,
            RasterScale,
            Background,
            BorderBrush);

        if (_cachedChrome is null || !_cacheKey.Equals(key))
        {
            _cachedChrome = RasterizeChrome(dpi);
            _cacheKey = key;
            RasterizationCount++;
        }

        drawingContext.DrawImage(_cachedChrome, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    private BitmapSource RasterizeChrome(DpiScale dpi)
    {
        var profile = CalculateRasterProfile(
            ActualWidth,
            ActualHeight,
            dpi.DpiScaleX,
            dpi.DpiScaleY,
            PhysicalStroke,
            RasterScale);
        var scaleX = profile.DpiScaleX;
        var scaleY = profile.DpiScaleY;
        var stroke = profile.StrokeDip;
        var rasterScale = profile.RasterScale;
        var inset = stroke / 2;
        var rect = new Rect(inset, inset, Math.Max(0, ActualWidth - stroke), Math.Max(0, ActualHeight - stroke));
        var radius = Math.Max(0, CornerRadius - inset);
        var pen = new System.Windows.Media.Pen(BorderBrush, stroke);
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        // A one-device-pixel WPF arc is rasterized from only one sample per output
        // pixel. On a 52 px capsule that makes neighbouring curve pixels alternate
        // between bright and almost transparent even though the vector is correct.
        // Render the small chrome layer at 4x device resolution and downsample it;
        // child content remains native WPF and therefore stays sharp.
        var pixelWidth = profile.PixelWidth;
        var pixelHeight = profile.PixelHeight;
        var chrome = new DrawingVisual();
        using (var chromeContext = chrome.RenderOpen())
        {
            chromeContext.PushTransform(new ScaleTransform(
                scaleX * rasterScale,
                scaleY * rasterScale));
            chromeContext.DrawRoundedRectangle(Background, pen, rect, radius, radius);
            chromeContext.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(chrome);
        if (bitmap.CanFreeze)
        {
            bitmap.Freeze();
        }

        return bitmap;
    }

    /// <summary>
    /// Everything the rasterized chrome depends on. Brushes are compared by reference, which is
    /// exactly right here: the capsule swaps between a handful of frozen static brushes, so a
    /// reference change is a real change and equality never gives a false cache hit.
    /// </summary>
    private readonly record struct CacheKey(
        double Width,
        double Height,
        double DpiScaleX,
        double DpiScaleY,
        double CornerRadius,
        double PhysicalStroke,
        int RasterScale,
        System.Windows.Media.Brush? Background,
        System.Windows.Media.Brush? BorderBrush);

    internal static CapsuleRasterProfile CalculateRasterProfile(
        double width,
        double height,
        double dpiScaleX,
        double dpiScaleY,
        double physicalStroke,
        int rasterScale)
    {
        var scaleX = Math.Max(1, dpiScaleX);
        var scaleY = Math.Max(1, dpiScaleY);
        var normalizedStroke = Math.Clamp(physicalStroke, 0.5d, 3d);
        var normalizedRasterScale = Math.Clamp(rasterScale, 1, 8);
        return new CapsuleRasterProfile(
            scaleX,
            scaleY,
            normalizedStroke / scaleX,
            normalizedRasterScale,
            Math.Max(1, (int)Math.Ceiling(width * scaleX * normalizedRasterScale)),
            Math.Max(1, (int)Math.Ceiling(height * scaleY * normalizedRasterScale)));
    }
}

internal readonly record struct CapsuleRasterProfile(
    double DpiScaleX,
    double DpiScaleY,
    double StrokeDip,
    int RasterScale,
    int PixelWidth,
    int PixelHeight);
