using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Audio;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Audio2Image.Core.Embeddings;

/// <summary>
/// Computes audio embeddings using PANNs CNN14 (16kHz) ONNX model.
/// Input: audio file → decode → resample to 16kHz → log-mel spectrogram → ONNX → 2048-dim embedding.
/// </summary>
public class AudioEmbeddingService : IAudioEmbeddingService
{
    private InferenceSession? _session;
    private bool _disposed;

    // CNN14 16kHz model parameters
    private const int TargetSampleRate = 16000;
    private const int MelBins = 64;
    private const int FftSize = 512;
    private const int HopSize = 160;
    private const float FMin = 50f;
    private const float FMax = 8000f;
    private const int MaxAudioLengthSeconds = 10; // Process first 10 seconds

    public bool IsModelAvailable => _session != null;
    public int EmbeddingDimension => 2048;
    public string ModelName => "panns_cnn14_v2";

    public void LoadModel(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("ONNX model file not found.", modelPath);

        _session?.Dispose();

        using var options = new SessionOptions();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.InterOpNumThreads = 1;
        options.IntraOpNumThreads = Environment.ProcessorCount;

        _session = new InferenceSession(modelPath, options);
    }

    public float[] ComputeEmbedding(string audioFilePath)
    {
        var (embedding, _) = ComputeEmbeddingAndTags(audioFilePath, topN: 0);
        return embedding;
    }

    public (float[] Embedding, List<(string Label, float Probability)> Tags) ComputeEmbeddingAndTags(
        string audioFilePath, int topN = 5, float threshold = 0.1f)
    {
        if (_session == null)
            throw new InvalidOperationException("Model not loaded. Call LoadModel first.");

        // 1. Decode audio to mono float[]
        var audioData = AudioDecoder.Decode(audioFilePath);

        // 2. Resample to 16kHz if needed
        var samples = audioData.SampleRate != TargetSampleRate
            ? Resample(audioData.Samples, audioData.SampleRate, TargetSampleRate)
            : audioData.Samples;

        // 3. Trim to max length (10 seconds)
        int maxSamples = TargetSampleRate * MaxAudioLengthSeconds;
        if (samples.Length > maxSamples)
            samples = samples[..maxSamples];

        // 4. Run ONNX inference on raw waveform (model does mel internally)
        return RunInference(samples, topN, threshold);
    }

    /// <summary>
    /// Simple linear resampling. For better quality, NAudio WdlResampler could be used,
    /// but for embedding computation linear interpolation is sufficient.
    /// </summary>
    private static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate) return input;

        double ratio = (double)fromRate / toRate;
        int outputLength = (int)(input.Length / ratio);
        var output = new float[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            double srcIndex = i * ratio;
            int idx = (int)srcIndex;
            float frac = (float)(srcIndex - idx);

            if (idx + 1 < input.Length)
                output[i] = input[idx] * (1 - frac) + input[idx + 1] * frac;
            else if (idx < input.Length)
                output[i] = input[idx];
        }

        return output;
    }

    /// <summary>
    /// Run ONNX inference on raw waveform samples (16kHz mono).
    /// Model input: "input_audio" [batch, samples] — raw waveform.
    /// Model outputs: "clip_scores" [1,527] (sigmoid probs) + "embedding" [1,2048].
    /// </summary>
    private (float[] Embedding, List<(string Label, float Probability)> Tags) RunInference(
        float[] samples, int topN = 5, float threshold = 0.1f)
    {
        if (_session == null)
            throw new InvalidOperationException("Model not loaded.");

        // Input: raw waveform [1, numSamples]
        var inputMeta = _session.InputMetadata;
        var inputName = inputMeta.Keys.First();

        var inputTensor = new DenseTensor<float>(new[] { 1, samples.Length });
        for (int i = 0; i < samples.Length; i++)
            inputTensor[0, i] = samples[i];

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        };

        using var results = _session.Run(inputs);

        // --- Extract embedding (output name: "embedding") ---
        var embeddingOutput = results.FirstOrDefault(r =>
                r.Name.Equals("embedding", StringComparison.OrdinalIgnoreCase))
            ?? results.FirstOrDefault(r =>
                r.Name.Contains("embedding", StringComparison.OrdinalIgnoreCase))
            ?? results.OrderByDescending(r => r.AsTensor<float>()?.Length ?? 0).First();

        var embTensor = embeddingOutput.AsTensor<float>();
        if (embTensor == null)
            throw new InvalidOperationException("Failed to get embedding tensor from ONNX output.");

        var embedding = new float[embTensor.Length > EmbeddingDimension ? EmbeddingDimension : (int)embTensor.Length];
        int idx = 0;
        foreach (var val in embTensor)
        {
            if (idx >= embedding.Length) break;
            embedding[idx++] = val;
        }

        // --- Extract classification tags (output name: "clip_scores") ---
        var tags = new List<(string Label, float Probability)>();
        if (topN > 0)
        {
            // Look for "clip_scores" or "clipwise" output (527 sigmoid probabilities)
            var clipOutput = results.FirstOrDefault(r =>
                    r.Name.Equals("clip_scores", StringComparison.OrdinalIgnoreCase))
                ?? results.FirstOrDefault(r =>
                    r.Name.Contains("clip", StringComparison.OrdinalIgnoreCase));

            if (clipOutput != null)
            {
                var clipTensor = clipOutput.AsTensor<float>();
                if (clipTensor != null && clipTensor.Length >= AudioSetLabels.NumClasses)
                {
                    var probs = new float[AudioSetLabels.NumClasses];
                    int ci = 0;
                    foreach (var val in clipTensor)
                    {
                        if (ci >= AudioSetLabels.NumClasses) break;
                        probs[ci++] = val;
                    }
                    tags = AudioSetLabels.GetTopLabels(probs, topN, threshold);
                }
            }
        }

        return (embedding, tags);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
        _session = null;
    }
}
