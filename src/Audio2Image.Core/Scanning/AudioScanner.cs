namespace Audio2Image.Core.Scanning;

using Audio2Image.Core.Models;

public static class AudioScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg"
    };

    public static IReadOnlyList<AudioFileInfo> Scan(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return Array.Empty<AudioFileInfo>();

        return Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .Select(f => new FileInfo(f))
            .Select(fi => new AudioFileInfo(fi.FullName, fi.Name, fi.Length))
            .OrderBy(a => a.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
