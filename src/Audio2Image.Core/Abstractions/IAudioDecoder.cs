using Audio2Image.Core.Models;

namespace Audio2Image.Core.Abstractions;

/// <summary>
/// Abstraction for decoding audio files into raw sample data.
/// </summary>
public interface IAudioDecoder
{
    AudioData Decode(string filePath);
}
