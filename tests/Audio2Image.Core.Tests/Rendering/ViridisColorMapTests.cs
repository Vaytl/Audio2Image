using Audio2Image.Core.Rendering;
using SkiaSharp;

namespace Audio2Image.Core.Tests.Rendering;

public class SpectrogramColorMapTests
{
    [Fact]
    public void GetColor_AtZero_IsBlack()
    {
        var color = SpectrogramColorMap.GetColor(0f);
        // Hot colormap starts at black (silence)
        Assert.Equal(0, color.Red);
        Assert.Equal(0, color.Green);
        Assert.Equal(0, color.Blue);
    }

    [Fact]
    public void GetColor_AtOne_IsBrightWarm()
    {
        var color = SpectrogramColorMap.GetColor(1f);
        // Hot colormap ends at near-white warm
        Assert.True(color.Red > 200);
        Assert.True(color.Green > 200);
    }

    [Fact]
    public void GetColor_ClampsBelowZero()
    {
        var color = SpectrogramColorMap.GetColor(-1f);
        Assert.Equal(SpectrogramColorMap.GetColor(0f), color);
    }

    [Fact]
    public void GetColor_ClampsAboveOne()
    {
        var color = SpectrogramColorMap.GetColor(2f);
        Assert.Equal(SpectrogramColorMap.GetColor(1f), color);
    }

    [Fact]
    public void GetColor_MidRange_IsWarmOrange()
    {
        var color = SpectrogramColorMap.GetColor(0.5f);
        // Mid-range should be in the orange area
        Assert.True(color.Red > 150, $"Red={color.Red} should be > 150");
        Assert.True(color.Green < color.Red, $"Green={color.Green} should be < Red={color.Red}");
    }

    [Fact]
    public void GetViridisColor_AtZero_DarkPurple()
    {
        var color = SpectrogramColorMap.GetViridisColor(0f);
        // Viridis starts at dark purple
        Assert.True(color.Red > 50 && color.Red < 90);
        Assert.True(color.Blue > 60);
    }
}
