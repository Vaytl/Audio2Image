using System.Buffers;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Audio2Image.Core.Audio;

/// <summary>
/// Represents a detected loop region in an audio file.
/// </summary>
public record LoopPoint(
    TimeSpan Start,
    TimeSpan End,
    float MatchScore // 0..1, how well start and end match spectrally + energetically
);

/// <summary>
/// Finds optimal loop points in audio for seamless looping.
/// 
/// Algorithm v3: "Scan from end, compare with start profile"
/// 1. Compute a reference PROFILE at the start of audio (multiple overlapping descriptors)
/// 2. Scan backward from end with same profile window
/// 3. Find position where profile matches best → that's the loop-end
/// 4. Loop = play from start, when reaching loop-end jump back to start
/// 
/// Optimized for ambient/nature recordings (rain, wind, etc.) as well as music.
/// </summary>
public static class LoopFinder
{
    private const int NumMelBands = 24;
    private const float MinFreqHz = 50f;
    private const float MaxFreqHz = 16000f;

    // Profile = series of overlapping descriptors capturing temporal context
    private const int ProfileWindows = 5;       // number of windows in a profile
    private const int ProfileWindowMs = 500;     // each window = 500ms
    private const int ProfileHopMs = 200;        // hop between windows = 200ms
    // Total profile span ≈ 500 + 4*200 = 1300ms of context

    private const int ScanHopMs = 100;           // backward scan step = 100ms
    private const double MinLoopFromStart = 3.0; // loop-end must be at least 3s from start
    private const float EarlyExitScore = 0.85f;  // stop early if this score reached

    /// <summary>
    /// Find loop points within a time selection.
    /// Scans from end of selection backward, comparing with profile at start of selection.
    /// </summary>
    public static List<LoopPoint> FindLoopPoints(
        float[] samples,
        int sampleRate,
        double selectionStart,
        double selectionEnd,
        int topN = 5,
        CancellationToken ct = default)
    {
        int startSample = Math.Clamp((int)(selectionStart * sampleRate), 0, samples.Length - 1);
        int endSample = Math.Clamp((int)(selectionEnd * sampleRate), startSample + 1, samples.Length);
        int selLength = endSample - startSample;

        // Need at least 3 sec for meaningful loop
        if (selLength < sampleRate * 3)
            return [];

        return ScanFromEnd(samples, sampleRate, startSample, endSample, topN, ct);
    }

    /// <summary>
    /// Auto-detect the best loop in the entire track.
    /// Scans from end backward, comparing with profile at start.
    /// </summary>
    public static List<LoopPoint> AutoDetect(
        float[] samples,
        int sampleRate,
        int topN = 5,
        double minLoopSeconds = 1.0,
        double maxLoopSeconds = 0,
        CancellationToken ct = default)
    {
        if (samples.Length < sampleRate * 3) return [];
        return ScanFromEnd(samples, sampleRate, 0, samples.Length, topN, ct);
    }

    /// <summary>
    /// Fine-tune a loop point by searching nearby for optimal splice.
    /// </summary>
    public static LoopPoint Refine(
        float[] samples,
        int sampleRate,
        LoopPoint rough,
        CancellationToken ct = default)
    {
        int startPos = (int)(rough.Start.TotalSeconds * sampleRate);
        int endPos = (int)(rough.End.TotalSeconds * sampleRate);
        int windowSize = Math.Clamp((int)(0.05 * sampleRate), 128, 8192);
        return RefineEndPoint(samples, sampleRate, startPos, endPos, windowSize);
    }

    // ==========================================
    // Core: scan from end, compare profiles
    // ==========================================

