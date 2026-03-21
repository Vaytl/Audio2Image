using Audio2Image.Core.Audio;
using NAudio.Wave;

namespace Audio2Image.Core.Tests.Audio;

public class AudioExporterTests : IDisposable
{
    private readonly string _tempDir;

    public AudioExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Audio2ImageTests_Export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    private string CreateTestWav(float durationSeconds = 3.0f, int sampleRate = 44100)
    {
        string path = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.wav");
        int sampleCount = (int)(sampleRate * durationSeconds);

        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
        for (int i = 0; i < sampleCount; i++)
        {
            float sample = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / sampleRate);
            writer.WriteSample(sample);
        }
        return path;
    }

    [Fact]
    public void ExportRange_CreatesValidWav()
    {
        var source = CreateTestWav(3.0f);
        var output = Path.Combine(_tempDir, "exported.wav");

        AudioExporter.ExportRange(source, output,
            TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2.0));

        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);

        // Verify it's a valid WAV
        using var reader = new AudioFileReader(output);
        Assert.True(reader.TotalTime.TotalSeconds > 1.0);
        Assert.True(reader.TotalTime.TotalSeconds < 2.0);
    }

    [Fact]
    public void ExportRange_CreatesOutputDirectory()
    {
        var source = CreateTestWav(2.0f);
        var output = Path.Combine(_tempDir, "sub", "deep", "out.wav");

        AudioExporter.ExportRange(source, output,
            TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1.0));

        Assert.True(File.Exists(output));
    }

    [Fact]
    public void ExportRange_WithCrossfade_CreatesFile()
    {
        var source = CreateTestWav(5.0f);
        var output = Path.Combine(_tempDir, "crossfade.wav");

        AudioExporter.ExportRange(source, output,
            TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(4.0),
            crossfadeMs: 50);

        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public void ExportRange_EndBeforeStart_Throws()
    {
        var source = CreateTestWav(3.0f);
        var output = Path.Combine(_tempDir, "bad.wav");

        Assert.Throws<ArgumentException>(() =>
            AudioExporter.ExportRange(source, output,
                TimeSpan.FromSeconds(2.0), TimeSpan.FromSeconds(1.0)));
    }

    [Fact]
    public void ExportRange_EqualStartEnd_Throws()
    {
        var source = CreateTestWav(3.0f);
        var output = Path.Combine(_tempDir, "zero.wav");

        Assert.Throws<ArgumentException>(() =>
            AudioExporter.ExportRange(source, output,
                TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.0)));
    }

    [Fact]
    public void SuggestFileName_FormatsCorrectly()
    {
        var name = AudioExporter.SuggestFileName(
            "/music/my track.mp3",
            TimeSpan.FromSeconds(90),  // 1:30
            TimeSpan.FromSeconds(135));  // 2:15

        Assert.Equal("my track_loop_01m30s-02m15s.wav", name);
    }

    [Fact]
    public void SuggestFileName_ZeroTimes()
    {
        var name = AudioExporter.SuggestFileName(
            "test.wav",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5));

        Assert.Equal("test_loop_00m00s-00m05s.wav", name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
