using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Media.Imaging;
using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Dsp;
using Audio2Image.Core.Audio;
using ReactiveUI;

namespace Audio2Image.App.ViewModels;

public class SpectrogramViewerViewModel : ViewModelBase, IDisposable
{
    private Bitmap? _fullImage;
    private string _fileName = "";
    private double _zoom = 1.0;
    private double _fitZoom = 1.0; // zoom level at which image fits viewport
    private double _imageWidth;
    private double _imageHeight;
    private double _originalWidth;
    private double _originalHeight;
    private string _zoomText = "100%";
    private bool _hasPrev;
    private bool _hasNext;
    private bool _isPlaying;
    private double _playbackPosition;
    private double _playbackDuration;
    private string _positionText = "00:00:00.000";
    private string _durationText = "00:00:00.000";
    private float _volume = 1.0f;
    private double _cursorX;
    private bool _showCursor;
    private string _audioFilePath = "";
    private double _selectionLeft;
    private double _selectionTop;
    private double _selectionWidth;
    private double _selectionHeight;
    private bool _hasSelection;
    private string _selectionInfo = "";
    private int _selectionModeIndex; // 0 = Time, 1 = Frequency
    private string _cursorTimeText = "";
    private string _cursorFreqText = "";

    // Audio metadata for programmatic scales
    private int _sampleRate = 44100;
    private double _audioDuration;

    private readonly IAudioPlaybackService _playback;
    private readonly Action _onPlaybackStopped;
    private System.Timers.Timer? _positionTimer;
    private CancellationTokenSource? _loopSearchCts;
    private bool _isSearchingLoops;

    public Bitmap? FullImage
    {
        get => _fullImage;
        set => this.RaiseAndSetIfChanged(ref _fullImage, value);
    }

    public string FileName
    {
        get => _fileName;
        set => this.RaiseAndSetIfChanged(ref _fileName, value);
    }

    public double ImageWidth
    {
        get => _imageWidth;
        set => this.RaiseAndSetIfChanged(ref _imageWidth, value);
    }

    public double ImageHeight
    {
        get => _imageHeight;
        set => this.RaiseAndSetIfChanged(ref _imageHeight, value);
    }

    public string ZoomText
    {
        get => _zoomText;
        set => this.RaiseAndSetIfChanged(ref _zoomText, value);
    }

    public bool HasPrev
    {
        get => _hasPrev;
        set => this.RaiseAndSetIfChanged(ref _hasPrev, value);
    }

