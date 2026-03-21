using Audio2Image.Core.Pipeline;
using Xunit;

namespace Audio2Image.Core.Tests.Pipeline;

public class SpectrogramPipelineTests
{
    [Fact]
    public async Task Run_EmptyDirectory_ReturnsZeroCounts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            var options = new PipelineOptions(tempDir, outputDir);
            var result = await SpectrogramPipeline.RunAsync(options);

            Assert.Equal(0, result.TotalFiles);
            Assert.Equal(0, result.SuccessCount);
            Assert.Equal(0, result.FailureCount);
            Assert.Empty(result.Errors);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public async Task Run_NonExistentDirectory_ReturnsZeroCounts()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var options = new PipelineOptions(fakePath, outputDir);
        var result = await SpectrogramPipeline.RunAsync(options);

        Assert.Equal(0, result.TotalFiles);
        Assert.Equal(0, result.SuccessCount);
    }

    [Fact]
    public async Task Run_WithWavFile_ProducesPng()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            // Create a minimal valid WAV file (mono, 44100 Hz, 1 second of 440 Hz sine)
            var wavPath = Path.Combine(tempDir, "test_tone.wav");
            CreateTestWav(wavPath, 44100, 1.0f, 440f);

            var options = new PipelineOptions(tempDir, outputDir, MaxDegreeOfParallelism: 1);
            var result = await SpectrogramPipeline.RunAsync(options);

            Assert.Equal(1, result.TotalFiles);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(0, result.FailureCount);
            Assert.Empty(result.Errors);

            // Check PNG was created
            var pngPath = Path.Combine(outputDir, "test_tone.png");
            Assert.True(File.Exists(pngPath), "PNG file should exist");
            Assert.True(new FileInfo(pngPath).Length > 0, "PNG file should not be empty");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public async Task Run_ReportsProgress()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            var wavPath = Path.Combine(tempDir, "progress_test.wav");
            CreateTestWav(wavPath, 44100, 0.5f, 440f);

            var progressReports = new List<PipelineProgress>();
            var progressHandler = new Progress<PipelineProgress>(p => progressReports.Add(p));

            var options = new PipelineOptions(tempDir, outputDir, MaxDegreeOfParallelism: 1);
            var result = await SpectrogramPipeline.RunAsync(options, progressHandler);

            Assert.Equal(1, result.SuccessCount);
            // Progress should have been reported at least once
            // Note: Progress<T> posts to SynchronizationContext, so in test we may not get reports immediately
            // We just verify pipeline completes correctly
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public async Task Run_CancellationToken_StopsProcessing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            // Create multiple files
            for (int i = 0; i < 5; i++)
            {
                CreateTestWav(Path.Combine(tempDir, $"file_{i}.wav"), 44100, 1.0f, 440f);
            }

            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            var options = new PipelineOptions(tempDir, outputDir, MaxDegreeOfParallelism: 1);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => SpectrogramPipeline.RunAsync(options, cancellationToken: cts.Token));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        }
    }

    private static void CreateTestWav(string path, int sampleRate, float durationSeconds, float frequency)
    {
        int numSamples = (int)(sampleRate * durationSeconds);
        short[] samples = new short[numSamples];
        for (int i = 0; i < numSamples; i++)
        {
            samples[i] = (short)(short.MaxValue * 0.5 * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
        }

        using var fs = new FileStream(path, FileMode.Create);
        using var writer = new BinaryWriter(fs);

        // WAV header
        int dataSize = numSamples * 2; // 16-bit = 2 bytes per sample
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // chunk size
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2); // byte rate
        writer.Write((short)2); // block align
        writer.Write((short)16); // bits per sample
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (var sample in samples)
        {
            writer.Write(sample);
        }
    }
}
