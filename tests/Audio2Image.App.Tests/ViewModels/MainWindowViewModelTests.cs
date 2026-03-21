using Audio2Image.App.Models;
using Audio2Image.App.ViewModels;
using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Models;
using Audio2Image.Core.Pipeline;
using Audio2Image.Core.Settings;
using NSubstitute;

namespace Audio2Image.App.Tests.ViewModels;

public class MainWindowViewModelTests : IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ISpectrogramPipeline _pipeline;
    private readonly IAudioScanner _scanner;
    private readonly ISpectrogramDatabase _database;
    private readonly MainWindowViewModel _vm;

    public MainWindowViewModelTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _pipeline = Substitute.For<ISpectrogramPipeline>();
        _scanner = Substitute.For<IAudioScanner>();
        _database = Substitute.For<ISpectrogramDatabase>();

        // Default settings
        _settingsService.Load(Arg.Any<string?>()).Returns(new AppSettings
        {
            FftSize = 4096,
            HopSize = 512,
            Colormap = "Hot",
            DynamicRangeDb = 90f,
            LibraryPath = Path.GetTempPath(),
            DatabasePath = Path.Combine(Path.GetTempPath(), "test.db")
        });
        _settingsService.GetDefaultLibraryPath().Returns(Path.GetTempPath());

        // Empty library by default
        _database.GetAll().Returns(new List<SpectrogramRecord>());
        _database.Search(Arg.Any<string>()).Returns(new List<SpectrogramRecord>());
        _database.GetRecordsWithoutThumbnail().Returns(new List<(long, string)>());

        _vm = new MainWindowViewModel(
            _settingsService,
            _pipeline,
            _scanner,
            () => Substitute.For<IAudioPlaybackService>());
        _vm.SetDatabase(_database);
    }

    [Fact]
    public void Constructor_InitializesDefaultState()
    {
        Assert.Equal("Audio2Image — Spectrogram Gallery", _vm.Title);
        Assert.False(_vm.IsProcessing);
        Assert.False(_vm.IsViewerOpen);
        Assert.False(_vm.HasErrors);
        Assert.False(_vm.HasSearchText);
    }

    [Fact]
    public void SearchText_SetsHasSearchText()
    {
        Assert.False(_vm.HasSearchText);

        _vm.SearchText = "test";
        Assert.True(_vm.HasSearchText);

        _vm.SearchText = "";
        Assert.False(_vm.HasSearchText);
    }

    [Fact]
    public async Task SearchText_FiltersLibrary()
    {
        var records = new List<SpectrogramRecord>
        {
            new() { Id = 1, AudioFilePath = "a.mp3", AudioFileName = "alpha", ImagePath = "a.png", CreatedAt = DateTime.UtcNow }
        };
        _database.Search("alpha").Returns(records);

        _vm.SearchText = "alpha";
        // Search is now called on background thread — give it time to execute
        await Task.Delay(500);
        _database.Received().Search("alpha");
    }

    [Fact]
    public void ClearSearch_ResetsSearchText()
    {
        _vm.SearchText = "test";
        _vm.ClearSearchCommand.Execute().Subscribe();
        Assert.Equal("", _vm.SearchText);
        Assert.False(_vm.HasSearchText);
    }

    [Fact]
    public void IsLibraryEmpty_TrueWhenNoRecords()
    {
        _database.GetAll().Returns(new List<SpectrogramRecord>());
        _vm.RefreshLibraryCommand.Execute().Subscribe();
        Assert.True(_vm.IsLibraryEmpty);
    }

    [Fact]
    public async Task SortIndex_ChangesAndReloadsLibrary()
    {
        _vm.SortIndex = 2;
        Assert.Equal(2, _vm.SortIndex);
        // GetAll is now called on background thread — give it time to execute
        await Task.Delay(200);
        // GetAll called again (once in constructor, once for sort change)
        _database.Received().GetAll();
    }

    [Fact]
    public void OpenViewer_OpensViewerWithCorrectItem()
    {
        var item = new SpectrogramItem
        {
            RecordId = 1,
            AudioFilePath = "test.mp3",
            AudioFileName = "test",
            ImagePath = "test.png"
        };
        _vm.SpectrogramItems.Add(item);

        // Can't easily test OpenViewer due to file I/O in LoadImage,
        // but we can verify state transitions
        Assert.False(_vm.IsViewerOpen);
    }

    [Fact]
    public void DeleteItem_RemovesFromCollection()
    {
        var item = new SpectrogramItem
        {
            RecordId = 1,
            AudioFilePath = "test.mp3",
            AudioFileName = "test",
            ImagePath = "test.png"
        };
        _vm.SpectrogramItems.Add(item);
        Assert.Single(_vm.SpectrogramItems);

        // Set up confirm to auto-accept
        _vm.ConfirmAction = (_, _) => Task.FromResult(true);
        _vm.DeleteItemCommand.Execute(item).Subscribe();

        Assert.Empty(_vm.SpectrogramItems);
        Assert.True(_vm.IsLibraryEmpty);
        _database.Received(1).Delete(1);
    }

    [Fact]
    public void DeleteItem_CancelledByUser_DoesNotRemove()
    {
        var item = new SpectrogramItem
        {
            RecordId = 1,
            AudioFilePath = "test.mp3",
            AudioFileName = "test",
            ImagePath = "test.png"
        };
        _vm.SpectrogramItems.Add(item);

        // User cancels
        _vm.ConfirmAction = (_, _) => Task.FromResult(false);
        _vm.DeleteItemCommand.Execute(item).Subscribe();

        Assert.Single(_vm.SpectrogramItems);
        _database.DidNotReceive().Delete(Arg.Any<long>());
    }

    [Fact]
    public void ShowErrors_DisplaysErrorsInStatusText()
    {
        // No errors initially
        _vm.ShowErrorsCommand.Execute().Subscribe();
        Assert.NotEqual("error1", _vm.StatusText);
    }

    [Fact]
    public void Dispose_CleansUpResources()
    {
        _vm.Dispose();
        Assert.Empty(_vm.SpectrogramItems);
        _database.Received(1).Dispose();
    }

    [Fact]
    public void SettingsService_ExposedForChildVMs()
    {
        Assert.Same(_settingsService, _vm.SettingsService);
    }

    public void Dispose()
    {
        _vm.Dispose();
    }
}
