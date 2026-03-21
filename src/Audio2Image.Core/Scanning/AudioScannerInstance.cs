using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Models;

namespace Audio2Image.Core.Scanning;

/// <summary>
/// Instance wrapper around the static AudioScanner for DI.
/// </summary>
public class AudioScannerInstance : IAudioScanner
{
    public IReadOnlyList<AudioFileInfo> Scan(string directoryPath) => AudioScanner.Scan(directoryPath);
}
