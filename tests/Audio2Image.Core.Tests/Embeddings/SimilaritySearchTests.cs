using Audio2Image.Core.Embeddings;

namespace Audio2Image.Core.Tests.Embeddings;

public class SimilaritySearchTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        float[] a = [1f, 2f, 3f, 4f, 5f];
        float[] b = [1f, 2f, 3f, 4f, 5f];

        float sim = SimilaritySearch.CosineSimilarity(a, b);
        Assert.InRange(sim, 0.999f, 1.001f);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        float[] a = [1f, 0f, 0f, 0f];
        float[] b = [0f, 1f, 0f, 0f];

        float sim = SimilaritySearch.CosineSimilarity(a, b);
        Assert.InRange(sim, -0.001f, 0.001f);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        float[] a = [1f, 2f, 3f];
        float[] b = [-1f, -2f, -3f];

        float sim = SimilaritySearch.CosineSimilarity(a, b);
        Assert.InRange(sim, -1.001f, -0.999f);
    }

    [Fact]
    public void CosineSimilarity_LargeVectors_WorksCorrectly()
    {
        // Test with 2048-dim vectors (same as PANNs output)
        var a = new float[2048];
        var b = new float[2048];
        var rng = new Random(42);

        for (int i = 0; i < 2048; i++)
        {
            a[i] = (float)(rng.NextDouble() * 2 - 1);
            b[i] = a[i]; // identical
        }

        float sim = SimilaritySearch.CosineSimilarity(a, b);
        Assert.InRange(sim, 0.999f, 1.001f);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZero()
    {
        float[] a = [1f, 2f, 3f];
        float[] b = [0f, 0f, 0f];

        float sim = SimilaritySearch.CosineSimilarity(a, b);
        Assert.InRange(sim, -0.01f, 0.01f);
    }

    [Fact]
    public void FindSimilar_ReturnsTopN_SortedByScore()
    {
        float[] query = [1f, 0f, 0f];
        var library = new Dictionary<long, float[]>
        {
            [1] = [1f, 0f, 0f],     // identical → 1.0
            [2] = [0.9f, 0.1f, 0f], // very similar
            [3] = [0f, 1f, 0f],     // orthogonal → 0.0
            [4] = [-1f, 0f, 0f],    // opposite → -1.0
            [5] = [0.5f, 0.5f, 0f], // somewhat similar
        };

        var results = SimilaritySearch.FindSimilar(query, library, topN: 3);

        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0].Id); // most similar
        Assert.True(results[0].Score > results[1].Score);
        Assert.True(results[1].Score > results[2].Score);
    }

    [Fact]
    public void FindSimilar_ExcludesSpecifiedId()
    {
        float[] query = [1f, 0f, 0f];
        var library = new Dictionary<long, float[]>
        {
            [1] = [1f, 0f, 0f],   // would be top, but excluded
            [2] = [0.9f, 0.1f, 0f],
            [3] = [0.5f, 0.5f, 0f],
        };

        var results = SimilaritySearch.FindSimilar(query, library, topN: 10, excludeId: 1);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.Id == 1);
        Assert.Equal(2, results[0].Id);
    }

    [Fact]
    public void FindSimilar_EmptyLibrary_ReturnsEmpty()
    {
        float[] query = [1f, 0f, 0f];
        var library = new Dictionary<long, float[]>();

        var results = SimilaritySearch.FindSimilar(query, library, topN: 10);

        Assert.Empty(results);
    }

    [Fact]
    public void FindSimilar_TopN_LargerThanLibrary_ReturnsAll()
    {
        float[] query = [1f, 0f, 0f];
        var library = new Dictionary<long, float[]>
        {
            [1] = [1f, 0f, 0f],
            [2] = [0f, 1f, 0f],
        };

        var results = SimilaritySearch.FindSimilar(query, library, topN: 100);

        Assert.Equal(2, results.Count);
    }
}
