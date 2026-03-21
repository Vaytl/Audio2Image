using Audio2Image.Core.Settings;

namespace Audio2Image.Core.Tests.Settings;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Audio2ImageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        string path = Path.Combine(_tempDir, "nonexistent.json");
        var settings = SettingsService.Load(path);

        Assert.Equal(4096, settings.FftSize);
        Assert.Equal(512, settings.HopSize);
        Assert.Equal("Hot", settings.Colormap);
        Assert.Equal(90f, settings.DynamicRangeDb);
    }

    [Fact]
    public void Save_And_Load_RoundTrip()
    {
        string path = Path.Combine(_tempDir, "test_settings.json");
        var original = new AppSettings
        {
            FftSize = 8192,
            HopSize = 1024,
            Colormap = "Viridis",
            DynamicRangeDb = 60f,
            LibraryPath = "/my/library",
            DatabasePath = "/my/library/db.sqlite"
        };

        SettingsService.Save(original, path);
        var loaded = SettingsService.Load(path);

        Assert.Equal(8192, loaded.FftSize);
        Assert.Equal(1024, loaded.HopSize);
        Assert.Equal("Viridis", loaded.Colormap);
        Assert.Equal(60f, loaded.DynamicRangeDb);
        Assert.Equal("/my/library", loaded.LibraryPath);
    }

    [Fact]
    public void Load_CorruptedFile_ReturnsDefaults()
    {
        string path = Path.Combine(_tempDir, "corrupt.json");
        File.WriteAllText(path, "{{{{not json!}}}}");

        var settings = SettingsService.Load(path);
        Assert.Equal(4096, settings.FftSize);
    }

    [Fact]
    public void Load_CreatesFileIfNotExists()
    {
        string path = Path.Combine(_tempDir, "auto_created.json");
        Assert.False(File.Exists(path));

        SettingsService.Load(path);
        Assert.True(File.Exists(path));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
