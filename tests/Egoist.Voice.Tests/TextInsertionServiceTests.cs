using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class TextInsertionServiceTests
{
    [Fact]
    public void NativeInputStructureHasWindowsX64Size()
    {
        Assert.True(Environment.Is64BitProcess);
        Assert.Equal(40, TextInsertionService.NativeInputSize);
    }
}