    /// <summary>
    /// Core algorithm: compute reference profile at regionStart, scan backward from regionEnd.
    /// </summary>
    private static List<LoopPoint> ScanFromEnd(
        float[] samples, int sampleRate,
        int regionStart, int regionEnd,
        int topN, CancellationToken ct)
    {
        int profileWindowSamples = (int)(ProfileWindowMs / 1000.0 * sampleRate);
        int profileHopSamples = (int)(ProfileHopMs / 1000.0 * sampleRate);
        int profileSpan = profileWindowSamples + (ProfileWindows - 1) * profileHopSamples;
        int scanHopSamples = (int)(ScanHopMs / 1000.0 * sampleRate);

        // Not enough data for even one profile
        if (regionEnd - regionStart < profileSpan * 2)
            return [];

        // 1. Compute reference profile at start of region
        var refProfile = ComputeProfile(samples, regionStart, profileWindowSamples, profileHopSamples, sampleRate);
        if (refProfile == null) return [];

        // Precompute reference RMS over the full profile span for energy comparison
        float refRms = ComputeLocalRms(samples, regionStart, Math.Min(profileSpan, regionEnd - regionStart));

        // 2. Minimum distance from start for loop-end
        int minEndPos = regionStart + Math.Max(
            (int)(MinLoopFromStart * sampleRate),
            profileSpan * 2);

        // 3. Scan backward from end
        int scanStart = regionEnd - profileSpan;
        if (scanStart <= minEndPos) return [];

        var candidates = new List<(int endPos, float score)>();
        float bestScoreSoFar = 0f;

        for (int pos = scanStart; pos >= minEndPos; pos -= scanHopSamples)
        {
            ct.ThrowIfCancellationRequested();

            var candidateProfile = ComputeProfile(samples, pos, profileWindowSamples, profileHopSamples, sampleRate);
            if (candidateProfile == null) continue;

            float score = CompareProfiles(refProfile, candidateProfile, samples, sampleRate,
                regionStart, pos, refRms, profileSpan);

            if (score > 0.3f)
                candidates.Add((pos, score));

            if (score > bestScoreSoFar)
                bestScoreSoFar = score;

            // Early exit: found excellent match and we're far enough from end
            if (bestScoreSoFar >= EarlyExitScore && pos < scanStart - sampleRate * 5)
                break;
        }

        if (candidates.Count == 0)
            return [];

        // 4. Take top candidates and refine
        var topCandidates = candidates
            .OrderByDescending(c => c.score)
            .Take(topN * 3)
            .ToList();

        var refined = new List<LoopPoint>();
        foreach (var (endPos, _) in topCandidates)
        {
            ct.ThrowIfCancellationRequested();
            var lp = RefineEndPoint(samples, sampleRate, regionStart, endPos, profileWindowSamples);
            refined.Add(lp);
        }

        return DeduplicateLoopPoints(refined, sampleRate)
            .OrderByDescending(lp => lp.MatchScore)
            .Take(topN)
            .ToList();
    }

    // ==========================================
    // Profile computation and comparison
    // ==========================================

    /// <summary>
    /// Compute a profile = series of overlapping descriptors capturing temporal context.
    /// Returns null if not enough data.
    /// </summary>
    private static AudioDescriptor[]? ComputeProfile(
        float[] samples, int startOffset,
        int windowSamples, int hopSamples, int sampleRate)
    {
        var profile = new AudioDescriptor[ProfileWindows];
        for (int i = 0; i < ProfileWindows; i++)
        {
            int offset = startOffset + i * hopSamples;
            if (offset + windowSamples > samples.Length)
                return null;
            profile[i] = ComputeDescriptor(samples, offset, windowSamples, sampleRate);
        }
        return profile;
    }

