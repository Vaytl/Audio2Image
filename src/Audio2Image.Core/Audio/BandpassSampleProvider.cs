using NAudio.Wave;

namespace Audio2Image.Core.Audio;

/// <summary>
/// An ISampleProvider wrapper that applies a bandpass filter using
/// cascaded second-order Butterworth IIR sections (biquads).
/// Only frequencies between LowFrequency and HighFrequency pass through.
/// This is a real-time streaming filter — no FFT, no block artifacts.
/// </summary>
public class BandpassSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    // Cascaded biquad sections for steep rolloff (4th order = 24 dB/octave)
    private readonly BiquadFilter _highPass1;
    private readonly BiquadFilter _highPass2;
    private readonly BiquadFilter _lowPass1;
    private readonly BiquadFilter _lowPass2;

    /// <summary>Low cutoff frequency in Hz.</summary>
    public float LowFrequency { get; }

    /// <summary>High cutoff frequency in Hz.</summary>
    public float HighFrequency { get; }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public BandpassSampleProvider(ISampleProvider source, float lowFrequency, float highFrequency)
    {
        _source = source;
        LowFrequency = lowFrequency;
        HighFrequency = highFrequency;

        float sampleRate = source.WaveFormat.SampleRate;

        // Two cascaded biquads per cutoff = 4th order Butterworth (24 dB/oct rolloff)
        _highPass1 = BiquadFilter.HighPass(sampleRate, lowFrequency, 0.707f);
        _highPass2 = BiquadFilter.HighPass(sampleRate, lowFrequency, 0.707f);
        _lowPass1 = BiquadFilter.LowPass(sampleRate, highFrequency, 0.707f);
        _lowPass2 = BiquadFilter.LowPass(sampleRate, highFrequency, 0.707f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);

        for (int i = 0; i < read; i++)
        {
            float sample = buffer[offset + i];
            // Cascaded high-pass then cascaded low-pass = steep bandpass
            sample = _highPass1.Process(sample);
            sample = _highPass2.Process(sample);
            sample = _lowPass1.Process(sample);
            sample = _lowPass2.Process(sample);
            buffer[offset + i] = sample;
        }

        return read;
    }
}

/// <summary>
/// A second-order IIR (biquad) filter.
/// Implements the standard Direct Form I difference equation:
///   y[n] = (b0/a0)*x[n] + (b1/a0)*x[n-1] + (b2/a0)*x[n-2]
///                        - (a1/a0)*y[n-1] - (a2/a0)*y[n-2]
/// </summary>
internal class BiquadFilter
{
    private readonly float _b0, _b1, _b2;
    private readonly float _a1, _a2;

    // State variables
    private float _x1, _x2; // previous input samples
    private float _y1, _y2; // previous output samples

    private BiquadFilter(float b0, float b1, float b2, float a0, float a1, float a2)
    {
        // Normalize by a0
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public float Process(float input)
    {
        float output = _b0 * input + _b1 * _x1 + _b2 * _x2
                                    - _a1 * _y1 - _a2 * _y2;

        _x2 = _x1;
        _x1 = input;
        _y2 = _y1;
        _y1 = output;

        return output;
    }

    /// <summary>
    /// Create a second-order Butterworth low-pass filter.
    /// </summary>
    public static BiquadFilter LowPass(float sampleRate, float cutoffHz, float q)
    {
        float w0 = 2f * MathF.PI * cutoffHz / sampleRate;
        float cosW0 = MathF.Cos(w0);
        float sinW0 = MathF.Sin(w0);
        float alpha = sinW0 / (2f * q);

        float b0 = (1f - cosW0) / 2f;
        float b1 = 1f - cosW0;
        float b2 = (1f - cosW0) / 2f;
        float a0 = 1f + alpha;
        float a1 = -2f * cosW0;
        float a2 = 1f - alpha;

        return new BiquadFilter(b0, b1, b2, a0, a1, a2);
    }

    /// <summary>
    /// Create a second-order Butterworth high-pass filter.
    /// </summary>
    public static BiquadFilter HighPass(float sampleRate, float cutoffHz, float q)
    {
        float w0 = 2f * MathF.PI * cutoffHz / sampleRate;
        float cosW0 = MathF.Cos(w0);
        float sinW0 = MathF.Sin(w0);
        float alpha = sinW0 / (2f * q);

        float b0 = (1f + cosW0) / 2f;
        float b1 = -(1f + cosW0);
        float b2 = (1f + cosW0) / 2f;
        float a0 = 1f + alpha;
        float a1 = -2f * cosW0;
        float a2 = 1f - alpha;

        return new BiquadFilter(b0, b1, b2, a0, a1, a2);
    }
}
