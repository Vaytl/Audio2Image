using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Audio2Image.Core.Audio;
using Audio2Image.Core.Dsp;
using Audio2Image.Core.Models;
using Audio2Image.Core.Rendering;
using Audio2Image.Core.Scanning;

namespace Audio2Image.Core.Pipeline;

public record PipelineOptions(
    string InputDirectory,
    string OutputDirectory,
    int FftSize = 4096,
    int HopSize = 512,          // smaller hop = better time resolution
    int ImageHeight = 512,      // 0 = auto from freq bins
    int MaxDegreeOfParallelism = 0,  // 0 = unlimited (use all available cores)
    float DynamicRangeDb = 90f,
    string Colormap = "Hot"
);

public record PipelineProgress(
    int TotalFiles,
    int ProcessedFiles,
    int FailedFiles,
    string? CurrentFile
);

/// <summary>
/// Metadata about a successfully processed audio file.
/// </summary>
public record ProcessedFileInfo(
    string AudioFilePath,
    string ImagePath,
    double DurationSeconds,
    int SampleRate,
    byte[]? ThumbnailData = null
);

public record PipelineResult(
    int TotalFiles,
    int SuccessCount,
    int FailureCount,
    List<string> Errors,
    TimeSpan Elapsed,
    List<ProcessedFileInfo> ProcessedFiles
);

public static class SpectrogramPipeline
{
    /// <summary>
    /// Process all audio files in a directory (recursive scan).
    /// </summary>
    public static async Task<PipelineResult> RunAsync(
        PipelineOptions options,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = AudioScanner.Scan(options.InputDirectory);
        return await ProcessFilesAsync(files, options, progress, null, cancellationToken);
    }

    /// <summary>
    /// Process all audio files in a directory with direct callback (no IProgress marshaling).
    /// </summary>
    public static async Task<PipelineResult> RunAsync(
        PipelineOptions options,
        Action<PipelineProgress> onProgress,
        CancellationToken cancellationToken = default)
    {
        var files = AudioScanner.Scan(options.InputDirectory);
        return await ProcessFilesAsync(files, options, null, onProgress, cancellationToken);
    }

    /// <summary>
    /// Process specific audio files (selected by user).
    /// </summary>
    public static async Task<PipelineResult> RunFilesAsync(
        IReadOnlyList<AudioFileInfo> files,
        string outputDirectory,
        int fftSize = 4096,
        int hopSize = 512,
        int imageHeight = 512,
        int maxDegreeOfParallelism = 0,
        float dynamicRangeDb = 90f,
        string colormap = "Hot",
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var options = new PipelineOptions("", outputDirectory, fftSize, hopSize, imageHeight, maxDegreeOfParallelism, dynamicRangeDb, colormap);
        return await ProcessFilesAsync(files, options, progress, null, cancellationToken);
    }

    /// <summary>
    /// Process specific audio files with direct callback (no IProgress marshaling).
    /// </summary>
    public static async Task<PipelineResult> RunFilesAsync(
        IReadOnlyList<AudioFileInfo> files,
        string outputDirectory,
        int fftSize = 4096,
        int hopSize = 512,
        int imageHeight = 512,
        int maxDegreeOfParallelism = 0,
        float dynamicRangeDb = 90f,
        string colormap = "Hot",
        Action<PipelineProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var options = new PipelineOptions("", outputDirectory, fftSize, hopSize, imageHeight, maxDegreeOfParallelism, dynamicRangeDb, colormap);
        return await ProcessFilesAsync(files, options, null, onProgress, cancellationToken);
    }

    private static async Task<PipelineResult> ProcessFilesAsync(
        IReadOnlyList<AudioFileInfo> files,
        PipelineOptions options,
        IProgress<PipelineProgress>? progress,
        Action<PipelineProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var errors = new List<string>();
        var processedFileInfos = new ConcurrentBag<ProcessedFileInfo>();
        int processedCount = 0;
        int failedCount = 0;

        if (files.Count == 0)
        {
            return new PipelineResult(0, 0, 0, errors, DateTime.UtcNow - startTime, new List<ProcessedFileInfo>());
        }

        // Ensure output directory exists
        Directory.CreateDirectory(options.OutputDirectory);

        int maxParallelism = options.MaxDegreeOfParallelism > 0
            ? options.MaxDegreeOfParallelism
            : Environment.ProcessorCount;  // use all cores but not unlimited

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism,
            CancellationToken = cancellationToken
        };

        // Process in parallel
        await Parallel.ForEachAsync(files, parallelOptions, async (file, ct) =>
        {
            ct.ThrowIfCancellationRequested();

            // Report start of file processing
            var startProgress = new PipelineProgress(
                files.Count,
                Interlocked.CompareExchange(ref processedCount, 0, 0),
                Interlocked.CompareExchange(ref failedCount, 0, 0),
                file.FileName);
            progress?.Report(startProgress);
            onProgress?.Invoke(startProgress);

            try
            {
                // Decode audio to mono float[]
                var audioData = AudioDecoder.Decode(file.FilePath);

                // FFT → spectrogram (no Mel scale — renderer uses log-frequency mapping directly)
                var spectrogram = FftProcessor.Process(
                    audioData.Samples, audioData.SampleRate,
                    options.FftSize, options.HopSize);

                // Render directly (log-frequency mapping is done in the renderer)
                string outputFileName = Path.GetFileNameWithoutExtension(file.FileName) + ".png";
                string outputPath = Path.Combine(options.OutputDirectory, outputFileName);

                var thumbnailData = SpectrogramRenderer.Render(spectrogram, outputPath, options.ImageHeight,
                    dynamicRangeDb: options.DynamicRangeDb, colormap: options.Colormap);

                // Collect metadata for the processed file
                processedFileInfos.Add(new ProcessedFileInfo(
                    file.FilePath,
                    outputPath,
                    audioData.Duration.TotalSeconds,
                    audioData.SampleRate,
                    thumbnailData));

                int done = Interlocked.Increment(ref processedCount);

                // Report completion
                var doneProgress = new PipelineProgress(
                    files.Count, done,
                    Interlocked.CompareExchange(ref failedCount, 0, 0),
                    file.FileName);
                progress?.Report(doneProgress);
                onProgress?.Invoke(doneProgress);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                int failed = Interlocked.Increment(ref failedCount);

                // Report failure progress
                var failProgress = new PipelineProgress(
                    files.Count,
                    Interlocked.CompareExchange(ref processedCount, 0, 0),
                    failed,
                    file.FileName);
                progress?.Report(failProgress);
                onProgress?.Invoke(failProgress);

                lock (errors)
                {
                    errors.Add($"{file.FileName}: {ex.Message}");
                }
            }
        });

        var elapsed = DateTime.UtcNow - startTime;

        return new PipelineResult(
            files.Count,
            Interlocked.CompareExchange(ref processedCount, 0, 0),
            Interlocked.CompareExchange(ref failedCount, 0, 0),
            errors,
            elapsed,
            processedFileInfos.ToList());
    }
}
