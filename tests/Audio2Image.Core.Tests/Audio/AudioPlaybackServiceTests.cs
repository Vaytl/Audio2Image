using Audio2Image.Core.Audio;
using NAudio.Wave;

namespace Audio2Image.Core.Tests.Audio;

public class AudioPlaybackServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AudioPlaybackService _service;

    public AudioPlaybackServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Audio2ImageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new AudioPlaybackService();
    }

    private string CreateTestWav(float durationSeconds = 1.0f, int sampleRate = 44100)
    {
        string path = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.wav");
        int sampleCount = (int)(sampleRate * durationSeconds);
        float[] samples = new float[sampleCount];

        // Generate 440 Hz sine wave
        for (int i = 0; i < sampleCount; i++)
            samples[i] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / sampleRate);

        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
        // Convert float to 16-bit
        foreach (var sample in samples)
        {
            short s16 = (short)(sample * short.MaxValue);
            writer.WriteSample(sample);
        }
        return path;
    }

    [Fact]
    public void InitialState_IsStopped()
    {
        Assert.Equal(PlaybackState.Stopped, _service.State);
        Assert.Equal(TimeSpan.Zero, _service.Position);
        Assert.Equal(TimeSpan.Zero, _service.Duration);
    }

    [Fact]
    public void Load_SetsCorrectDuration()
    {
        string wav = CreateTestWav(2.0f);
        _service.Load(wav);

        // Duration should be approximately 2 seconds
        Assert.True(_service.Duration.TotalSeconds > 1.5 && _service.Duration.TotalSeconds < 2.5,
            $"Expected ~2s, got {_service.Duration.TotalSeconds}s");
    }

    [Fact]
    public void Seek_ChangesPosition()
    {
        string wav = CreateTestWav(3.0f);
        _service.Load(wav);

        _service.Seek(TimeSpan.FromSeconds(1.0));
        Assert.True(_service.Position.TotalSeconds >= 0.9, $"Position should be ~1s, got {_service.Position.TotalSeconds}");
    }

    [Fact]
    public void Volume_CanBeSet()
    {
        string wav = CreateTestWav();
        _service.Load(wav);

        _service.Volume = 0.5f;
        Assert.Equal(0.5f, _service.Volume, 0.01f);
    }

    [Fact]
    public void Load_NonExistentFile_Throws()
    {
        // On Windows, a non-existent directory throws DirectoryNotFoundException,
        // while a non-existent file in an existing directory throws FileNotFoundException.
        // Both derive from IOException.
        Assert.ThrowsAny<IOException>(() => _service.Load(Path.Combine(_tempDir, "nonexistent", "audio.wav")));
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