    public bool HasNext
    {
        get => _hasNext;
        set => this.RaiseAndSetIfChanged(ref _hasNext, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => this.RaiseAndSetIfChanged(ref _isPlaying, value);
    }

    public double PlaybackPosition
    {
        get => _playbackPosition;
        set
        {
            this.RaiseAndSetIfChanged(ref _playbackPosition, value);
            if (!_isUpdatingFromTimer && PlaybackDuration > 0)
            {
                _playback.Seek(TimeSpan.FromSeconds(value));
                UpdateCursorFromPosition(value);
            }
        }
    }

    public double PlaybackDuration
    {
        get => _playbackDuration;
        set => this.RaiseAndSetIfChanged(ref _playbackDuration, value);
    }

    public string PositionText
    {
        get => _positionText;
        set => this.RaiseAndSetIfChanged(ref _positionText, value);
    }

    public string DurationText
    {
        get => _durationText;
        set => this.RaiseAndSetIfChanged(ref _durationText, value);
    }

    public float Volume
    {
        get => _volume;
        set
        {
            this.RaiseAndSetIfChanged(ref _volume, value);
            _playback.Volume = value;
        }
    }

    public double CursorX
    {
        get => _cursorX;
        set => this.RaiseAndSetIfChanged(ref _cursorX, value);
    }

    public bool ShowCursor
    {
        get => _showCursor;
        set => this.RaiseAndSetIfChanged(ref _showCursor, value);
    }

    public double SpectrogramPixelWidth => _originalWidth;
    public double SpectrogramPixelHeight => _originalHeight;

    public int SampleRate => _sampleRate;
    public double AudioDuration => _audioDuration;
    public double Zoom => _zoom;

    /// <summary>
    /// Selection mode: 0 = Time (vertical strip), 1 = Frequency (horizontal strip)
    /// </summary>
    public int SelectionModeIndex
    {
        get => _selectionModeIndex;
        set => this.RaiseAndSetIfChanged(ref _selectionModeIndex, value);
    }

    // Selection rectangle (in zoomed image coordinates — recalculated on zoom)
    public double SelectionLeft
    {
        get => _selectionLeft;
        set => this.RaiseAndSetIfChanged(ref _selectionLeft, value);
    }

    public double SelectionTop
    {
        get => _selectionTop;
        set => this.RaiseAndSetIfChanged(ref _selectionTop, value);
    }

    public double SelectionWidth
    {
        get => _selectionWidth;
        set => this.RaiseAndSetIfChanged(ref _selectionWidth, value);
    }

    public double SelectionHeight
    {
        get => _selectionHeight;
        set => this.RaiseAndSetIfChanged(ref _selectionHeight, value);
    }

    public bool HasSelection
    {
        get => _hasSelection;
        set => this.RaiseAndSetIfChanged(ref _hasSelection, value);
    }

    public string SelectionInfo
    {
        get => _selectionInfo;
        set => this.RaiseAndSetIfChanged(ref _selectionInfo, value);
    }

    public string CursorTimeText
    {
        get => _cursorTimeText;
        set => this.RaiseAndSetIfChanged(ref _cursorTimeText, value);
    }

    public string CursorFreqText
    {
        get => _cursorFreqText;
        set => this.RaiseAndSetIfChanged(ref _cursorFreqText, value);
    }

    // Normalized selection coordinates (0..1, zoom-independent)
    private double _selNormLeft;
    private double _selNormTop;
    private double _selNormWidth;
    private double _selNormHeight;

    // Commands
    public ReactiveCommand<Unit, Unit> ZoomInCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomOutCommand { get; }
    public ReactiveCommand<Unit, Unit> FitToWindowCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetZoomCommand { get; }
    public ReactiveCommand<Unit, Unit> PrevCommand { get; }
    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleLoopCommand { get; }
    public ReactiveCommand<Unit, Unit> FindLoopPointsCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportSelectionCommand { get; }

    public Action? OnClose { get; set; }
    public Action<int>? OnNavigate { get; set; }

    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }

    private bool _isUpdatingFromTimer;
    private bool _isUserSeeking;

