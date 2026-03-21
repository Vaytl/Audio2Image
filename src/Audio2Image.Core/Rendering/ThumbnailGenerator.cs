using SkiaSharp;

namespace Audio2Image.Core.Rendering;

/// <summary>
/// Generates small JPEG thumbnail bytes from a spectrogram PNG or SKBitmap.
/// </summary>
public static class ThumbnailGenerator
{
    private const int DefaultHeight = 80;
    private const int JpegQuality = 75;

    /// <summary>Generate thumbnail JPEG bytes from an existing PNG file on disk.</summary>
    public static byte[]? FromFile(string pngPath, int height = DefaultHeight)
    {
        if (!File.Exists(pngPath)) return null;

        using var stream = File.OpenRead(pngPath);
        using var original = SKBitmap.Decode(stream);
        if (original == null) return null;

        return FromBitmap(original, height);
    }

    /// <summary>Generate thumbnail JPEG bytes from an in-memory SKBitmap.</summary>
    public static byte[] FromBitmap(SKBitmap bitmap, int height = DefaultHeight)
    {
        float scale = (float)height / bitmap.Height;
        int width = (int)(bitmap.Width * scale);
        if (width < 1) width = 1;

        using var resized = bitmap.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
        using var image = SKImage.FromBitmap(resized ?? bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return data.ToArray();
    }
}
