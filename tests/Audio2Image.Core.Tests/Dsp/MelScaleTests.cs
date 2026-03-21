using Audio2Image.Core.Dsp;

namespace Audio2Image.Core.Tests.Dsp;

public class MelScaleTests
{
    [Fact]
    public void HzToMel_KnownValues()
    {
        Assert.Equal(0f, MelScale.HzToMel(0f), 0.01f);
        // 1000 Hz ≈ 1000 Mel (approximately)
        var mel1000 = MelScale.HzToMel(1000f);
        Assert.InRange(mel1000, 999f, 1001f);
    }

    [Fact]
    public void HzToMel_MelToHz_RoundTrip()
    {
        var frequencies = new float[] { 0f, 100f, 440f, 1000f, 4000f, 8000f };
        foreach (var freq in frequencies)
        {
            var mel = MelScale.HzToMel(freq);
            var hz = MelScale.MelToHz(mel);
            Assert.Equal(freq, hz, 0.1f);
        }
    }

    [Fact]
    public void CreateFilterBank_CorrectDimensions()
    {
        var filterBank = MelScale.CreateFilterBank(4096, 44100, numMelBins: 128);
        Assert.Equal(128, filterBank.Length);
        Assert.Equal(4096 / 2 + 1, filterBank[0].Length);
    }

    [Fact]
    public void Apply_ReducesFrequencyBins()
    {
        var samples = new float[44100]; // 1 second of silence
        var fftData = FftProcessor.Process(samples, 44100, 4096, 1024);
        var melData = MelScale.Apply(fftData, numMelBins: 128);

        Assert.Equal(128, melData.FrequencyBins);
        Assert.Equal(fftData.TimeFrames, melData.TimeFrames);
    }
}
