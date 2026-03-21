using Audio2Image.Core.Scanning;

namespace Audio2Image.Core.Tests.Scanning;

public class AudioScannerTests : IDisposable
{
    private readonly string _testDir;

    public AudioScannerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Audio2Image_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void Scan_EmptyDirectory_ReturnsEmpty()
    {
        var result = AudioScanner.Scan(_testDir);
        Assert.Empty(result);
    }

    [Fact]
    public void Scan_NonExistentDirectory_ReturnsEmpty()
    {
        var result = AudioScanner.Scan(Path.Combine(_testDir, "nonexistent"));
        Assert.Empty(result);
    }

    [Fact]
    public void Scan_MixedFiles_ReturnsOnlyAudio()
    {
        File.WriteAllBytes(Path.Combine(_testDir, "track.mp3"), new byte[] { 0xFF, 0xFB });
        File.WriteAllBytes(Path.Combine(_testDir, "sound.wav"), new byte[] { 0x52, 0x49 });
        File.WriteAllText(Path.Combine(_testDir, "readme.txt"), "hello");
        File.WriteAllBytes(Path.Combine(_testDir, "image.png"), new byte[] { 0x89, 0x50 });

        var result = AudioScanner.Scan(_testDir);
        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.True(
            f.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
            f.FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Scan_RecursiveSubdirectories_FindsAllAudio()
    {
        var subDir = Path.Combine(_testDir, "sub", "deep");
        Directory.CreateDirectory(subDir);
        File.WriteAllBytes(Path.Combine(_testDir, "root.mp3"), new byte[] { 0xFF });
        File.WriteAllBytes(Path.Combine(subDir, "deep.wav"), new byte[] { 0x52 });

        var result = AudioScanner.Scan(_testDir);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Scan_CaseInsensitiveExtensions()
    {
        File.WriteAllBytes(Path.Combine(_testDir, "track.MP3"), new byte[] { 0xFF });
        File.WriteAllBytes(Path.Combine(_testDir, "sound.Wav"), new byte[] { 0x52 });

        var result = AudioScanner.Scan(_testDir);
        Assert.Equal(2, result.Count);
    }
}
