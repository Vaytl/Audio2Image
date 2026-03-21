using Audio2Image.Core.Models;
using Audio2Image.Core.Rendering;
using SkiaSharp;

namespace Audio2Image.Core.Tests.Rendering;

public class SpectrogramRendererTests : IDisposable
{
    private readonly string _testDir;

    public SpectrogramRendererTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Audio2Image_Render_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void Render_CreatesValidPng()
    {
        // Create synthetic spectrogram data
        var timeFrames = 100;
        var freqBins = 64;
        var magnitudes = new float[timeFrames][];
        for (int t = 0; t < timeFrames; t++)
        {
            magnitudes[t] = new float[freqBins];
            for (int f = 0; f < freqBins; f++)
                magnitudes[t][f] = -80f + 80f * ((float)f / freqBins); // gradient
        }

        var data = new SpectrogramData(magnitudes, freqBins, timeFrames, 44100, 4096);
        var outputPath = Path.Combine(_testDir, "test.png");

        SpectrogramRenderer.Render(data, outputPath, height: 200);

        Assert.True(File.Exists(outputPath));

        // Verify it's a valid PNG with correct dimensions
        using var bitmap = SKBitmap.Decode(outputPath);
        Assert.NotNull(bitmap);
        Assert.Equal(100, bitmap.Width);   // width = timeFrames (no margins — axes drawn in UI)
        Assert.Equal(200, bitmap.Height); // height as specified
    }

    [Fact]
    public void Render_CreatesOutputDirectory()
    {
        var magnitudes = new float[10][];
        for (int i = 0; i < 10; i++)
            magnitudes[i] = new float[8];

        var data = new SpectrogramData(magnitudes, 8, 10, 44100, 4096);
        var outputPath = Path.Combine(_testDir, "sub", "deep", "test.png");

        SpectrogramRenderer.Render(data, outputPath, height: 50);

        Assert.True(File.Exists(outputPath));
    }
}
