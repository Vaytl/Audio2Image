using System.Numerics;

namespace Audio2Image.Core.Embeddings;

/// <summary>
/// Cosine similarity search using SIMD-accelerated vector operations.
/// </summary>
public static class SimilaritySearch
{
    /// <summary>
    /// Compute cosine similarity between two vectors using SIMD.
    /// Returns value in [-1, 1] where 1 = identical direction.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dot = 0f, normA = 0f, normB = 0f;
        int i = 0;
        int simdLength = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated && a.Length >= simdLength)
        {
            var vDot = Vector<float>.Zero;
            var vNormA = Vector<float>.Zero;
            var vNormB = Vector<float>.Zero;

            for (; i <= a.Length - simdLength; i += simdLength)
            {
                var va = new Vector<float>(a.Slice(i, simdLength));
                var vb = new Vector<float>(b.Slice(i, simdLength));
                vDot += va * vb;
                vNormA += va * va;
                vNormB += vb * vb;
            }

            dot = Vector.Dot(vDot, Vector<float>.One);
            normA = Vector.Dot(vNormA, Vector<float>.One);
            normB = Vector.Dot(vNormB, Vector<float>.One);
        }

        // Handle remaining elements
        for (; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 1e-10f ? dot / denom : 0f;
    }

    /// <summary>
    /// Find top-N most similar records to the query embedding.
    /// Returns list of (RecordId, SimilarityScore) ordered by descending similarity.
    /// </summary>
    public static List<(long Id, float Score)> FindSimilar(
        float[] query,
        Dictionary<long, float[]> library,
        int topN = 20,
        long? excludeId = null)
    {
        var querySpan = query.AsSpan();

        var results = new List<(long Id, float Score)>(library.Count);
        foreach (var (id, embedding) in library)
        {
            if (excludeId.HasValue && id == excludeId.Value)
                continue;

            float score = CosineSimilarity(querySpan, embedding.AsSpan());
            results.Add((id, score));
        }

        // Sort descending by score and take top N
        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (results.Count > topN)
            results.RemoveRange(topN, results.Count - topN);

        return results;
    }
}
