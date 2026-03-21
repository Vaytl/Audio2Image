using Audio2Image.Core.Models;
using Audio2Image.Core.Pipeline;

namespace Audio2Image.Core.Abstractions;

/// <summary>
/// Abstraction for the spectrogram processing pipeline.
/// </summary>
public interface ISpectrogramPipeline
{
    Task<PipelineResult> RunAsync(
        PipelineOptions options,
        Action<PipelineProgress> onProgress,
        CancellationToken cancellationToken = default);

    Task<PipelineResult> RunFilesAsync(
        IReadOnlyList<AudioFileInfo> files,
        string outputDirectory,
        int fftSize = 4096,
        int hopSize = 512,
        int imageHeight = 512,
        int maxDegreeOfParallelism = 0,
        float dynamicRangeDb = 90f,
        string colormap = "Hot",
        Action<PipelineProgress>? onProgress = null,
        CancellationToken cancellationToken = default);
}