    /// <summary>
    /// Compare two profiles (series of descriptors).
    /// Weights: Mel spectrum 40%, RMS envelope 25%, Spectral stability 15%, Waveform continuity 20%.
    /// </summary>
    private static float CompareProfiles(
        AudioDescriptor[] refProfile, AudioDescriptor[] candidateProfile,
        float[] samples, int sampleRate,
        int refPos, int candidatePos,
        float refRms, int profileSpan)
    {
        int count = Math.Min(refProfile.Length, candidateProfile.Length);

        float melSimSum = 0, rmsSimSum = 0, spectralSimSum = 0;

        for (int i = 0; i < count; i++)
        {
            var a = refProfile[i];
            var b = candidateProfile[i];

            // Mel spectrum cosine similarity (timbre)
            melSimSum += CosineSimilarity(a.MelSpectrum, b.MelSpectrum);

            // RMS similarity (energy envelope)
            float maxRms = MathF.Max(a.Rms, b.Rms);
            float rmsSim = maxRms > 1e-10f
                ? 1f - MathF.Abs(a.Rms - b.Rms) / maxRms
                : 1f;
            rmsSimSum += Math.Clamp(rmsSim, 0f, 1f);

            // Spectral shape: centroid + flux
            float centroidSim = 1f - MathF.Abs(a.SpectralCentroid - b.SpectralCentroid);
            float fluxSim = 1f - Math.Clamp(MathF.Abs(a.SpectralFlux - b.SpectralFlux) * 2f, 0f, 1f);
            spectralSimSum += centroidSim * 0.5f + fluxSim * 0.5f;
        }

        float melSim = melSimSum / count;
        float rmsSim2 = rmsSimSum / count;
        float spectralSim = Math.Clamp(spectralSimSum / count, 0f, 1f);

        // Waveform continuity at the splice point (end of candidate → jump to start)
        float continuity = WaveformContinuity(samples, refPos, candidatePos, sampleRate);

        // RMS envelope matching over full profile span (guards against gradual volume drift)
        float candRms = ComputeLocalRms(samples, candidatePos, Math.Min(profileSpan, samples.Length - candidatePos));
        float maxSpanRms = MathF.Max(refRms, candRms);
        float spanRmsSim = maxSpanRms > 1e-10f
            ? 1f - MathF.Abs(refRms - candRms) / maxSpanRms
            : 1f;
        spanRmsSim = Math.Clamp(spanRmsSim, 0f, 1f);

        // Blend individual RMS with span RMS
        float rmsTotal = rmsSim2 * 0.5f + spanRmsSim * 0.5f;

        // Weighted combination
        float score = melSim * 0.40f
                    + rmsTotal * 0.25f
                    + spectralSim * 0.15f
                    + continuity * 0.20f;

        // Severe penalty for large RMS difference (>6dB)
        if (maxSpanRms > 1e-10f)
        {
            float ratio = MathF.Min(refRms, candRms) / maxSpanRms;
            if (ratio < 0.5f) // >6dB difference
                score *= ratio;
        }

        return Math.Clamp(score, 0f, 1f);
    }

    // ==========================================
    // Multi-feature audio descriptor
    // ==========================================

    private record AudioDescriptor(
        float[] MelSpectrum,    // Mel-band energies (timbral shape)
        float Rms,              // RMS energy (volume)
        float SpectralCentroid, // Brightness
        float SpectralFlux      // Rate of spectral change
    );

    private static AudioDescriptor ComputeDescriptor(float[] samples, int offset, int windowSize, int sampleRate)
    {
        // FFT
        int fftSize = 1;
        while (fftSize < windowSize) fftSize <<= 1;
        int bins = fftSize / 2;

        var pool = ArrayPool<Complex>.Shared;
        var complexBuffer = pool.Rent(fftSize);
        float rmsSum = 0f;

        try
        {
            for (int n = 0; n < windowSize; n++)
            {
                int idx = offset + n;
                float sample = idx >= 0 && idx < samples.Length ? samples[idx] : 0f;
                float w = 0.5f * (1f - MathF.Cos(2f * MathF.PI * n / (windowSize - 1))); // Hann
                float windowed = sample * w;
                complexBuffer[n] = new Complex(windowed, 0);
                rmsSum += windowed * windowed;
            }
            for (int n = windowSize; n < fftSize; n++)
                complexBuffer[n] = Complex.Zero;

            float rms = MathF.Sqrt(rmsSum / windowSize);

            Fourier.Forward(complexBuffer, FourierOptions.NoScaling);

            // Compute spectral features from rented buffer directly
            var melSpectrum = ComputeMelBandsFromComplex(complexBuffer, bins, sampleRate, fftSize);

            float centroidNum = 0, centroidDen = 0;
            float mean = 0, maxMag = 0;
            for (int k = 0; k < bins; k++)
            {
                float mag = (float)complexBuffer[k].Magnitude;
                float freq = (float)k * sampleRate / fftSize;
                centroidNum += freq * mag;
                centroidDen += mag;
                mean += mag;
                if (mag > maxMag) maxMag = mag;
            }
            float centroid = centroidDen > 1e-10f ? centroidNum / centroidDen : 0f;
            centroid = Math.Clamp(centroid / (sampleRate / 2f), 0f, 1f);

            mean /= bins;
            float variance = 0;
            for (int k = 0; k < bins; k++)
            {
                float mag = (float)complexBuffer[k].Magnitude;
                float d = mag - mean;
                variance += d * d;
            }
            float flux = MathF.Sqrt(variance / bins);
            flux = maxMag > 1e-10f ? flux / maxMag : 0f;

            return new AudioDescriptor(melSpectrum, rms, centroid, flux);
        }
        finally
        {
            pool.Return(complexBuffer);
        }
    }

