using Audio2Image.Core.Settings;

namespace Audio2Image.Core.Abstractions;

/// <summary>
/// Abstraction for loading and saving application settings.
/// </summary>
public interface ISettingsService
{
    AppSettings Load(string? path = null);
    void Save(AppSettings settings, string? path = null);
    string GetDefaultPath();
    string GetDefaultLibraryPath();
}
