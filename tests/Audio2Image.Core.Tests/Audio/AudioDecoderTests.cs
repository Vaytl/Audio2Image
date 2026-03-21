using NAudio.Wave;
using Audio2Image.Core.Audio;

namespace Audio2Image.Core.Tests.Audio;

public class AudioDecoderTests : IDisposable
{
    private readonly string _testDir;

    public AudioDecoderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Audio2Image_Decoder_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private string CreateTestWav(int sampleRate = 44100, int durationMs = 500, int channels = 1)
    {
        var path = Path.Combine(_testDir, $"test_{Guid.NewGuid():N}.wav");
        var samples = sampleRate * durationMs / 1000;
        var format = new WaveFormat(sampleRate, 16, channels);

        using var writer = new WaveFileWriter(path, format);
        for (int i = 0; i < samples * channels; i++)
        {
            var sample = (short)(Math.Sin(2.0 * Math.PI * 440.0 * (i / channels) / sampleRate) * short.MaxValue * 0.5);
            writer.WriteSample(sample / (float)short.MaxValue);
        }
        return path;
    }

    [Fact]
    public void Decode_MonoWav_ReturnsCorrectData()
    {
        var path = CreateTestWav(44100, 500, 1);
        var result = AudioDecoder.Decode(path);

        Assert.Equal(44100, result.SampleRate);
        Assert.True(result.Samples.Length > 0);
        Assert.True(result.Duration.TotalMilliseconds >= 400); // allow some tolerance
    }

    [Fact]
    public void Decode_StereoWav_DownmixesToMono()
    {
        var monoPath = CreateTestWav(44100, 500, 1);
        var stereoPath = CreateTestWav(44100, 500, 2);

        var mono = AudioDecoder.Decode(monoPath);
        var stereo = AudioDecoder.Decode(stereoPath);

        // Stereo downmixed should have roughly same sample count as mono
        Assert.InRange(stereo.Samples.Length, mono.Samples.Length - 100, mono.Samples.Length + 100);
    }

    [Fact]
    public void Decode_NonExistentFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            AudioDecoder.Decode(Path.Combine(_testDir, "nonexistent.wav")));
    }
}
