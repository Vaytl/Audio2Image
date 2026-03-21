using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Models;

namespace Audio2Image.Core.Pipeline;

/// <summary>
/// Instance wrapper around the static SpectrogramPipeline for DI.
/// </summary>
public class SpectrogramPipelineInstance : ISpectrogramPipeline
{
    public Task<PipelineResult> RunAsync(
        PipelineOptions options,
        Action<PipelineProgress> onProgress,
        CancellationToken cancellationToken = default)
        => SpectrogramPipeline.RunAsync(options, onProgress, cancellationToken);

    public Task<PipelineResult> RunFilesAsync(
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
        => SpectrogramPipeline.RunFilesAsync(files, outputDirectory, fftSize, hopSize,
            imageHeight, maxDegreeOfParallelism, dynamicRangeDb, colormap,
            onProgress, cancellationToken);
}
