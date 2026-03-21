using SkiaSharp;

namespace Audio2Image.Core.Rendering;

/// <summary>
/// Spectrogram color maps. Default is a "hot" colormap similar to iZotope RX:
/// black → dark red → orange → yellow → white.
/// Also includes Viridis as an alternative.
/// </summary>
public static class SpectrogramColorMap
{
    // Hot/RX-style: 256 entries interpolated through key stops
    // black → dark brown → dark orange → orange → amber → yellow → white
    private static readonly SKColor[] HotTable;
    private static readonly SKColor[] ViridisTable;

    static SpectrogramColorMap()
    {
        HotTable = BuildHotColormap();
        ViridisTable = BuildViridisColormap();
    }

    /// <summary>
    /// RX 11-style "hot" colormap: silence=black, loud=white, through warm oranges.
    /// </summary>
    public static SKColor GetColor(float normalized)
    {
        int idx = Math.Clamp((int)(normalized * 255), 0, 255);
        return HotTable[idx];
    }

    /// <summary>
    /// Viridis colormap (scientific, perceptually uniform).
    /// </summary>
    public static SKColor GetViridisColor(float normalized)
    {
        int idx = Math.Clamp((int)(normalized * 255), 0, 255);
        return ViridisTable[idx];
    }

    /// <summary>
    /// Get color using the specified colormap name ("Hot" or "Viridis").
    /// Falls back to Hot for unknown names.
    /// </summary>
    public static SKColor GetColor(float normalized, string colormap)
    {
        return string.Equals(colormap, "Viridis", StringComparison.OrdinalIgnoreCase)
            ? GetViridisColor(normalized)
            : GetColor(normalized);
    }

    /// <summary>Interpolate 256-entry color table from key stops.</summary>
    private static SKColor[] InterpolateColormap((float pos, byte r, byte g, byte b)[] stops)
    {
        var table = new SKColor[256];
        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f;
            int s = 0;
            for (int j = 1; j < stops.Length; j++)
            {
                if (stops[j].pos >= t) { s = j - 1; break; }
            }

            var a = stops[s];
            var b = stops[Math.Min(s + 1, stops.Length - 1)];
            float range = b.pos - a.pos;
            float local = range > 0 ? (t - a.pos) / range : 0;

            table[i] = new SKColor(
                (byte)Math.Clamp(a.r + (b.r - a.r) * local, 0, 255),
                (byte)Math.Clamp(a.g + (b.g - a.g) * local, 0, 255),
                (byte)Math.Clamp(a.b + (b.b - a.b) * local, 0, 255));
        }
        return table;
    }

    private static SKColor[] BuildHotColormap() => InterpolateColormap(new (float, byte, byte, byte)[]
    {
        (0.00f, 0, 0, 0),           // black — silence
        (0.05f, 15, 4, 0),          // near-black warm
        (0.12f, 45, 12, 0),         // very dark brown-red
        (0.20f, 80, 25, 2),         // dark red-brown
        (0.28f, 120, 40, 3),        // brown-red
        (0.36f, 165, 60, 5),        // dark orange
        (0.44f, 200, 85, 8),        // deep orange
        (0.52f, 225, 110, 12),      // orange
        (0.60f, 240, 140, 20),      // bright orange
        (0.68f, 248, 165, 35),      // orange-amber
        (0.76f, 252, 190, 55),      // warm amber
        (0.84f, 255, 210, 85),      // golden-orange
        (0.92f, 255, 230, 130),     // pale orange
        (1.00f, 255, 245, 190),     // warm cream — loud
    });

    private static SKColor[] BuildViridisColormap() => InterpolateColormap(new (float, byte, byte, byte)[]
    {
        (0.000f, 68, 1, 84),
        (0.125f, 72, 36, 117),
        (0.250f, 64, 67, 135),
        (0.375f, 52, 94, 141),
        (0.500f, 33, 144, 141),
        (0.625f, 53, 183, 121),
        (0.750f, 109, 205, 89),
        (0.875f, 180, 222, 44),
        (1.000f, 253, 231, 37),
    });
}
