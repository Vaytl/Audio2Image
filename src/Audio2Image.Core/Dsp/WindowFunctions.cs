using System.Collections.Concurrent;

namespace Audio2Image.Core.Dsp;

public static class WindowFunctions
{
    // Cache Hann windows by size — typical sizes are 2048, 4096, 8192
    private static readonly ConcurrentDictionary<int, float[]> _hannCache = new();

    public static float[] Hann(int size)
    {
        if (size <= 0) return [];
        if (size == 1) return [1f]; // Single sample = max energy

        return _hannCache.GetOrAdd(size, static s =>
        {
            var window = new float[s];
            for (int i = 0; i < s; i++)
                window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (s - 1)));
            return window;
        });
    }
}
