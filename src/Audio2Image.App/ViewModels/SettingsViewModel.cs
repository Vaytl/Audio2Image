using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Settings;
using ReactiveUI;

namespace Audio2Image.App.ViewModels;

public class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IDisposable _fftSizeSubscription;

    private int _selectedFftSizeIndex;
    private decimal _hopSize;
    private int _selectedColormapIndex;
    private float _dynamicRangeDb;
    private string _libraryPath;
    private int _maxHopSize = 4096;
    private string _statusMessage = "";
    private int _selectedThemeIndex;

    public int[] AvailableFftSizes => AppSettings.AvailableFftSizes;
    public string[] AvailableColormaps => AppSettings.AvailableColormaps;
    public string[] AvailableThemes => AppSettings.AvailableThemes;

    public int SelectedThemeIndex
    {
        get => _selectedThemeIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedThemeIndex, value);
    }

    public int SelectedFftSizeIndex
    {
        get => _selectedFftSizeIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedFftSizeIndex, value);
    }

    public decimal HopSize
    {
        get => _hopSize;
        set => this.RaiseAndSetIfChanged(ref _hopSize, value);
    }

    public int SelectedColormapIndex
    {
        get => _selectedColormapIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedColormapIndex, value);
    }

    public float DynamicRangeDb
    {
        get => _dynamicRangeDb;
        set => this.RaiseAndSetIfChanged(ref _dynamicRangeDb, value);
    }

    public string LibraryPath
    {
        get => _libraryPath;
        set => this.RaiseAndSetIfChanged(ref _libraryPath, value);
    }

    public int MaxHopSize
    {
        get => _maxHopSize;
        set => this.RaiseAndSetIfChanged(ref _maxHopSize, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetDefaultsCommand { get; }

    public Action? OnSave { get; set; }
    public Action? OnCancel { get; set; }

    // Delegate set by the View for folder picker
    public Func<Task<string?>>? FolderPicker { get; set; }
    public ReactiveCommand<Unit, Unit> BrowseLibraryCommand { get; }

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        var settings = _settingsService.Load();

        // Initialize from current settings
        _selectedFftSizeIndex = Array.IndexOf(AppSettings.AvailableFftSizes, settings.FftSize);
        if (_selectedFftSizeIndex < 0) _selectedFftSizeIndex = 1; // default 4096
        _hopSize = settings.HopSize;
        _selectedColormapIndex = Array.IndexOf(AppSettings.AvailableColormaps, settings.Colormap);
        if (_selectedColormapIndex < 0) _selectedColormapIndex = 0;
        _dynamicRangeDb = settings.DynamicRangeDb;
        _libraryPath = settings.LibraryPath;
        _selectedThemeIndex = Array.IndexOf(AppSettings.AvailableThemes, settings.Theme);
        if (_selectedThemeIndex < 0) _selectedThemeIndex = 0;

        // Initialize MaxHopSize from current FFT size
        if (_selectedFftSizeIndex >= 0 && _selectedFftSizeIndex < AppSettings.AvailableFftSizes.Length)
            _maxHopSize = AppSettings.AvailableFftSizes[_selectedFftSizeIndex];

        SaveCommand = ReactiveCommand.Create(Save);
        CancelCommand = ReactiveCommand.Create(() => OnCancel?.Invoke());
        BrowseLibraryCommand = ReactiveCommand.CreateFromTask(BrowseLibrary);
        ResetDefaultsCommand = ReactiveCommand.Create(ResetDefaults);

        // Update MaxHopSize when FFT size changes
        _fftSizeSubscription = this.WhenAnyValue(x => x.SelectedFftSizeIndex)
            .Subscribe(idx =>
            {
                if (idx >= 0 && idx < AppSettings.AvailableFftSizes.Length)
                {
                    MaxHopSize = AppSettings.AvailableFftSizes[idx];
                    if (HopSize > MaxHopSize)
                        HopSize = MaxHopSize;
                }
            });
    }

    private void Save()
    {
        var fftSize = AppSettings.AvailableFftSizes[SelectedFftSizeIndex];
        if ((int)HopSize > fftSize)
        {
            StatusMessage = $"Hop Size ({(int)HopSize}) must be less than or equal to FFT Size ({fftSize}).";
            return;
        }

        StatusMessage = "";
        var settings = _settingsService.Load();
        settings.FftSize = fftSize;
        settings.HopSize = (int)HopSize;
        settings.Colormap = AppSettings.AvailableColormaps[SelectedColormapIndex];
        settings.DynamicRangeDb = DynamicRangeDb;
        settings.LibraryPath = LibraryPath;
        settings.Theme = AppSettings.AvailableThemes[SelectedThemeIndex];
        settings.DatabasePath = Path.Combine(LibraryPath, "audio2image.db");
        _settingsService.Save(settings);

        // Apply theme at runtime
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = settings.Theme == "Light"
                ? ThemeVariant.Light : ThemeVariant.Dark;
        }

        OnSave?.Invoke();
    }

    private void ResetDefaults()
    {
        var defaults = new AppSettings();
        SelectedFftSizeIndex = Array.IndexOf(AppSettings.AvailableFftSizes, defaults.FftSize);
        if (SelectedFftSizeIndex < 0) SelectedFftSizeIndex = 1;
        HopSize = defaults.HopSize;
        SelectedColormapIndex = Array.IndexOf(AppSettings.AvailableColormaps, defaults.Colormap);
        if (SelectedColormapIndex < 0) SelectedColormapIndex = 0;
        DynamicRangeDb = defaults.DynamicRangeDb;
        SelectedThemeIndex = Array.IndexOf(AppSettings.AvailableThemes, defaults.Theme);
        if (SelectedThemeIndex < 0) SelectedThemeIndex = 0;
        // Don't reset LibraryPath — keep user's storage choice
    }

    private async Task BrowseLibrary()
    {
        if (FolderPicker == null) return;
        var path = await FolderPicker();
        if (!string.IsNullOrEmpty(path))
            LibraryPath = path;
    }

    public void Dispose()
    {
        _fftSizeSubscription.Dispose();
    }
}