    /// <summary>Compute mel bands directly from Complex[] FFT buffer (avoids separate magnitude array).</summary>
    private static float[] ComputeMelBandsFromComplex(Complex[] complexBuffer, int bins, int sampleRate, int fftSize)
    {
        float melMin = Dsp.MelScale.HzToMel(MinFreqHz);
        float melMax = Dsp.MelScale.HzToMel(Math.Min(MaxFreqHz, sampleRate / 2f));

        var melBands = new float[NumMelBands];
        var melEdges = new float[NumMelBands + 2];

        for (int i = 0; i < melEdges.Length; i++)
            melEdges[i] = Dsp.MelScale.MelToHz(melMin + (melMax - melMin) * i / (NumMelBands + 1));

        for (int m = 0; m < NumMelBands; m++)
        {
            float lowHz = melEdges[m];
            float centerHz = melEdges[m + 1];
            float highHz = melEdges[m + 2];

            for (int k = 0; k < bins; k++)
            {
                float freq = (float)k * sampleRate / fftSize;
                float weight = 0;

                if (freq >= lowHz && freq <= centerHz && centerHz > lowHz)
                    weight = (freq - lowHz) / (centerHz - lowHz);
                else if (freq > centerHz && freq <= highHz && highHz > centerHz)
                    weight = (highHz - freq) / (highHz - centerHz);

                if (weight > 0)
                {
                    float mag = (float)complexBuffer[k].Magnitude;
                    melBands[m] += mag * mag * weight;
                }
            }

            melBands[m] = MathF.Log10(melBands[m] + 1e-10f);
        }

        return melBands;
    }

    // ==========================================
    // Waveform continuity
    // ==========================================

    private static float WaveformContinuity(float[] samples, int posA, int posB, int sampleRate)
    {
        int checkSamples = Math.Max(4, (int)(0.002 * sampleRate));

        if (posA < checkSamples || posB < checkSamples ||
            posA + checkSamples >= samples.Length || posB + checkSamples >= samples.Length)
            return 0f;

        // Amplitude at splice point — should be near zero
        float ampA = MathF.Abs(samples[posA]);
        float ampB = MathF.Abs(samples[posB]);
        float ampScore = 1f - Math.Clamp((ampA + ampB) * 3f, 0f, 1f);

        // Slope matching
        float slopeA = samples[posA + 1] - samples[posA];
        float slopeB = samples[posB + 1] - samples[posB];
        float slopeDiff = MathF.Abs(slopeA - slopeB);
        float slopeScore = 1f - Math.Clamp(slopeDiff * 5f, 0f, 1f);

        // Short-term correlation (~2ms)
        float corrSum = 0, normSumA = 0, normSumB = 0;
        for (int i = -checkSamples; i < checkSamples; i++)
        {
            float sa = samples[posA + i];
            float sb = samples[posB + i];
            corrSum += sa * sb;
            normSumA += sa * sa;
            normSumB += sb * sb;
        }
        float corrDenom = MathF.Sqrt(normSumA) * MathF.Sqrt(normSumB);
        float correlation = corrDenom > 1e-10f ? corrSum / corrDenom : 0f;
        correlation = Math.Clamp((correlation + 1f) / 2f, 0f, 1f);

        return ampScore * 0.3f + slopeScore * 0.2f + correlation * 0.5f;
    }

