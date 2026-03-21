using Audio2Image.Core.Models;

namespace Audio2Image.Core.Abstractions;

/// <summary>
/// Abstraction for rendering spectrogram data to image files.
/// </summary>
public interface ISpectrogramRenderer
{
    byte[]? Render(SpectrogramData data, string outputPath,
        int height = 0, bool useLogFrequency = true, float dynamicRangeDb = 90f,
        string colormap = "Hot");
}
