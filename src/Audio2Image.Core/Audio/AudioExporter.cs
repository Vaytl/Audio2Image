using NAudio.Wave;
using NAudio.Vorbis;

namespace Audio2Image.Core.Audio;

/// <summary>
/// Exports audio fragments in the source format.
/// WAV → WAV, MP3 → WAV (re-encoding to MP3 requires LAME which adds complexity),
/// OGG → WAV. All exports are lossless WAV for simplicity and quality.
/// </summary>
public static class AudioExporter
{
    /// <summary>
    /// Export a time range from an audio file to a new WAV file.
    /// Always exports as WAV (16-bit PCM) regardless of source format.
    /// </summary>
    /// <param name="sourceFilePath">Source audio file (MP3/WAV/OGG)</param>
    /// <param name="outputPath">Destination .wav file path</param>
    /// <param name="startTime">Start of region to export</param>
    /// <param name="endTime">End of region to export</param>
    /// <param name="crossfadeMs">Crossfade duration in ms for seamless loop (0 = no crossfade)</param>
    public static void ExportRange(
        string sourceFilePath,
        string outputPath,
        TimeSpan startTime,
        TimeSpan endTime,
        int crossfadeMs = 0)
    {
        if (endTime <= startTime)
            throw new ArgumentException("End time must be after start time.");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Read source samples
        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        using WaveStream reader = ext == ".ogg"
            ? new VorbisWaveReader(sourceFilePath)
            : new AudioFileReader(sourceFilePath);

        var format = reader.WaveFormat;
        int sampleRate = format.SampleRate;
        int channels = format.Channels;
        int bitsPerSample = 16;

        // Convert to sample provider for uniform reading
        ISampleProvider sampleProvider = ext == ".ogg"
            ? (ISampleProvider)reader
            : ((AudioFileReader)reader);

        // Calculate sample positions
        long startSamplePos = (long)(startTime.TotalSeconds * sampleRate) * channels;
        long endSamplePos = (long)(endTime.TotalSeconds * sampleRate) * channels;
        int totalSamples = (int)(endSamplePos - startSamplePos);

        if (totalSamples <= 0) return;

        // Seek to start position
        reader.CurrentTime = startTime;

        // Read all samples in the range
        var buffer = new float[totalSamples];
        int read = 0;
        while (read < totalSamples)
        {
            int toRead = Math.Min(4096, totalSamples - read);
            int got = sampleProvider.Read(buffer, read, toRead);
            if (got == 0) break;
            read += got;
        }

        // Apply crossfade for seamless looping if requested
        if (crossfadeMs > 0)
        {
            int crossfadeSamples = (int)(crossfadeMs / 1000.0 * sampleRate) * channels;
            crossfadeSamples = Math.Min(crossfadeSamples, read / 4); // max 25% of loop

            if (crossfadeSamples > channels)
            {
                // Read crossfade tail: samples from BEFORE the loop start (clamp to zero to avoid negative seek)
                var seekBack = TimeSpan.FromSeconds((double)crossfadeSamples / channels / sampleRate);
                reader.CurrentTime = startTime > seekBack ? startTime - seekBack : TimeSpan.Zero;
                var tailBuffer = new float[crossfadeSamples];
                int tailRead = 0;
                while (tailRead < crossfadeSamples)
                {
                    int toRead = Math.Min(4096, crossfadeSamples - tailRead);
                    int got = sampleProvider.Read(tailBuffer, tailRead, toRead);
                    if (got == 0) break;
                    tailRead += got;
                }

                // Apply crossfade: blend end of loop with beginning
                int fadeLen = Math.Min(crossfadeSamples, read);
                int startOffset = read - fadeLen;
                for (int i = 0; i < fadeLen && i < tailRead; i++)
                {
                    float t = (float)i / fadeLen;
                    // Fade out the loop end, fade in the pre-loop tail
                    buffer[startOffset + i] = buffer[startOffset + i] * (1f - t) + tailBuffer[i] * t;
                }
            }
        }

        // Write WAV
        var outFormat = new WaveFormat(sampleRate, bitsPerSample, channels);
        using var writer = new WaveFileWriter(outputPath, outFormat);

        // Convert float samples to 16-bit PCM
        for (int i = 0; i < read; i++)
        {
            float sample = Math.Clamp(buffer[i], -1f, 1f);
            writer.WriteSample(sample);
        }
    }

    /// <summary>
    /// Suggest an output file name for a loop export.
    /// </summary>
    public static string SuggestFileName(string sourceFilePath, TimeSpan start, TimeSpan end)
    {
        string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
        string startStr = $"{start.Minutes:D2}m{start.Seconds:D2}s";
        string endStr = $"{end.Minutes:D2}m{end.Seconds:D2}s";
        return $"{baseName}_loop_{startStr}-{endStr}.wav";
    }
}
