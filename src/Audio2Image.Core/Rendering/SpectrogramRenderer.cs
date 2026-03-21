using SkiaSharp;
using Audio2Image.Core.Models;
using Audio2Image.Core.Dsp;

namespace Audio2Image.Core.Rendering;

public static class SpectrogramRenderer
{

    /// <summary>
    /// Render spectrogram to a clean PNG file (no axes, no margins).
    /// Axes are drawn programmatically in the UI viewer.
    /// height: if 0, uses Min(FrequencyBins, 512).
    /// useLogFrequency: true = logarithmic frequency axis (more low-freq detail).
    /// dynamicRangeDb: dB range from peak to silence floor.
    /// </summary>
    /// <summary>
    /// Render spectrogram to PNG and return JPEG thumbnail bytes.
    /// </summary>
    public static byte[]? Render(SpectrogramData data, string outputPath,
        int height = 0, bool useLogFrequency = true, float dynamicRangeDb = 90f,
        string colormap = "Hot")
    {
        int width = data.TimeFrames;
        if (width == 0 || data.FrequencyBins == 0) return null;

        int imgHeight = height <= 0 ? Math.Min(data.FrequencyBins, 512) : height;

        using var bitmap = new SKBitmap(width, imgHeight, SKColorType.Rgba8888, SKAlphaType.Premul);

        // --- Find peak value for normalization ---
        float maxDb = float.MinValue;
        for (int t = 0; t < data.TimeFrames; t++)
        {
            var frame = data.Magnitudes[t];
            for (int f = 0; f < data.FrequencyBins; f++)
            {
                if (frame[f] > maxDb) maxDb = frame[f];
            }
        }

        float minDb = maxDb - dynamicRangeDb;
        float invRange = 1f / dynamicRangeDb;

        // --- Precompute frequency bin mapping for each y-row ---
        float nyquist = data.SampleRate / 2f;
        float maxFreq = MathF.Min(nyquist, MelScale.MaxFreqHz); // cap at 20kHz
        // Mel-scale frequency mapping (matches iZotope RX)
        float melMin = MelScale.HzToMel(MelScale.MinFreqHz);
        float melMax = MelScale.HzToMel(maxFreq);
        float melRange = melMax - melMin;

        int[] freqBinMap = new int[imgHeight];
        for (int y = 0; y < imgHeight; y++)
        {
            float normalizedY = 1.0f - (float)y / (imgHeight - 1); // 0=bottom(low) to 1=top(high)

            float freq;
            if (useLogFrequency)
            {
                float mel = melMin + normalizedY * melRange;
                freq = MelScale.MelToHz(mel);
            }
            else
            {
                freq = normalizedY * maxFreq;
            }

            int bin = (int)(freq / nyquist * (data.FrequencyBins - 1));
            freqBinMap[y] = Math.Clamp(bin, 0, data.FrequencyBins - 1);
        }

        // --- Render spectrogram pixels ---
        var pixels = bitmap.GetPixels();
        unsafe
        {
            byte* ptr = (byte*)pixels.ToPointer();
            int stride = width * 4;

            for (int y = 0; y < imgHeight; y++)
            {
                int freqBin = freqBinMap[y];
                int rowOffset = y * stride;

                for (int x = 0; x < width; x++)
                {
                    float db = data.Magnitudes[x][freqBin];

                    float normalized = (db - minDb) * invRange;
                    normalized = Math.Clamp(normalized, 0f, 1f);
                    normalized = MathF.Sqrt(normalized); // gamma 0.5

                    var color = SpectrogramColorMap.GetColor(normalized, colormap);

                    int offset = rowOffset + x * 4;
                    ptr[offset + 0] = color.Red;
                    ptr[offset + 1] = color.Green;
                    ptr[offset + 2] = color.Blue;
                    ptr[offset + 3] = 255;
                }
            }
        }

        // Ensure output directory exists
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Generate thumbnail from bitmap before saving (avoids re-reading file)
        var thumbnailBytes = ThumbnailGenerator.FromBitmap(bitmap);

        using var image = SKImage.FromBitmap(bitmap);
        using var encodedData = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        encodedData.SaveTo(stream);

        return thumbnailBytes;
    }
}