    public SpectrogramViewerViewModel(IAudioPlaybackService playback)
    {
        _playback = playback;

        var canPrev = this.WhenAnyValue(x => x.HasPrev);
        var canNext = this.WhenAnyValue(x => x.HasNext);

        ZoomInCommand = ReactiveCommand.Create(ZoomIn);
        ZoomOutCommand = ReactiveCommand.Create(ZoomOut);
        FitToWindowCommand = ReactiveCommand.Create(FitToWindow);
        ResetZoomCommand = ReactiveCommand.Create(ResetZoom);
        PrevCommand = ReactiveCommand.Create(() => OnNavigate?.Invoke(-1), canPrev);
        NextCommand = ReactiveCommand.Create(() => OnNavigate?.Invoke(1), canNext);
        CloseCommand = ReactiveCommand.Create(DoClose);
        PlayPauseCommand = ReactiveCommand.Create(TogglePlayPause);
        StopCommand = ReactiveCommand.Create(StopPlayback);
        ToggleLoopCommand = ReactiveCommand.Create(ToggleLoop);
        FindLoopPointsCommand = ReactiveCommand.CreateFromTask(FindLoopPointsAsync);
        ExportSelectionCommand = ReactiveCommand.CreateFromTask(ExportSelectionAsync);

        _onPlaybackStopped = () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsPlaying = false;
            });
        };
        _playback.PlaybackStopped += _onPlaybackStopped;

        _positionTimer = new System.Timers.Timer(33);
        _positionTimer.Elapsed += OnPositionTimerTick;
        _positionTimer.Start();
    }

    public async void LoadImage(string imagePath, string fileName, string audioFilePath)
    {
        ClearLoopState();
        _playback.Stop();
        IsPlaying = false;

        FileName = fileName;
        _audioFilePath = audioFilePath;

        try
        {
            // Dispose previous image to prevent memory leak on navigation
            _fullImage?.Dispose();
            FullImage = null;

            // Load full-resolution image on background thread to avoid UI freeze
            var bmp = await Task.Run(() => new Bitmap(imagePath));
            FullImage = bmp;
            _originalWidth = FullImage.PixelSize.Width;
            _originalHeight = FullImage.PixelSize.Height;
            _zoom = 1.0;
            ImageWidth = _originalWidth;
            ImageHeight = _originalHeight; // height stays fixed (no vertical zoom)
            UpdateZoomText();
        }
        catch (Exception ex)
        {
            FileName = $"Error loading image: {ex.Message}";
        }

        // Load audio
        if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
        {
            try
            {
                _playback.Load(audioFilePath);
                PlaybackDuration = _playback.Duration.TotalSeconds;
                _audioDuration = _playback.Duration.TotalSeconds;
                DurationText = FormatTimeMs(_playback.Duration);
                ShowCursor = true;

                // Get sample rate from the already-loaded playback service
                // instead of opening the file a second time
                _sampleRate = _playback.SampleRate;
            }
            catch (Exception)
            {
                PlaybackDuration = 0;
                _audioDuration = 0;
                DurationText = "00:00:00.000";
                ShowCursor = false;
            }
        }
        else
        {
            PlaybackDuration = 0;
            _audioDuration = 0;
            DurationText = "00:00:00.000";
            ShowCursor = false;
        }

        _isUpdatingFromTimer = true;
        PlaybackPosition = 0;
        _isUpdatingFromTimer = false;
        PositionText = "00:00:00.000";
        CursorX = 0;

        // Auto-fit to window on load if viewport is available
        // (also sets _fitZoom as the baseline minimum zoom)
        if (ViewportWidth > 0 && _originalWidth > 0)
        {
            _fitZoom = ViewportWidth / _originalWidth;
            SetZoom(_fitZoom);
        }
    }

    public void SeekToRelativePosition(double relativeX)
    {
        if (PlaybackDuration <= 0) return;
        relativeX = Math.Clamp(relativeX, 0, 1);
        var targetTime = TimeSpan.FromSeconds(relativeX * PlaybackDuration);
        _playback.Seek(targetTime);
        UpdatePositionDisplay();
    }

    private void TogglePlayPause()
    {
        if (PlaybackDuration <= 0) return;

        // If already playing, just pause/resume
        if (IsPlaying)
        {
            _playback.TogglePlayPause();
            IsPlaying = _playback.IsPlaying;
            return;
        }

        // If there's a selection, play the selection (range or bandpass)
        if (HasSelection)
        {
            PlaySelection();
            return;
        }

        // Normal play
        _playback.TogglePlayPause();
        IsPlaying = _playback.IsPlaying;
    }

    private void StopPlayback()
    {
        ClearLoopState();
        _playback.Stop();
        IsPlaying = false;
        UpdatePositionDisplay();
    }

    private void DoClose()
    {
        ClearLoopState();
        _playback.Stop();
        IsPlaying = false;
        OnClose?.Invoke();
    }

    /// <summary>Set to true while user is dragging the seekbar to suppress timer updates.</summary>
    public bool IsUserSeeking
    {
        get => _isUserSeeking;
        set => _isUserSeeking = value;
    }

    private void OnPositionTimerTick(object? sender, ElapsedEventArgs e)
    {
        if (!_playback.IsPlaying || _isUserSeeking) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(UpdatePositionDisplay);
    }

    private void UpdatePositionDisplay()
    {
        var pos = _playback.Position;

        _isUpdatingFromTimer = true;
        PlaybackPosition = pos.TotalSeconds;
        _isUpdatingFromTimer = false;

        PositionText = FormatTimeMs(pos);
        UpdateCursorFromPosition(pos.TotalSeconds);
    }

    private void UpdateCursorFromPosition(double seconds)
    {
        // Update cursor X position — only horizontal zoom applies
        if (PlaybackDuration > 0 && _originalWidth > 0)
        {
            double fraction = seconds / PlaybackDuration;
            CursorX = fraction * _originalWidth * _zoom;
            PositionText = FormatTimeMs(TimeSpan.FromSeconds(seconds));
        }
    }

    public void SetSelection(double left, double top, double width, double height)
    {
        SelectionLeft = left;
        SelectionTop = top;
        SelectionWidth = width;
        SelectionHeight = height;
        HasSelection = width > 2 || height > 2;

        // Store normalized (0..1) coordinates for zoom-independent tracking
        double spectroW = _originalWidth * _zoom;
        double spectroH = _originalHeight;
        _selNormLeft = spectroW > 0 ? left / spectroW : 0;
        _selNormTop = spectroH > 0 ? top / spectroH : 0;
        _selNormWidth = spectroW > 0 ? width / spectroW : 0;
        _selNormHeight = spectroH > 0 ? height / spectroH : 0;

        _selFreqLow = 0;
        _selFreqHigh = 0;

        if (HasSelection && PlaybackDuration > 0)
        {
            if (SelectionModeIndex == 0) // Time selection
            {
                double startFrac = Math.Clamp(_selNormLeft, 0, 1);
                double endFrac = Math.Clamp(_selNormLeft + _selNormWidth, 0, 1);
                double startTime = startFrac * PlaybackDuration;
                double endTime = endFrac * PlaybackDuration;
                SelectionInfo = $"{FormatTimeMs(TimeSpan.FromSeconds(startTime))} - {FormatTimeMs(TimeSpan.FromSeconds(endTime))}";
            }
            else if (SelectionModeIndex == 1) // Frequency selection
            {
                double hm1 = Math.Max(1, spectroH - 1);
                double topNorm = Math.Clamp(1.0 - (top / hm1), 0, 1);
                double bottomNorm = Math.Clamp(1.0 - ((top + height) / hm1), 0, 1);
                _selFreqHigh = MelScale.NormalizedYToFreq(topNorm, _sampleRate);
                _selFreqLow = MelScale.NormalizedYToFreq(bottomNorm, _sampleRate);
                SelectionInfo = $"{FormatFreq(_selFreqLow)} - {FormatFreq(_selFreqHigh)}";
            }
            else
            {
                SelectionInfo = "";
            }
        }
        else
        {
            SelectionInfo = "";
        }
    }

    public void ClearSelection()
    {
        HasSelection = false;
        SelectionLeft = 0;
        SelectionTop = 0;
        SelectionWidth = 0;
        SelectionHeight = 0;
        SelectionInfo = "";
    }

    /// <summary>
    /// Update cursor info text from mouse position over the spectrogram (in zoomed image coordinates).
    /// </summary>
    public void UpdateCursorInfo(double imageX, double imageY)
    {
        // Time from X position
        if (_originalWidth > 0 && _audioDuration > 0)
        {
            double timeFraction = imageX / (_originalWidth * _zoom);
            timeFraction = Math.Clamp(timeFraction, 0, 1);
            double timeSeconds = timeFraction * _audioDuration;
            CursorTimeText = FormatTimeMs(TimeSpan.FromSeconds(timeSeconds));
        }
        else
        {
            CursorTimeText = "";
        }

        // Frequency from Y position (mel-scale, matching renderer)
        if (_originalHeight > 0 && _sampleRate > 0)
        {
            double normalizedY = 1.0 - (imageY / Math.Max(1, _originalHeight));
            normalizedY = Math.Clamp(normalizedY, 0, 1);
            double freq = MelScale.NormalizedYToFreq(normalizedY, _sampleRate);
            CursorFreqText = FormatFreq(freq);
        }
        else
        {
            CursorFreqText = "";
        }
    }

    /// <summary>
    /// Clear cursor info when mouse leaves the spectrogram.
    /// </summary>
    public void ClearCursorInfo()
    {
        CursorTimeText = "";
        CursorFreqText = "";
    }

    // Cached frequency range from the last freq selection
    private double _selFreqLow;
    private double _selFreqHigh;

    // Loop state
    private bool _isLooping;
    private double _loopStartX;
    private double _loopEndX;
    private string _loopInfo = "";
    private bool _showLoopMarkers;
    private bool _isExporting;
    private TimeSpan _loopStartTime;
    private TimeSpan _loopEndTime;

    public bool IsLooping
    {
        get => _isLooping;
        set => this.RaiseAndSetIfChanged(ref _isLooping, value);
    }

    public double LoopStartX
    {
        get => _loopStartX;
        set => this.RaiseAndSetIfChanged(ref _loopStartX, value);
    }

    public double LoopEndX
    {
        get => _loopEndX;
        set => this.RaiseAndSetIfChanged(ref _loopEndX, value);
    }

    public string LoopInfo
    {
        get => _loopInfo;
        set => this.RaiseAndSetIfChanged(ref _loopInfo, value);
    }

    public bool ShowLoopMarkers
    {
        get => _showLoopMarkers;
        set => this.RaiseAndSetIfChanged(ref _showLoopMarkers, value);
    }

    public bool IsExporting
    {
        get => _isExporting;
        set => this.RaiseAndSetIfChanged(ref _isExporting, value);
    }

    public bool IsSearchingLoops
    {
        get => _isSearchingLoops;
        set => this.RaiseAndSetIfChanged(ref _isSearchingLoops, value);
    }

    /// <summary>Delegate set by the View for save-file dialog.</summary>
    public Func<string?, Task<string?>>? ExportFilePicker { get; set; }

    public void PlaySelection()
    {
        if (!HasSelection || PlaybackDuration <= 0) return;

        if (SelectionModeIndex == 0)
        {
            // Time selection: play only the selected time range (using normalized coords)
            double startFrac = Math.Clamp(_selNormLeft, 0, 1);
            double endFrac = Math.Clamp(_selNormLeft + _selNormWidth, 0, 1);

            var startTime = TimeSpan.FromSeconds(startFrac * PlaybackDuration);
            var endTime = TimeSpan.FromSeconds(endFrac * PlaybackDuration);

            _playback.PlayRange(startTime, endTime);
        }
        else if (SelectionModeIndex == 1 && _selFreqLow > 0 && _selFreqHigh > _selFreqLow)
        {
            // Frequency selection: play entire track with bandpass filter
            _playback.PlayWithBandpass((float)_selFreqLow, (float)_selFreqHigh);
        }
        else
        {
            return;
        }
        IsPlaying = true;
    }

    // ---- Loop ----

    private void ToggleLoop()
    {
        if (IsLooping)
        {
            // Stop loop
            _playback.StopLoop();
            IsLooping = false;
            ShowLoopMarkers = false;
            LoopInfo = "";
            return;
        }

        // Start loop: need time selection
        if (!HasSelection || SelectionModeIndex != 0 || PlaybackDuration <= 0) return;

        double startFrac = Math.Clamp(_selNormLeft, 0, 1);
        double endFrac = Math.Clamp(_selNormLeft + _selNormWidth, 0, 1);
        _loopStartTime = TimeSpan.FromSeconds(startFrac * PlaybackDuration);
        _loopEndTime = TimeSpan.FromSeconds(endFrac * PlaybackDuration);

        if (_loopEndTime <= _loopStartTime) return;

        _playback.PlayLoop(_loopStartTime, _loopEndTime);
        IsLooping = true;
        IsPlaying = true;
        UpdateLoopMarkers();
        LoopInfo = $"Loop: {FormatTimeMs(_loopStartTime)} - {FormatTimeMs(_loopEndTime)}";
    }

    private void UpdateLoopMarkers()
    {
        if (PlaybackDuration <= 0 || _originalWidth <= 0) return;

        double startFrac = _loopStartTime.TotalSeconds / PlaybackDuration;
        double endFrac = _loopEndTime.TotalSeconds / PlaybackDuration;
        LoopStartX = startFrac * _originalWidth * _zoom;
        LoopEndX = endFrac * _originalWidth * _zoom;
        ShowLoopMarkers = true;
    }

    private void ClearLoopState()
    {
        // Cancel any ongoing loop search
        _loopSearchCts?.Cancel();
        _loopSearchCts?.Dispose();
        _loopSearchCts = null;
        IsSearchingLoops = false;

        if (IsLooping)
        {
            _playback.StopLoop();
        }
        IsLooping = false;
        ShowLoopMarkers = false;
        LoopInfo = "";
        LoopStartX = 0;
        LoopEndX = 0;
    }

    private async Task FindLoopPointsAsync()
    {
        if (string.IsNullOrEmpty(_audioFilePath) || !File.Exists(_audioFilePath)) return;
        if (PlaybackDuration <= 0) return;
        if (IsSearchingLoops) return; // already searching

        // Cancel any previous search
        _loopSearchCts?.Cancel();
        _loopSearchCts?.Dispose();
        _loopSearchCts = new CancellationTokenSource();
        var ct = _loopSearchCts.Token;

        IsSearchingLoops = true;
        LoopInfo = "Searching for loop points...";

        try
        {
            var audioData = await Task.Run(() => AudioDecoder.Decode(_audioFilePath), ct);

            List<LoopPoint> loopPoints;
            if (HasSelection && SelectionModeIndex == 0)
            {
                // Find loop points within current time selection
                double startFrac = Math.Clamp(_selNormLeft, 0, 1);
                double endFrac = Math.Clamp(_selNormLeft + _selNormWidth, 0, 1);
                double selStartSec = startFrac * PlaybackDuration;
                double selEndSec = endFrac * PlaybackDuration;
                loopPoints = await Task.Run(() =>
                    LoopFinder.FindLoopPoints(audioData.Samples, audioData.SampleRate, selStartSec, selEndSec, ct: ct), ct);
            }
            else
            {
                // Auto-detect loops across entire track
                loopPoints = await Task.Run(() =>
                    LoopFinder.AutoDetect(audioData.Samples, audioData.SampleRate, ct: ct), ct);
            }

            if (loopPoints.Count == 0)
            {
                LoopInfo = "No loop points found";
                return;
            }

            // Apply best loop point as selection
            var best = loopPoints[0];
            double startNorm = best.Start.TotalSeconds / PlaybackDuration;
            double endNorm = best.End.TotalSeconds / PlaybackDuration;
            double spectroW = _originalWidth * _zoom;

            SetSelection(startNorm * spectroW, 0, (endNorm - startNorm) * spectroW, _originalHeight);
            SelectionInfo = $"{FormatTimeMs(best.Start)} - {FormatTimeMs(best.End)} (match: {best.MatchScore:P0})";
            LoopInfo = $"Found loop ({best.MatchScore:P0} match)";
        }
        catch (OperationCanceledException)
        {
            LoopInfo = "Loop search cancelled";
        }
        catch (Exception ex)
        {
            LoopInfo = $"Loop search failed: {ex.Message}";
        }
        finally
        {
            IsSearchingLoops = false;
        }
    }

    private async Task ExportSelectionAsync()
    {
        if (!HasSelection || SelectionModeIndex != 0 || PlaybackDuration <= 0) return;
        if (string.IsNullOrEmpty(_audioFilePath) || ExportFilePicker == null) return;

        double startFrac = Math.Clamp(_selNormLeft, 0, 1);
        double endFrac = Math.Clamp(_selNormLeft + _selNormWidth, 0, 1);
        var startTime = TimeSpan.FromSeconds(startFrac * PlaybackDuration);
        var endTime = TimeSpan.FromSeconds(endFrac * PlaybackDuration);

        var suggestedName = AudioExporter.SuggestFileName(_audioFilePath, startTime, endTime);
        var outputPath = await ExportFilePicker(suggestedName);
        if (string.IsNullOrEmpty(outputPath)) return;

        IsExporting = true;
        try
        {
            int crossfadeMs = IsLooping ? 20 : 0;
            await Task.Run(() => AudioExporter.ExportRange(_audioFilePath, outputPath, startTime, endTime, crossfadeMs));
        }
        catch (Exception ex)
        {
            LoopInfo = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    // Zoom only horizontal — height stays fixed
    public void ZoomIn()
    {
        SetZoom(_zoom * 1.2);
    }

    public void ZoomOut()
    {
        SetZoom(_zoom / 1.2);
    }

    public void SetZoom(double newZoom)
    {
        // Minimum zoom = fit zoom (can't zoom out past "fit in window")
        double minZoom = _fitZoom > 0 ? _fitZoom : 0.01;
        _zoom = Math.Clamp(newZoom, minZoom, 50.0);
        ImageWidth = _originalWidth * _zoom;
        // ImageHeight stays at _originalHeight — no vertical zoom
        UpdateZoomText();
        UpdatePositionDisplay();
        RecalcSelectionFromNormalized();
        if (ShowLoopMarkers) UpdateLoopMarkers();
    }

    /// <summary>
    /// Recalculate pixel-space selection coordinates from stored normalized values after zoom change.
    /// </summary>
    private void RecalcSelectionFromNormalized()
    {
        if (!HasSelection) return;

        double spectroW = _originalWidth * _zoom;
        double spectroH = _originalHeight;

        SelectionLeft = _selNormLeft * spectroW;
        SelectionTop = _selNormTop * spectroH;
        SelectionWidth = _selNormWidth * spectroW;
        SelectionHeight = _selNormHeight * spectroH;
    }

    private void ResetZoom()
    {
        SetZoom(1.0);
    }

    /// <summary>
    /// Recalculates fit zoom and applies it. Called on viewport resize or when image loads.
    /// </summary>
    public void FitToWindow()
    {
        if (_originalWidth <= 0 || _originalHeight <= 0) return;
        if (ViewportWidth <= 0) return;

        // Calculate and store fit zoom level
        _fitZoom = ViewportWidth / _originalWidth;
        SetZoom(_fitZoom);
    }

    /// <summary>
    /// Recalculates fit zoom minimum without changing current zoom.
    /// Called on viewport resize when user has already zoomed in.
    /// </summary>
    public void UpdateFitZoom()
    {
        if (_originalWidth <= 0 || ViewportWidth <= 0) return;
        _fitZoom = ViewportWidth / _originalWidth;
        // If current zoom is now below minimum, clamp up
        if (_zoom < _fitZoom)
        {
            SetZoom(_fitZoom);
        }
    }

    private void UpdateZoomText()
    {
        // Show zoom relative to fit: fit=100%, 1:1=originalWidth/fitZoom*100
        double relativeZoom = _fitZoom > 0 ? (_zoom / _fitZoom) * 100 : _zoom * 100;
        ZoomText = $"{relativeZoom:F0}%";
    }

    /// <summary>
    /// Format time as 00:00:00.000 (h:mm:ss.fff)
    /// </summary>
    private static string FormatTimeMs(TimeSpan t)
    {
        int h = (int)t.TotalHours;
        int m = t.Minutes;
        int s = t.Seconds;
        int ms = t.Milliseconds;
        return $"{h:D2}:{m:D2}:{s:D2}.{ms:D3}";
    }

    private static string FormatFreq(double hz)
    {
        if (hz >= 1000)
            return $"{hz / 1000:F1}k";
        return $"{hz:F0} Hz";
    }

    public void Dispose()
    {
        _loopSearchCts?.Cancel();
        _loopSearchCts?.Dispose();
        _positionTimer?.Stop();
        _positionTimer?.Dispose();
        _playback.PlaybackStopped -= _onPlaybackStopped;
        _playback.Dispose();
        _fullImage?.Dispose();
        _fullImage = null;
    }
}
