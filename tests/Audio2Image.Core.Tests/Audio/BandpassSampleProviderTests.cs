using Audio2Image.Core.Audio;
using NAudio.Wave;

namespace Audio2Image.Core.Tests.Audio;

public class BandpassSampleProviderTests
{
    /// <summary>
    /// Helper: create a sample provider from a float array.
    /// </summary>
    private static ISampleProvider CreateSineProvider(float frequency, int sampleRate = 44100, float durationSeconds = 0.5f)
    {
        int sampleCount = (int)(sampleRate * durationSeconds);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            samples[i] = MathF.Sin(2 * MathF.PI * frequency * i / sampleRate);

        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        return new RawSourceWaveStream(
            new MemoryStream(GetBytes(samples)),
            format
        ).ToSampleProvider();
    }

    private static byte[] GetBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    [Fact]
    public void WaveFormat_MatchesSource()
    {
        var source = CreateSineProvider(440);
        var bandpass = new BandpassSampleProvider(source, 100, 1000);
        Assert.Equal(source.WaveFormat.SampleRate, bandpass.WaveFormat.SampleRate);
        Assert.Equal(source.WaveFormat.Channels, bandpass.WaveFormat.Channels);
    }

    [Fact]
    public void PassesFrequencyInBand()
    {
        // 440 Hz sine with bandpass 200-800 Hz should pass through
        var source = CreateSineProvider(440, durationSeconds: 0.2f);
        var bandpass = new BandpassSampleProvider(source, 200, 800);

        float[] buffer = new float[4096];
        int read = bandpass.Read(buffer, 0, buffer.Length);

        Assert.True(read > 0);

        // Check that signal has significant energy (not all zeros)
        float rms = 0;
        for (int i = 0; i < read; i++)
            rms += buffer[i] * buffer[i];
        rms = MathF.Sqrt(rms / read);

        Assert.True(rms > 0.1f, $"Expected significant signal energy, got RMS={rms}");
    }

    [Fact]
    public void AttenuatesFrequencyOutOfBand()
    {
        // 440 Hz sine with bandpass 1000-2000 Hz should be attenuated
        var source = CreateSineProvider(440, durationSeconds: 0.2f);
        var bandpass = new BandpassSampleProvider(source, 1000, 2000);

        float[] buffer = new float[4096];
        int read = bandpass.Read(buffer, 0, buffer.Length);

        Assert.True(read > 0);

        // Check that signal has very low energy
        float rms = 0;
        for (int i = 0; i < read; i++)
            rms += buffer[i] * buffer[i];
        rms = MathF.Sqrt(rms / read);

        Assert.True(rms < 0.1f, $"Expected attenuated signal, got RMS={rms}");
    }

    [Fact]
    public void ReadsCorrectSampleCount()
    {
        var source = CreateSineProvider(440, durationSeconds: 0.1f);
        var bandpass = new BandpassSampleProvider(source, 100, 1000);

        float[] buffer = new float[8192];
        int totalRead = 0;
        int read;
        do
        {
            read = bandpass.Read(buffer, totalRead, buffer.Length - totalRead);
            totalRead += read;
        } while (read > 0 && totalRead < buffer.Length);

        // 44100 * 0.1 = 4410 samples
        Assert.True(totalRead >= 4000 && totalRead <= 5000,
            $"Expected ~4410 samples, got {totalRead}");
    }
}
