using Audio2Image.Core.Dsp;

namespace Audio2Image.Core.Tests.Dsp;

public class WindowFunctionsTests
{
    [Fact]
    public void Hann_FirstAndLastAreZero()
    {
        var window = WindowFunctions.Hann(1024);
        Assert.Equal(0f, window[0], 1e-6f);
        Assert.Equal(0f, window[1023], 1e-6f);
    }

    [Fact]
    public void Hann_MiddleIsOne()
    {
        var window = WindowFunctions.Hann(1024);
        // For even length, the middle value should be very close to 1.0
        Assert.True(window[512] > 0.99f);
    }

    [Fact]
    public void Hann_IsSymmetric()
    {
        var window = WindowFunctions.Hann(1024);
        for (int i = 0; i < 512; i++)
            Assert.Equal(window[i], window[1023 - i], 1e-6f);
    }

    [Fact]
    public void Hann_CorrectLength()
    {
        Assert.Equal(4096, WindowFunctions.Hann(4096).Length);
        Assert.Equal(2048, WindowFunctions.Hann(2048).Length);
    }

    [Fact]
    public void Hann_SizeZero_ReturnsEmpty()
    {
        var window = WindowFunctions.Hann(0);
        Assert.Empty(window);
    }

    [Fact]
    public void Hann_SizeOne_ReturnsOneWithoutNaN()
    {
        var window = WindowFunctions.Hann(1);
        Assert.Single(window);
        Assert.False(float.IsNaN(window[0]));
        Assert.Equal(1f, window[0]);
    }

    [Fact]
    public void Hann_NegativeSize_ReturnsEmpty()
    {
        var window = WindowFunctions.Hann(-5);
        Assert.Empty(window);
    }
}
