using Audio2Image.Core.Abstractions;

namespace Audio2Image.Core.Settings;

/// <summary>
/// Instance wrapper around the static SettingsService for DI.
/// </summary>
public class SettingsServiceInstance : ISettingsService
{
    public AppSettings Load(string? path = null) => SettingsService.Load(path);
    public void Save(AppSettings settings, string? path = null) => SettingsService.Save(settings, path);
    public string GetDefaultPath() => SettingsService.GetDefaultPath();
    public string GetDefaultLibraryPath() => SettingsService.GetDefaultLibraryPath();
}
