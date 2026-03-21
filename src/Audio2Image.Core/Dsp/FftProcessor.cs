using System.Buffers;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Audio2Image.Core.Models;

namespace Audio2Image.Core.Dsp;

public static class FftProcessor
{
    public static SpectrogramData Process(float[] samples, int sampleRate,
        int fftSize = 4096, int hopSize = 1024)
    {
        var window = WindowFunctions.Hann(fftSize);
        var numFrames = Math.Max(1, (samples.Length - fftSize) / hopSize + 1);
        var freqBins = fftSize / 2 + 1;
        var magnitudes = new float[numFrames][];

        // Rent a buffer from pool to reuse across frames
        var pool = ArrayPool<Complex>.Shared;

        for (int frame = 0; frame < numFrames; frame++)
        {
            var offset = frame * hopSize;
            var complexBuffer = pool.Rent(fftSize);

            for (int i = 0; i < fftSize; i++)
            {
                var sampleIndex = offset + i;
                var sample = sampleIndex < samples.Length ? samples[sampleIndex] : 0f;
                complexBuffer[i] = new Complex(sample * window[i], 0);
            }

            Fourier.Forward(complexBuffer, FourierOptions.NoScaling);

            var frameMag = new float[freqBins];
            for (int i = 0; i < freqBins; i++)
            {
                var magnitude = complexBuffer[i].Magnitude;
                // Convert to dB, with floor at -120 dB (wider range for better detail)
                var db = magnitude > 1e-12 ? 20.0f * MathF.Log10((float)magnitude) : -120f;
                frameMag[i] = Math.Max(db, -120f);
            }
            magnitudes[frame] = frameMag;

            pool.Return(complexBuffer);
        }

        return new SpectrogramData(magnitudes, freqBins, numFrames, sampleRate, fftSize);
    }
}
