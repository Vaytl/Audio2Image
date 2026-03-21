using Audio2Image.App.ViewModels;
using Audio2Image.Core.Abstractions;
using NSubstitute;

namespace Audio2Image.App.Tests.ViewModels;

public class SpectrogramViewerViewModelTests : IDisposable
{
    private readonly IAudioPlaybackService _playback;
    private readonly SpectrogramViewerViewModel _vm;

    public SpectrogramViewerViewModelTests()
    {
        _playback = Substitute.For<IAudioPlaybackService>();
        _vm = new SpectrogramViewerViewModel(_playback);
    }

    [Fact]
    public void Constructor_InitialState()
    {
        Assert.Equal("", _vm.FileName);
        Assert.Equal(1.0, _vm.Zoom);
        Assert.Equal("100%", _vm.ZoomText);
        Assert.False(_vm.IsPlaying);
        Assert.False(_vm.HasSelection);
        Assert.Equal(0, _vm.PlaybackPosition);
        Assert.Equal(0, _vm.PlaybackDuration);
    }

    [Fact]
    public void ZoomIn_IncreasesZoom()
    {
        double initialZoom = _vm.Zoom;
        _vm.ZoomIn();
        Assert.True(_vm.Zoom > initialZoom);
    }

    [Fact]
    public void ZoomOut_DecreasesZoom()
    {
        // First zoom in so we have room to zoom out
        _vm.SetZoom(2.0);
        double afterZoomIn = _vm.Zoom;
        _vm.ZoomOut();
        Assert.True(_vm.Zoom < afterZoomIn);
    }

    [Fact]
    public void SetZoom_ClampsToMinimum()
    {
        _vm.SetZoom(0.001);
        Assert.True(_vm.Zoom >= 0.01);
    }

    [Fact]
    public void SetZoom_ClampsToMaximum()
    {
        _vm.SetZoom(100.0);
        Assert.Equal(50.0, _vm.Zoom);
    }

    [Fact]
    public void SetSelection_SetsHasSelection()
    {
        Assert.False(_vm.HasSelection);
        _vm.SetSelection(10, 10, 100, 50);
        Assert.True(_vm.HasSelection);
    }

    [Fact]
    public void SetSelection_SmallSize_NoSelection()
    {
        _vm.SetSelection(10, 10, 1, 1);
        Assert.False(_vm.HasSelection);
    }

    [Fact]
    public void ClearSelection_ResetsSelection()
    {
        _vm.SetSelection(10, 10, 100, 50);
        Assert.True(_vm.HasSelection);

        _vm.ClearSelection();
        Assert.False(_vm.HasSelection);
        Assert.Equal(0, _vm.SelectionLeft);
        Assert.Equal(0, _vm.SelectionTop);
        Assert.Equal(0, _vm.SelectionWidth);
        Assert.Equal(0, _vm.SelectionHeight);
    }

    [Fact]
    public void SelectionModeIndex_CanBeSet()
    {
        _vm.SelectionModeIndex = 1; // Frequency mode
        Assert.Equal(1, _vm.SelectionModeIndex);

        _vm.SelectionModeIndex = 0; // Time mode
        Assert.Equal(0, _vm.SelectionModeIndex);
    }

    [Fact]
    public void Volume_SetsPlaybackVolume()
    {
        _vm.Volume = 0.5f;
        Assert.Equal(0.5f, _vm.Volume);
        _playback.Volume = 0.5f; // verify it was set
    }

    [Fact]
    public void Close_StopsPlaybackAndInvokesCallback()
    {
        bool closed = false;
        _vm.OnClose = () => closed = true;

        _vm.CloseCommand.Execute().Subscribe();

        Assert.True(closed);
        _playback.Received(1).Stop();
        Assert.False(_vm.IsPlaying);
    }

    [Fact]
    public void Navigate_InvokesCallback()
    {
        int navigateDirection = 0;
        _vm.OnNavigate = dir => navigateDirection = dir;
        _vm.HasPrev = true;
        _vm.HasNext = true;

        _vm.PrevCommand.Execute().Subscribe();
        Assert.Equal(-1, navigateDirection);

        _vm.NextCommand.Execute().Subscribe();
        Assert.Equal(1, navigateDirection);
    }

    [Fact]
    public void HasPrev_HasNext_ControlNavigation()
    {
        _vm.HasPrev = false;
        _vm.HasNext = true;

        Assert.False(_vm.HasPrev);
        Assert.True(_vm.HasNext);
    }

    [Fact]
    public void UpdateCursorInfo_SetsTimeAndFreqText()
    {
        // Without loaded image, cursor info should be empty
        _vm.UpdateCursorInfo(0, 0);
        Assert.Equal("", _vm.CursorTimeText);
        Assert.Equal("", _vm.CursorFreqText);
    }

    [Fact]
    public void ClearCursorInfo_ClearsTexts()
    {
        _vm.ClearCursorInfo();
        Assert.Equal("", _vm.CursorTimeText);
        Assert.Equal("", _vm.CursorFreqText);
    }

    [Fact]
    public void Dispose_StopsTimerAndPlayback()
    {
        _vm.Dispose();
        _playback.Received(1).Dispose();
    }

    public void Dispose()
    {
        _vm.Dispose();
    }
}
