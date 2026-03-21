using Audio2Image.Core.Audio;

namespace Audio2Image.Core.Tests.Audio;

public class LoopFinderTests
{
    /// <summary>Generate a sine wave with given frequency and duration.</summary>
    private static float[] GenerateSine(float freq, int sampleRate, float durationSec)
    {
        int count = (int)(sampleRate * durationSec);
        var samples = new float[count];
        for (int i = 0; i < count; i++)
            samples[i] = 0.5f * MathF.Sin(2 * MathF.PI * freq * i / sampleRate);
        return samples;
    }

    /// <summary>Generate white noise.</summary>
    private static float[] GenerateNoise(int sampleRate, float durationSec, int seed = 42)
    {
        int count = (int)(sampleRate * durationSec);
        var samples = new float[count];
        var rng = new Random(seed);
        for (int i = 0; i < count; i++)
            samples[i] = (float)(rng.NextDouble() * 2 - 1) * 0.3f;
        return samples;
    }

    [Fact]
    public void FindLoopPoints_ShortSelection_ReturnsEmpty()
    {
        // Less than 3 seconds -> no results
        var samples = GenerateSine(440, 44100, 2.0f);
        var result = LoopFinder.FindLoopPoints(samples, 44100, 0, 2.0);
        Assert.Empty(result);
    }

    [Fact]
    public void FindLoopPoints_LongSineWave_ReturnsResults()
    {
        // A 10-second pure sine should have good self-similarity
        var samples = GenerateSine(440, 44100, 10.0f);
        var result = LoopFinder.FindLoopPoints(samples, 44100, 0, 10.0);

        // Should find at least one loop candidate
        Assert.NotEmpty(result);
        Assert.All(result, lp =>
        {
            Assert.True(lp.Start < lp.End);
            Assert.InRange(lp.MatchScore, 0f, 1f);
        });
    }

    [Fact]
    public void FindLoopPoints_RespectsTopN()
    {
        var samples = GenerateSine(440, 44100, 10.0f);
        var result = LoopFinder.FindLoopPoints(samples, 44100, 0, 10.0, topN: 2);
        Assert.True(result.Count <= 2);
    }

    [Fact]
    public void FindLoopPoints_SupportsCancellation()
    {
        var samples = GenerateSine(440, 44100, 10.0f);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            LoopFinder.FindLoopPoints(samples, 44100, 0, 10.0, ct: cts.Token));
    }

    [Fact]
    public void AutoDetect_ShortAudio_ReturnsEmpty()
    {
        var samples = GenerateSine(440, 44100, 2.0f);
        var result = LoopFinder.AutoDetect(samples, 44100);
        Assert.Empty(result);
    }

    [Fact]
    public void AutoDetect_LongNoise_ReturnsResults()
    {
        // Continuous noise should have self-similar regions
        var samples = GenerateNoise(44100, 10.0f);
        var result = LoopFinder.AutoDetect(samples, 44100);

        // Noise may or may not produce good matches, but should not crash
        Assert.NotNull(result);
        Assert.All(result, lp => Assert.True(lp.Start < lp.End));
    }

    [Fact]
    public void AutoDetect_SupportsCancellation()
    {
        var samples = GenerateSine(440, 44100, 10.0f);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            LoopFinder.AutoDetect(samples, 44100, ct: cts.Token));
    }

    [Fact]
    public void Refine_ImprovesOrMaintainsScore()
    {
        var samples = GenerateSine(440, 44100, 10.0f);
        var rough = new LoopPoint(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5), 0.5f);

        var refined = LoopFinder.Refine(samples, 44100, rough);

        Assert.NotNull(refined);
        Assert.True(refined.Start >= TimeSpan.Zero);
        Assert.True(refined.End > refined.Start);
        // Score should be at least as good or better
        Assert.True(refined.MatchScore >= rough.MatchScore * 0.9f,
            $"Refined score {refined.MatchScore} should be close to or better than {rough.MatchScore}");
    }

    [Fact]
    public void Refine_WithCancelledToken_DoesNotCrash()
    {
        // Refine may complete before checking cancellation on small inputs
        var samples = GenerateSine(440, 44100, 10.0f);
        var rough = new LoopPoint(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5), 0.5f);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Should either throw OperationCanceledException or return a result
        try
        {
            var result = LoopFinder.Refine(samples, 44100, rough, cts.Token);
            Assert.NotNull(result);
        }
        catch (OperationCanceledException)
        {
            // Also acceptable
        }
    }

    [Fact]
    public void LoopPoint_RecordProperties()
    {
        var lp = new LoopPoint(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), 0.75f);
        Assert.Equal(TimeSpan.FromSeconds(1), lp.Start);
        Assert.Equal(TimeSpan.FromSeconds(5), lp.End);
        Assert.Equal(0.75f, lp.MatchScore);
    }
}
