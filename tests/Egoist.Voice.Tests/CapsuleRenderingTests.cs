using Egoist.Voice.Controls;

namespace Egoist.Voice.Tests;

public sealed class CapsuleRenderingTests
{
    [Theory]
    [InlineData(1.00)]
    [InlineData(1.25)]
    [InlineData(1.50)]
    [InlineData(2.00)]
    public void SupersampledStrokeKeepsTheSamePhysicalWeightAtEveryDpi(double dpiScale)
    {
        var profile = PixelPerfectCapsuleBorder.CalculateRasterProfile(
            218,
            48,
            dpiScale,
            dpiScale,
            1.6,
            4);

        Assert.Equal(1.6, profile.StrokeDip * dpiScale, precision: 8);
        Assert.Equal((int)Math.Ceiling(218 * dpiScale * 4), profile.PixelWidth);
        Assert.Equal((int)Math.Ceiling(48 * dpiScale * 4), profile.PixelHeight);
        Assert.Equal(4, profile.RasterScale);
    }
}
