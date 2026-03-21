namespace Audio2Image.Core.Dsp;

using Audio2Image.Core.Models;

public static class MelScale
{
    /// <summary>Standard audible frequency limits used across the project.</summary>
    public const float MinFreqHz = 20f;
    public const float MaxFreqHz = 20000f;

    public static float HzToMel(float hz) => 2595f * MathF.Log10(1f + hz / 700f);
    public static float MelToHz(float mel) => 700f * (MathF.Pow(10f, mel / 2595f) - 1f);

    // Double overloads for UI code (Avalonia uses double coordinates)
    public static double HzToMel(double hz) => 2595.0 * Math.Log10(1.0 + hz / 700.0);
    public static double MelToHz(double mel) => 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);

    /// <summary>
    /// Map a frequency in Hz to a normalized Y position (0=bottom/low, 1=top/high)
    /// on the mel-scale spectrogram, given the sample rate.
    /// </summary>
    public static double FreqToNormalizedY(double freqHz, double sampleRate)
    {
        double maxFreq = Math.Min(sampleRate / 2.0, MaxFreqHz);
        double melMin = HzToMel((double)MinFreqHz);
        double melMax = HzToMel(maxFreq);
        double melRange = melMax - melMin;
        if (melRange <= 0) return 0;
        double mel = HzToMel(freqHz);
        return Math.Clamp((mel - melMin) / melRange, 0, 1);
    }

    /// <summary>
    /// Map a normalized Y position (0=bottom/low, 1=top/high) to frequency in Hz.
    /// </summary>
    public static double NormalizedYToFreq(double normalizedY, double sampleRate)
    {
        double maxFreq = Math.Min(sampleRate / 2.0, MaxFreqHz);
        double melMin = HzToMel((double)MinFreqHz);
        double melMax = HzToMel(maxFreq);
        double melRange = melMax - melMin;
        double mel = melMin + normalizedY * melRange;
        return MelToHz(mel);
    }

    public static float[][] CreateFilterBank(int fftSize, int sampleRate,
        int numMelBins = 128, float fMin = 0f, float fMax = 0f)
    {
        if (fMax <= 0f) fMax = sampleRate / 2f;

        var melMin = HzToMel(fMin);
        var melMax = HzToMel(fMax);
        var freqBins = fftSize / 2 + 1;

        // Create equally spaced mel points
        var melPoints = new float[numMelBins + 2];
        for (int i = 0; i < melPoints.Length; i++)
            melPoints[i] = melMin + (melMax - melMin) * i / (numMelBins + 1);

        // Convert back to Hz and then to FFT bin indices
        var binIndices = new float[melPoints.Length];
        for (int i = 0; i < melPoints.Length; i++)
        {
            var hz = MelToHz(melPoints[i]);
            binIndices[i] = hz * fftSize / sampleRate;
        }

        // Create triangular filters
        var filterBank = new float[numMelBins][];
        for (int m = 0; m < numMelBins; m++)
        {
            filterBank[m] = new float[freqBins];
            var left = binIndices[m];
            var center = binIndices[m + 1];
            var right = binIndices[m + 2];

            for (int k = 0; k < freqBins; k++)
            {
                if (k >= left && k <= center && center > left)
                    filterBank[m][k] = (k - left) / (center - left);
                else if (k > center && k <= right && right > center)
                    filterBank[m][k] = (right - k) / (right - center);
            }
        }

        return filterBank;
    }

    public static SpectrogramData Apply(SpectrogramData data, int numMelBins = 128)
    {
        var filterBank = CreateFilterBank(data.FftSize, data.SampleRate, numMelBins);
        var melMagnitudes = new float[data.TimeFrames][];

        // Reuse linear buffer across frames to avoid per-frame allocation
        var linear = new float[data.FrequencyBins];

        for (int t = 0; t < data.TimeFrames; t++)
        {
            melMagnitudes[t] = new float[numMelBins];

            // Convert from dB back to linear for filtering
            for (int f = 0; f < data.FrequencyBins; f++)
                linear[f] = MathF.Pow(10f, data.Magnitudes[t][f] / 20f);

            for (int m = 0; m < numMelBins; m++)
            {
                float sum = 0f;
                for (int f = 0; f < data.FrequencyBins; f++)
                    sum += linear[f] * filterBank[m][f];

                // Back to dB
                melMagnitudes[t][m] = sum > 1e-10f ? 20f * MathF.Log10(sum) : -80f;
                melMagnitudes[t][m] = Math.Max(melMagnitudes[t][m], -80f);
            }
        }

        return new SpectrogramData(melMagnitudes, numMelBins, data.TimeFrames, data.SampleRate, data.FftSize);
    }
}
