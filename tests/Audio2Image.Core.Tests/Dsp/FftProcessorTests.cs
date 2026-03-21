using Audio2Image.Core.Dsp;

namespace Audio2Image.Core.Tests.Dsp;

public class FftProcessorTests
{
    private static float[] GenerateSine(float frequency, int sampleRate, float durationSec)
    {
        var numSamples = (int)(sampleRate * durationSec);
        var samples = new float[numSamples];
        for (int i = 0; i < numSamples; i++)
            samples[i] = MathF.Sin(2f * MathF.PI * frequency * i / sampleRate);
        return samples;
    }

    [Fact]
    public void Process_SineWave_PeakAtCorrectFrequency()
    {
        var sampleRate = 44100;
        var frequency = 440f;
        var samples = GenerateSine(frequency, sampleRate, 1.0f);

        var result = FftProcessor.Process(samples, sampleRate, fftSize: 4096, hopSize: 2048);

        // Check the first frame: peak should be near 440 Hz bin
        var frame = result.Magnitudes[0];
        var peakBin = Array.IndexOf(frame, frame.Max());
        var peakFreq = (float)peakBin * sampleRate / 4096;

        Assert.InRange(peakFreq, 420f, 460f); // within ~20 Hz tolerance
    }

    [Fact]
    public void Process_ReturnsCorrectDimensions()
    {
        var samples = GenerateSine(440f, 44100, 1.0f);
        var result = FftProcessor.Process(samples, 44100, fftSize: 4096, hopSize: 1024);

        Assert.Equal(4096 / 2 + 1, result.FrequencyBins);
        Assert.True(result.TimeFrames > 0);
        Assert.Equal(44100, result.SampleRate);
        Assert.Equal(4096, result.FftSize);
    }

    [Fact]
    public void Process_ShortSignal_AtLeastOneFrame()
    {
        var samples = new float[1000]; // shorter than fftSize
        var result = FftProcessor.Process(samples, 44100, fftSize: 4096);

        Assert.True(result.TimeFrames >= 1);
    }

    [Fact]
    public void Process_EmptyArray_ReturnsAtLeastOneFrame()
    {
        var samples = new float[0];
        var result = FftProcessor.Process(samples, 44100, fftSize: 4096);
        // Math.Max(1, ...) ensures at least 1 frame
        Assert.True(result.TimeFrames >= 1);
    }
}
