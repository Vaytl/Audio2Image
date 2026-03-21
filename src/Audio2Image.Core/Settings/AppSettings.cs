using System.Text.Json;
using System.Text.Json.Serialization;

namespace Audio2Image.Core.Settings;

public class AppSettings
{
    public int FftSize { get; set; } = 4096;
    public int HopSize { get; set; } = 512;
    public string Colormap { get; set; } = "Hot";
    public float DynamicRangeDb { get; set; } = 90f;
    public string LibraryPath { get; set; } = "";
    public string DatabasePath { get; set; } = "";
    public bool EmbeddingsEnabled { get; set; } = true;
    public string ModelPath { get; set; } = "";
    public string Theme { get; set; } = "Dark";

    [JsonIgnore]
    public static readonly int[] AvailableFftSizes = { 2048, 4096, 8192 };

    [JsonIgnore]
    public static readonly string[] AvailableColormaps = { "Hot", "Viridis" };

    [JsonIgnore]
    public static readonly string[] AvailableThemes = { "Dark", "Light" };
}
