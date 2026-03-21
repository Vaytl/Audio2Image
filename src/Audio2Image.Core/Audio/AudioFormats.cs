namespace Audio2Image.Core.Audio;

/// <summary>
/// Shared audio format constants used across decoder and playback service.
/// </summary>
public static class AudioFormats
{
    public static readonly HashSet<string> VorbisExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ogg"
    };

    public static bool IsVorbis(string filePath)
        => VorbisExtensions.Contains(Path.GetExtension(filePath));
}
