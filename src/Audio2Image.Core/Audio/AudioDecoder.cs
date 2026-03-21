using NAudio.Wave;
using NAudio.Vorbis;
using Audio2Image.Core.Models;

namespace Audio2Image.Core.Audio;

public static class AudioDecoder
{
    public static AudioData Decode(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found.", filePath);

        bool isVorbis = AudioFormats.IsVorbis(filePath);

        // OGG Vorbis requires VorbisWaveReader; MP3/WAV use AudioFileReader
        if (isVorbis)
        {
            using var reader = new VorbisWaveReader(filePath);
            return DecodeFromSampleProvider(reader, reader.WaveFormat, reader.TotalTime);
        }
        else
        {
            using var reader = new AudioFileReader(filePath);
            return DecodeFromSampleProvider(reader, reader.WaveFormat, reader.TotalTime);
        }
    }

    private static AudioData DecodeFromSampleProvider(ISampleProvider provider, WaveFormat format, TimeSpan totalTime)
    {
        var sampleRate = format.SampleRate;
        var channels = format.Channels;

        // Pre-allocate based on estimated total samples (avoid List.Add per-sample)
        int estimatedSamples = (int)(totalTime.TotalSeconds * sampleRate * channels) + sampleRate * channels;
        var allSamples = new float[estimatedSamples];
        int totalRead = 0;

        var chunkSize = sampleRate * channels * 10; // 10 seconds per chunk
        var buffer = new float[chunkSize];
        int read;

        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            // Grow array if needed (rare — only if estimate was too small)
            if (totalRead + read > allSamples.Length)
            {
                var newSize = Math.Max(allSamples.Length * 2, totalRead + read);
                Array.Resize(ref allSamples, newSize);
            }
            Buffer.BlockCopy(buffer, 0, allSamples, totalRead * sizeof(float), read * sizeof(float));
            totalRead += read;
        }

        // Downmix to mono if stereo
        float[] mono;
        if (channels > 1)
        {
            var monoLength = totalRead / channels;
            mono = new float[monoLength];
            for (int i = 0; i < monoLength; i++)
            {
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                    sum += allSamples[i * channels + ch];
                mono[i] = sum / channels;
            }
        }
        else
        {
            // Trim to exact size without unnecessary copy if already exact
            if (totalRead == allSamples.Length)
                mono = allSamples;
            else
            {
                mono = new float[totalRead];
                Buffer.BlockCopy(allSamples, 0, mono, 0, totalRead * sizeof(float));
            }
        }

        return new AudioData(mono, sampleRate, totalTime);
    }
}
