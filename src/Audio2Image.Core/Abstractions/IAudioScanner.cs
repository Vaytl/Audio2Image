using Audio2Image.Core.Models;

namespace Audio2Image.Core.Abstractions;

/// <summary>
/// Abstraction for scanning directories for audio files.
/// </summary>
public interface IAudioScanner
{
    IReadOnlyList<AudioFileInfo> Scan(string directoryPath);
}
