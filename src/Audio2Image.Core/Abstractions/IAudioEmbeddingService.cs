namespace Audio2Image.Core.Abstractions;

/// <summary>
/// Service for computing audio embeddings using an ONNX model (PANNs CNN14).
/// </summary>
public interface IAudioEmbeddingService : IDisposable
{
    /// <summary>Whether the ONNX model file is available on disk.</summary>
    bool IsModelAvailable { get; }

    /// <summary>Load the ONNX model. Must be called before ComputeEmbedding.</summary>
    void LoadModel(string modelPath);

    /// <summary>
    /// Compute a 2048-dim embedding for the given audio file.
    /// Audio is decoded, resampled to 16kHz, converted to mel spectrogram, then fed to ONNX model.
    /// </summary>
    float[] ComputeEmbedding(string audioFilePath);

    /// <summary>
    /// Compute embedding and AudioSet classification tags in a single inference pass.
    /// Returns (embedding[2048], topTags) where topTags are the top-N AudioSet classes with probabilities.
    /// </summary>
    (float[] Embedding, List<(string Label, float Probability)> Tags) ComputeEmbeddingAndTags(
        string audioFilePath, int topN = 5, float threshold = 0.1f);

    /// <summary>Embedding dimension (2048 for CNN14).</summary>
    int EmbeddingDimension { get; }

    /// <summary>Model identifier string for storage (e.g. "panns_cnn14").</summary>
    string ModelName { get; }
}
