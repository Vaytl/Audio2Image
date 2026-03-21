using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Models;

namespace Audio2Image.Core.Rendering;

/// <summary>
/// Instance wrapper around the static SpectrogramRenderer for DI.
/// </summary>
public class SpectrogramRendererInstance : ISpectrogramRenderer
{
    public byte[]? Render(SpectrogramData data, string outputPath,
        int height = 0, bool useLogFrequency = true, float dynamicRangeDb = 90f,
        string colormap = "Hot")
        => SpectrogramRenderer.Render(data, outputPath, height, useLogFrequency, dynamicRangeDb, colormap);
}
