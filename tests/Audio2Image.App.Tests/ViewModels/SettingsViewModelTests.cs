using Audio2Image.App.ViewModels;
using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Settings;
using NSubstitute;

namespace Audio2Image.App.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly ISettingsService _settingsService;

    public SettingsViewModelTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _settingsService.Load(Arg.Any<string?>()).Returns(new AppSettings
        {
            FftSize = 4096,
            HopSize = 512,
            Colormap = "Hot",
            DynamicRangeDb = 90f,
            LibraryPath = "/test/library",
            DatabasePath = "/test/library/db.sqlite"
        });
    }

    [Fact]
    public void Constructor_LoadsCurrentSettings()
    {
        var vm = new SettingsViewModel(_settingsService);

        Assert.Equal(1, vm.SelectedFftSizeIndex); // 4096 is index 1
        Assert.Equal(512m, vm.HopSize);
        Assert.Equal(0, vm.SelectedColormapIndex); // "Hot" is index 0
        Assert.Equal(90f, vm.DynamicRangeDb);
        Assert.Equal("/test/library", vm.LibraryPath);
    }

    [Fact]
    public void Constructor_SetsMaxHopSizeFromFftSize()
    {
        var vm = new SettingsViewModel(_settingsService);
        Assert.Equal(4096, vm.MaxHopSize); // FFT 4096 -> MaxHop 4096
    }

    [Fact]
    public void Save_PersistsAllSettings()
    {
        var vm = new SettingsViewModel(_settingsService);
        vm.SelectedFftSizeIndex = 2; // 8192
        vm.HopSize = 1024;
        vm.SelectedColormapIndex = 1; // Viridis
        vm.DynamicRangeDb = 60f;
        vm.LibraryPath = "/new/path";

        bool saved = false;
        vm.OnSave = () => saved = true;
        vm.SaveCommand.Execute().Subscribe();

        Assert.True(saved);
        _settingsService.Received(1).Save(Arg.Is<AppSettings>(s =>
            s.FftSize == 8192 &&
            s.HopSize == 1024 &&
            s.Colormap == "Viridis" &&
            s.DynamicRangeDb == 60f &&
            s.LibraryPath == "/new/path"),
            Arg.Any<string?>());
    }

    [Fact]
    public void Cancel_InvokesOnCancel()
    {
        var vm = new SettingsViewModel(_settingsService);
        bool cancelled = false;
        vm.OnCancel = () => cancelled = true;

        vm.CancelCommand.Execute().Subscribe();
        Assert.True(cancelled);
    }

    [Fact]
    public void ResetDefaults_RestoresDefaultValues()
    {
        var vm = new SettingsViewModel(_settingsService);
        vm.SelectedFftSizeIndex = 2; // 8192
        vm.HopSize = 2048;
        vm.SelectedColormapIndex = 1; // Viridis
        vm.DynamicRangeDb = 30f;

        vm.ResetDefaultsCommand.Execute().Subscribe();

        Assert.Equal(1, vm.SelectedFftSizeIndex); // default 4096
        Assert.Equal(512m, vm.HopSize);
        Assert.Equal(0, vm.SelectedColormapIndex); // default "Hot"
        Assert.Equal(90f, vm.DynamicRangeDb);
        // LibraryPath should NOT be reset
        Assert.Equal("/test/library", vm.LibraryPath);
    }

    [Fact]
    public void FftSizeChange_UpdatesMaxHopSize()
    {
        var vm = new SettingsViewModel(_settingsService);

        vm.SelectedFftSizeIndex = 0; // 2048
        Assert.Equal(2048, vm.MaxHopSize);

        vm.SelectedFftSizeIndex = 2; // 8192
        Assert.Equal(8192, vm.MaxHopSize);
    }

    [Fact]
    public void FftSizeChange_ClampsHopSizeIfExceeds()
    {
        var vm = new SettingsViewModel(_settingsService);
        vm.SelectedFftSizeIndex = 2; // 8192
        vm.HopSize = 8192;

        vm.SelectedFftSizeIndex = 0; // 2048 — hop 8192 > max 2048
        Assert.Equal(2048m, vm.HopSize);
    }

    [Fact]
    public void AvailableValues_AreCorrect()
    {
        var vm = new SettingsViewModel(_settingsService);
        Assert.Equal(new[] { 2048, 4096, 8192 }, vm.AvailableFftSizes);
        Assert.Equal(new[] { "Hot", "Viridis" }, vm.AvailableColormaps);
    }
}
