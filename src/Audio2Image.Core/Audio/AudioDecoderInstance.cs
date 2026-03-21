using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Models;

namespace Audio2Image.Core.Audio;

/// <summary>
/// Instance wrapper around the static AudioDecoder for DI.
/// </summary>
public class AudioDecoderInstance : IAudioDecoder
{
    public AudioData Decode(string filePath) => AudioDecoder.Decode(filePath);
}