    // ==========================================
    // Refinement: only refine END position (start stays fixed)
    // ==========================================

    private static LoopPoint RefineEndPoint(float[] samples, int sampleRate, int startPos, int endPos, int windowSize)
    {
        int searchRange = (int)(0.01 * sampleRate); // +/- 10ms
        int refinedWindowSize = Math.Min(windowSize, (int)(0.05 * sampleRate));

        if (startPos < 0 || startPos + refinedWindowSize >= samples.Length)
            return new LoopPoint(
                TimeSpan.FromSeconds((double)startPos / sampleRate),
                TimeSpan.FromSeconds((double)endPos / sampleRate), 0f);

        var descStart = ComputeDescriptor(samples, startPos, refinedWindowSize, sampleRate);

        float bestScore = -1;
        int bestEnd = endPos;

        // Coarse: 4-sample step
        for (int de = -searchRange; de <= searchRange; de += 4)
        {
            int e = endPos + de;
            if (e <= startPos + refinedWindowSize || e + refinedWindowSize >= samples.Length) continue;

            var descE = ComputeDescriptor(samples, e, refinedWindowSize, sampleRate);

            float melSim = CosineSimilarity(descStart.MelSpectrum, descE.MelSpectrum);
            float maxRms = MathF.Max(descStart.Rms, descE.Rms);
            float rmsSim = maxRms > 1e-10f ? 1f - MathF.Abs(descStart.Rms - descE.Rms) / maxRms : 1f;
            float continuity = WaveformContinuity(samples, startPos, e, sampleRate);

            float score = melSim * 0.35f + rmsSim * 0.30f + continuity * 0.35f;

            if (score > bestScore)
            {
                bestScore = score;
                bestEnd = e;
            }
        }

        // Fine: 1-sample step around best
        int fineRange = 8;
        for (int de = -fineRange; de <= fineRange; de++)
        {
            int e = bestEnd + de;
            if (e <= startPos + refinedWindowSize || e + refinedWindowSize >= samples.Length) continue;

            float continuity = WaveformContinuity(samples, startPos, e, sampleRate);
            float rmsE = ComputeLocalRms(samples, e, Math.Min(512, refinedWindowSize));
            float maxRms = MathF.Max(descStart.Rms, rmsE);
            float rmsSim = maxRms > 1e-10f ? 1f - MathF.Abs(descStart.Rms - rmsE) / maxRms : 1f;

            float fineScore = continuity * 0.6f + rmsSim * 0.4f;

            if (fineScore > bestScore)
            {
                bestScore = fineScore;
                bestEnd = e;
            }
        }

        return new LoopPoint(
            TimeSpan.FromSeconds((double)startPos / sampleRate),
            TimeSpan.FromSeconds((double)bestEnd / sampleRate),
            Math.Clamp(bestScore, 0f, 1f));
    }

    // ==========================================
    // Utilities
    // ==========================================

    private static float ComputeLocalRms(float[] samples, int offset, int length)
    {
        float sum = 0;
        int count = 0;
        for (int i = 0; i < length; i++)
        {
            int idx = offset + i;
            if (idx >= 0 && idx < samples.Length)
            {
                sum += samples[idx] * samples[idx];
                count++;
            }
        }
        return count > 0 ? MathF.Sqrt(sum / count) : 0f;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        int len = Math.Min(a.Length, b.Length);
        float dot = 0, normA = 0, normB = 0;

        for (int i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 1e-10f ? dot / denom : 0f;
    }

    private static List<LoopPoint> DeduplicateLoopPoints(List<LoopPoint> points, int sampleRate)
    {
        if (points.Count == 0) return points;

        double regionMs = 0.2;
        var sorted = points.OrderByDescending(p => p.MatchScore).ToList();
        var result = new List<LoopPoint>();

        foreach (var p in sorted)
        {
            bool tooClose = false;
            foreach (var existing in result)
            {
                if (Math.Abs(p.End.TotalSeconds - existing.End.TotalSeconds) < regionMs)
                {
                    tooClose = true;
                    break;
                }
            }
            if (!tooClose)
                result.Add(p);
        }

        return result;
    }
}
