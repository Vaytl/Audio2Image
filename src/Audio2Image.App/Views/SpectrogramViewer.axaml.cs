using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Audio2Image.App.ViewModels;
using Audio2Image.Core.Dsp;
using ReactiveUI;

namespace Audio2Image.App.Views;

public partial class SpectrogramViewer : UserControl
{
    private bool _isPanning;
    private Point _panStart;
    private Vector _scrollStart;
    private IDisposable? _cursorSubscription;
    private IDisposable? _selectionSubscription;
    private IDisposable? _zoomSubscription;
    private IDisposable? _loopSubscription;

    // Selection drag state
    private bool _isSelecting;
    private Point _selectionStart;

    // Cached cursor elements (reused instead of Clear+Create at 30fps)
    private Line? _cursorLine;
    private Ellipse? _cursorMarker;
    private Avalonia.Controls.Shapes.Path? _cursorTriangle;
    private bool _cursorElementsCreated;

    // Cached selection elements (reused on drag)
    private Rectangle? _selFill;
    private Line? _selLine1;
    private Line? _selLine2;
    private bool _selElementsCreated;

    // Cached loop marker elements
    private Line? _loopLine1;
    private Line? _loopLine2;
    private TextBlock? _loopLabel1;
    private TextBlock? _loopLabel2;
    private bool _loopElementsCreated;

    // Frequency labels for log scale
    private static readonly (double hz, string label)[] FreqLabels =
    {
        (100, "100"), (300, "300"), (500, "500"),
        (1000, "1k"), (1500, "1.5k"), (2000, "2k"), (3000, "3k"),
        (5000, "5k"), (7000, "7k"), (10000, "10k"), (15000, "15k"),
        (20000, "20k"),
    };

    // Use MelScale.MinFreqHz / MelScale.MaxFreqHz from Core

    // Cached brushes
    private static readonly SolidColorBrush TickBrush = new(Color.FromRgb(100, 100, 100));
    private static readonly SolidColorBrush TextBrush = new(Color.FromRgb(180, 180, 180));
    private static readonly SolidColorBrush SelectionFillBrush = new(Color.FromArgb(0x40, 0x40, 0x80, 0xFF));
    private static readonly SolidColorBrush SelectionBorderBrush = new(Color.FromArgb(0xCC, 0xFF, 0xC8, 0x00));
    private static readonly SolidColorBrush CursorBrush = new(Color.FromArgb(0xDC, 0xFF, 0xC8, 0x00));
    private static readonly SolidColorBrush LoopMarkerBrush = new(Color.FromArgb(0xCC, 0x44, 0xCC, 0x44));
    private static readonly AvaloniaList<double> DashPattern = new() { 6, 4 };

    public SpectrogramViewer()
    {
        InitializeComponent();

        // Wire up selection mode toggle buttons
        TimeSelBtn.IsCheckedChanged += OnSelectionModeChanged;
        FreqSelBtn.IsCheckedChanged += OnSelectionModeChanged;
    }

    private void OnSelectionModeChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SpectrogramViewerViewModel vm) return;

        if (sender == TimeSelBtn && TimeSelBtn.IsChecked == true)
        {
            FreqSelBtn.IsChecked = false;
            vm.SelectionModeIndex = 0;
        }
        else if (sender == FreqSelBtn && FreqSelBtn.IsChecked == true)
        {
            TimeSelBtn.IsChecked = false;
            vm.SelectionModeIndex = 1;
        }
        else
        {
            // If unchecked, default back to time
            if (TimeSelBtn.IsChecked != true && FreqSelBtn.IsChecked != true)
            {
                TimeSelBtn.IsChecked = true;
                vm.SelectionModeIndex = 0;
            }
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateViewportSize();
        SubscribeToOverlays();
        ImageScroller.ScrollChanged += OnScrollChanged;

        // Suppress timer updates while user drags seekbar
        SeekBar.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnSeekBarPressed, handledEventsToo: true);
        SeekBar.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, OnSeekBarReleased, handledEventsToo: true);

        Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // Re-subscribe when VM changes (SpectrogramViewer is created once,
        // but ViewerVm is assigned later when user opens a spectrogram)
        SubscribeToOverlays();
        UpdateViewportSize();
        DrawFrequencyScale();
        DrawRightFrequencyScale();
        DrawTimeScale();

        // Clear old overlays and reset cached elements
        SelectionCanvas.Children.Clear();
        CursorCanvas.Children.Clear();
        LoopMarkerCanvas.Children.Clear();
        _cursorElementsCreated = false;
        _selElementsCreated = false;
        _loopElementsCreated = false;

        // Wire export file picker delegate
        if (DataContext is SpectrogramViewerViewModel vmExport)
        {
            vmExport.ExportFilePicker = ExportFilePickerAsync;
        }

        // Deferred FitToWindow: viewport size may not be available yet,
        // schedule after layout pass completes
        if (DataContext is SpectrogramViewerViewModel vm2)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                UpdateViewportSize();
                if (vm2.ViewportWidth > 0 && vm2.ImageWidth > 0)
                {
                    vm2.FitToWindowCommand.Execute().Subscribe();
                }
                DrawFrequencyScale();
                DrawRightFrequencyScale();
                DrawTimeScale();
                DrawCursor();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _cursorSubscription?.Dispose();
        _cursorSubscription = null;
        _selectionSubscription?.Dispose();
        _selectionSubscription = null;
        _zoomSubscription?.Dispose();
        _zoomSubscription = null;
        _loopSubscription?.Dispose();
        _loopSubscription = null;
        ImageScroller.ScrollChanged -= OnScrollChanged;
        SeekBar.RemoveHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnSeekBarPressed);
        SeekBar.RemoveHandler(Avalonia.Input.InputElement.PointerReleasedEvent, OnSeekBarReleased);
    }

    private void OnSeekBarPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is SpectrogramViewerViewModel vm)
            vm.IsUserSeeking = true;
    }

    private void OnSeekBarReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (DataContext is SpectrogramViewerViewModel vm)
            vm.IsUserSeeking = false;
    }

    private void SubscribeToOverlays()
    {
        _cursorSubscription?.Dispose();
        _selectionSubscription?.Dispose();
        _zoomSubscription?.Dispose();
        _loopSubscription?.Dispose();

        if (DataContext is SpectrogramViewerViewModel vm)
        {
            _cursorSubscription = vm.WhenAnyValue(x => x.CursorX)
                .Subscribe(_ => DrawCursor());

            _selectionSubscription = vm.WhenAnyValue(
                    x => x.SelectionLeft, x => x.SelectionTop,
                    x => x.SelectionWidth, x => x.SelectionHeight)
                .Subscribe(_ => DrawSelection());

            // Zoom or image size changes -> redraw scales
            _zoomSubscription = vm.WhenAnyValue(x => x.ImageWidth, x => x.ImageHeight)
                .Subscribe(_ =>
                {
                    DrawFrequencyScale();
                    DrawRightFrequencyScale();
                    DrawTimeScale();
                });

            // Loop markers
            _loopSubscription = vm.WhenAnyValue(x => x.LoopStartX, x => x.LoopEndX, x => x.ShowLoopMarkers)
                .Subscribe(_ => DrawLoopMarkers());
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        DrawFrequencyScale();
        DrawRightFrequencyScale();
        DrawTimeScale();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateViewportSize();
        // Update minimum zoom level when window resizes
        if (DataContext is SpectrogramViewerViewModel vm)
        {
            vm.UpdateFitZoom();
        }
        DrawFrequencyScale();
        DrawRightFrequencyScale();
        DrawTimeScale();
    }

    // ---- Drawing programmatic scales ----

    /// <summary>
    /// Frequency scale: uses TranslatePoint to find the exact pixel positions
    /// of the top and bottom of the spectrogram image relative to the canvas.
    /// This guarantees perfect alignment regardless of scrolling or layout.
    /// </summary>
    private void DrawFrequencyScale() => DrawFrequencyScaleImpl(FreqScaleCanvas, isLeft: true);
    private void DrawRightFrequencyScale() => DrawFrequencyScaleImpl(RightFreqScaleCanvas, isLeft: false);

    /// <summary>
    /// Unified frequency scale drawing for left and right panels.
    /// Uses MelScale from Core for all mel calculations.
    /// </summary>
    private void DrawFrequencyScaleImpl(Canvas canvas, bool isLeft)
    {
        if (DataContext is not SpectrogramViewerViewModel vm) return;
        canvas.Children.Clear();

        double canvasHeight = canvas.Bounds.Height;
        if (canvasHeight <= 0) return;

        double sampleRate = vm.SampleRate;
        double maxFreq = Math.Min(sampleRate / 2.0, MelScale.MaxFreqHz);
        if (maxFreq <= MelScale.MinFreqHz) return;

        double imgH = SpectrogramImage.Bounds.Height;
        if (imgH <= 0) imgH = vm.ImageHeight;
        if (imgH <= 0) return;

        var imgTopInCanvas = SpectrogramImage.TranslatePoint(new Point(0, 0), canvas);
        var imgBotInCanvas = SpectrogramImage.TranslatePoint(new Point(0, imgH), canvas);

        double imageTopY, imageBotY;
        if (imgTopInCanvas.HasValue && imgBotInCanvas.HasValue)
        {
            imageTopY = imgTopInCanvas.Value.Y;
            imageBotY = imgBotInCanvas.Value.Y;
        }
        else
        {
            imageTopY = -ImageScroller.Offset.Y;
            imageBotY = imgH - ImageScroller.Offset.Y;
        }

        double visibleImgH = imageBotY - imageTopY;
        if (visibleImgH <= 0) return;

        foreach (var (hz, label) in FreqLabels)
        {
            if (hz > maxFreq || hz < MelScale.MinFreqHz) continue;

            double normalizedY = MelScale.FreqToNormalizedY(hz, sampleRate);
            double pixelFraction = 1.0 - normalizedY;
            double yPos = imageTopY + pixelFraction * visibleImgH;

            if (yPos < -12 || yPos > canvasHeight + 12) continue;

            if (isLeft)
            {
                canvas.Children.Add(new Line
                {
                    StartPoint = new Point(42, yPos),
                    EndPoint = new Point(55, yPos),
                    Stroke = TickBrush,
                    StrokeThickness = 0.5
                });
                var text = new TextBlock
                {
                    Text = $"{label} \u2014",
                    FontSize = 10, Foreground = TextBrush,
                    TextAlignment = TextAlignment.Right, Width = 52
                };
                Canvas.SetLeft(text, 0);
                Canvas.SetTop(text, yPos - 8);
                canvas.Children.Add(text);
            }
            else
            {
                canvas.Children.Add(new Line
                {
                    StartPoint = new Point(0, yPos),
                    EndPoint = new Point(13, yPos),
                    Stroke = TickBrush,
                    StrokeThickness = 0.5
                });
                var text = new TextBlock
                {
                    Text = $"\u2014 {label}",
                    FontSize = 10, Foreground = TextBrush,
                    TextAlignment = TextAlignment.Left, Width = 52
                };
                Canvas.SetLeft(text, 3);
                Canvas.SetTop(text, yPos - 8);
                canvas.Children.Add(text);
            }
        }
    }

    /// <summary>
    /// Time scale: synced with horizontal scroll and zoom.
    /// </summary>
    private void DrawTimeScale()
    {
        if (DataContext is not SpectrogramViewerViewModel vm) return;
        TimeScaleCanvas.Children.Clear();

        double duration = vm.AudioDuration;
        if (duration <= 0) return;

        double imageWidth = vm.ImageWidth; // zoomed width
        double canvasWidth = TimeScaleCanvas.Bounds.Width;
        if (canvasWidth <= 0 || imageWidth <= 0) return;

        double offsetX = ImageScroller.Offset.X;

        // Visible time range
        double startFraction = offsetX / imageWidth;
        double endFraction = (offsetX + canvasWidth) / imageWidth;
        double visibleDuration = (endFraction - startFraction) * duration;

        double interval = ChooseTimeInterval(visibleDuration, canvasWidth);

        double startTime = Math.Floor(startFraction * duration / interval) * interval;
        if (startTime < 0) startTime = 0;

        for (double t = startTime; t <= duration + 0.001; t += interval)
        {
            double fraction = t / duration;
            double xInImage = fraction * imageWidth;
            double xPos = xInImage - offsetX;

            if (xPos < -40 || xPos > canvasWidth + 40) continue;

            var tick = new Line
            {
                StartPoint = new Point(xPos, 0),
                EndPoint = new Point(xPos, 5),
                Stroke = TickBrush,
                StrokeThickness = 1
            };
            TimeScaleCanvas.Children.Add(tick);

            string timeLabel = FormatTimeScaleLabel(t);
            var text = new TextBlock
            {
                Text = timeLabel,
                FontSize = 10,
                Foreground = TextBrush
            };
            Canvas.SetLeft(text, xPos - 20);
            Canvas.SetTop(text, 7);
            TimeScaleCanvas.Children.Add(text);
        }
    }

    /// <summary>
    /// Draw RX-style selection: blue semi-transparent fill + yellow dashed border edges.
    /// </summary>
    private void DrawSelection()
    {
        if (DataContext is not SpectrogramViewerViewModel vm) return;

        if (!vm.HasSelection)
        {
            if (_selElementsCreated)
            {
                _selFill!.IsVisible = false;
                _selLine1!.IsVisible = false;
                _selLine2!.IsVisible = false;
            }
            return;
        }

        double left = vm.SelectionLeft;
        double top = vm.SelectionTop;
        double width = vm.SelectionWidth;
        double height = vm.SelectionHeight;

        if (!_selElementsCreated)
        {
            _selFill = new Rectangle { Fill = SelectionFillBrush };
            _selLine1 = new Line { Stroke = SelectionBorderBrush, StrokeThickness = 1.5, StrokeDashArray = DashPattern };
            _selLine2 = new Line { Stroke = SelectionBorderBrush, StrokeThickness = 1.5, StrokeDashArray = DashPattern };
            SelectionCanvas.Children.Add(_selFill);
            SelectionCanvas.Children.Add(_selLine1);
            SelectionCanvas.Children.Add(_selLine2);
            _selElementsCreated = true;
        }

        _selFill!.Width = Math.Max(0, width);
        _selFill.Height = Math.Max(0, height);
        Canvas.SetLeft(_selFill, left);
        Canvas.SetTop(_selFill, top);
        _selFill.IsVisible = true;

        bool isTimeSelection = (Math.Abs(height - vm.ImageHeight) < 2);
        bool isFreqSelection = (Math.Abs(width - vm.ImageWidth) < 2);

        if (isTimeSelection)
        {
            _selLine1!.StartPoint = new Point(left, 0);
            _selLine1.EndPoint = new Point(left, vm.ImageHeight);
            _selLine2!.StartPoint = new Point(left + width, 0);
            _selLine2.EndPoint = new Point(left + width, vm.ImageHeight);
            _selLine1.IsVisible = true;
            _selLine2.IsVisible = true;
        }
        else if (isFreqSelection)
        {
            _selLine1!.StartPoint = new Point(0, top);
            _selLine1.EndPoint = new Point(vm.ImageWidth, top);
            _selLine2!.StartPoint = new Point(0, top + height);
            _selLine2.EndPoint = new Point(vm.ImageWidth, top + height);
            _selLine1.IsVisible = true;
            _selLine2.IsVisible = true;
        }
        else
        {
            _selLine1!.IsVisible = false;
            _selLine2!.IsVisible = false;
        }
    }

    /// <summary>
    /// Draw yellow playback cursor: vertical dashed line + teardrop at top.
    /// Everything is on CursorCanvas (inside ScrollViewer, in image coordinates).
    /// CursorCanvas has ClipToBounds="False" so the teardrop can extend above y=0.
    /// </summary>
    private void DrawCursor()
    {
        if (DataContext is not SpectrogramViewerViewModel vm) return;

        if (!vm.ShowCursor || vm.CursorX <= 0)
        {
            if (_cursorElementsCreated)
            {
                _cursorLine!.IsVisible = false;
                _cursorMarker!.IsVisible = false;
                _cursorTriangle!.IsVisible = false;
            }
            return;
        }

        double x = vm.CursorX;
        double h = vm.ImageHeight;

        if (!_cursorElementsCreated)
        {
            _cursorLine = new Line { Stroke = CursorBrush, StrokeThickness = 1.5, StrokeDashArray = DashPattern };
            _cursorMarker = new Ellipse { Width = 10, Height = 10, Fill = CursorBrush };
            _cursorTriangle = new Avalonia.Controls.Shapes.Path { Fill = CursorBrush };
            CursorCanvas.Children.Add(_cursorLine);
            CursorCanvas.Children.Add(_cursorMarker);
            CursorCanvas.Children.Add(_cursorTriangle);
            _cursorElementsCreated = true;
        }

        // Update positions (no allocation)
        _cursorLine!.StartPoint = new Point(x, 0);
        _cursorLine.EndPoint = new Point(x, h);
        _cursorLine.IsVisible = true;

        Canvas.SetLeft(_cursorMarker!, x - 5);
        Canvas.SetTop(_cursorMarker!, -14);
        _cursorMarker!.IsVisible = true;

        var xStr = x.ToString(CultureInfo.InvariantCulture);
        var xL = (x - 5).ToString(CultureInfo.InvariantCulture);
        var xR = (x + 5).ToString(CultureInfo.InvariantCulture);
        _cursorTriangle!.Data = StreamGeometry.Parse($"M {xL},-6 L {xStr},2 L {xR},-6 Z");
        _cursorTriangle.IsVisible = true;
    }

    /// <summary>
    /// Draw green dashed vertical lines at loop start and end positions.
    /// </summary>
    private void DrawLoopMarkers()
    {
        if (DataContext is not SpectrogramViewerViewModel vm) return;

        if (!vm.ShowLoopMarkers)
        {
            if (_loopElementsCreated)
            {
                _loopLine1!.IsVisible = false;
                _loopLine2!.IsVisible = false;
                _loopLabel1!.IsVisible = false;
                _loopLabel2!.IsVisible = false;
            }
            return;
        }

        double h = vm.ImageHeight;

        if (!_loopElementsCreated)
        {
            _loopLine1 = new Line { Stroke = LoopMarkerBrush, StrokeThickness = 2, StrokeDashArray = DashPattern };
            _loopLine2 = new Line { Stroke = LoopMarkerBrush, StrokeThickness = 2, StrokeDashArray = DashPattern };
            _loopLabel1 = new TextBlock { Text = "L", FontSize = 10, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = LoopMarkerBrush };
            _loopLabel2 = new TextBlock { Text = "L", FontSize = 10, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = LoopMarkerBrush };
            LoopMarkerCanvas.Children.Add(_loopLine1);
            LoopMarkerCanvas.Children.Add(_loopLine2);
            LoopMarkerCanvas.Children.Add(_loopLabel1);
            LoopMarkerCanvas.Children.Add(_loopLabel2);
            _loopElementsCreated = true;
        }

        _loopLine1!.StartPoint = new Point(vm.LoopStartX, 0);
        _loopLine1.EndPoint = new Point(vm.LoopStartX, h);
        _loopLine1.IsVisible = true;

        _loopLine2!.StartPoint = new Point(vm.LoopEndX, 0);
        _loopLine2.EndPoint = new Point(vm.LoopEndX, h);
        _loopLine2.IsVisible = true;

        Canvas.SetLeft(_loopLabel1!, vm.LoopStartX + 3);
        Canvas.SetTop(_loopLabel1!, 2);
        _loopLabel1!.IsVisible = true;

        Canvas.SetLeft(_loopLabel2!, vm.LoopEndX + 3);
        Canvas.SetTop(_loopLabel2!, 2);
        _loopLabel2!.IsVisible = true;
    }

    // ---- Input handling ----

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        // Scroll wheel = horizontal ZOOM (no modifier needed)
        // Ctrl+Scroll = pass through for normal scroll
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is SpectrogramViewerViewModel vm)
            {
                var posInScroller = e.GetPosition(ImageScroller);
                double oldZoom = vm.Zoom;

                if (e.Delta.Y > 0)
                    vm.ZoomIn();
                else
                    vm.ZoomOut();

                double newZoom = vm.Zoom;
                double zoomRatio = newZoom / oldZoom;

                // Anchor zoom horizontally only
                double newOffsetX = (ImageScroller.Offset.X + posInScroller.X) * zoomRatio - posInScroller.X;

                ImageScroller.Offset = new Vector(
                    Math.Max(0, newOffsetX),
                    ImageScroller.Offset.Y); // keep vertical scroll unchanged

                e.Handled = true;
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(ImageScroller);

        if (point.Properties.IsMiddleButtonPressed ||
            (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            // Pan mode
            _isPanning = true;
            _panStart = e.GetPosition(ImageScroller);
            _scrollStart = new Vector(ImageScroller.Offset.X, ImageScroller.Offset.Y);
            e.Pointer.Capture(ImageScroller);
            Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            if (DataContext is SpectrogramViewerViewModel vm)
            {
                _isSelecting = true;
                _selectionStart = e.GetPosition(SpectrogramImage);
                vm.ClearSelection();
                e.Pointer.Capture(SpectrogramImage);
                e.Handled = true;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isPanning)
        {
            var current = e.GetPosition(ImageScroller);
            var delta = _panStart - current;
            ImageScroller.Offset = new Vector(
                _scrollStart.X + delta.X,
                _scrollStart.Y + delta.Y);
            e.Handled = true;
        }
        else if (_isSelecting && DataContext is SpectrogramViewerViewModel vm)
        {
            var current = e.GetPosition(SpectrogramImage);

            // Selection mode determined by toolbar toggle (not by drag direction)
            if (vm.SelectionModeIndex == 0)
            {
                // Time selection: vertical strip across FULL height
                double left = Math.Min(_selectionStart.X, current.X);
                double width = Math.Abs(current.X - _selectionStart.X);
                left = Math.Max(0, left);
                if (left + width > vm.ImageWidth) width = vm.ImageWidth - left;

                vm.SetSelection(left, 0, width, vm.ImageHeight);
            }
            else
            {
                // Frequency selection: horizontal strip across FULL width
                double top = Math.Min(_selectionStart.Y, current.Y);
                double height = Math.Abs(current.Y - _selectionStart.Y);
                top = Math.Max(0, top);
                if (top + height > vm.ImageHeight) height = vm.ImageHeight - top;

                vm.SetSelection(0, top, vm.ImageWidth, height);
            }
            e.Handled = true;
        }

        // Track mouse position over spectrogram for cursor info
        if (!_isPanning && !_isSelecting && DataContext is SpectrogramViewerViewModel vm2)
        {
            var posInImage = e.GetPosition(SpectrogramImage);
            if (posInImage.X >= 0 && posInImage.Y >= 0 && posInImage.X <= vm2.ImageWidth && posInImage.Y <= vm2.ImageHeight)
            {
                vm2.UpdateCursorInfo(posInImage.X, posInImage.Y);
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            Cursor = Cursor.Default;
            e.Handled = true;
        }
        else if (_isSelecting)
        {
            _isSelecting = false;
            e.Pointer.Capture(null);
            Cursor = Cursor.Default;

            if (DataContext is SpectrogramViewerViewModel vm)
            {
                // If tiny drag (< 3px), treat as click-to-seek
                double dx = Math.Abs(e.GetPosition(SpectrogramImage).X - _selectionStart.X);
                double dy = Math.Abs(e.GetPosition(SpectrogramImage).Y - _selectionStart.Y);

                if (dx < 3 && dy < 3)
                {
                    vm.ClearSelection();
                    var posInImage = _selectionStart;
                    if (posInImage.X >= 0 && posInImage.X <= vm.ImageWidth && vm.ImageWidth > 0)
                    {
                        double seekFraction = posInImage.X / vm.ImageWidth;
                        vm.SeekToRelativePosition(seekFraction);
                    }
                }
            }
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not SpectrogramViewerViewModel vm) return;

        switch (e.Key)
        {
            case Key.F1:
            case Key.OemQuestion: // '?' key
                ShortcutsOverlay.IsVisible = !ShortcutsOverlay.IsVisible;
                e.Handled = true;
                break;

            case Key.Escape:
                if (ShortcutsOverlay.IsVisible)
                {
                    ShortcutsOverlay.IsVisible = false;
                    e.Handled = true;
                }
                else if (vm.HasSelection)
                {
                    vm.ClearSelection();
                    e.Handled = true;
                }
                else
                {
                    vm.CloseCommand.Execute().Subscribe();
                    e.Handled = true;
                }
                break;

            case Key.Space:
                if (vm.HasSelection)
                    vm.PlaySelection();
                else
                    vm.PlayPauseCommand.Execute().Subscribe();
                e.Handled = true;
                break;

            case Key.OemPlus or Key.Add:
                vm.ZoomIn();
                e.Handled = true;
                break;

            case Key.OemMinus or Key.Subtract:
                vm.ZoomOut();
                e.Handled = true;
                break;

            case Key.Left:
                if (vm.HasPrev)
                {
                    vm.PrevCommand.Execute().Subscribe();
                    e.Handled = true;
                }
                break;

            case Key.Right:
                if (vm.HasNext)
                {
                    vm.NextCommand.Execute().Subscribe();
                    e.Handled = true;
                }
                break;

            case Key.Home:
                ImageScroller.Offset = new Vector(0, ImageScroller.Offset.Y);
                e.Handled = true;
                break;

            case Key.End:
                ImageScroller.Offset = new Vector(
                    ImageScroller.ScrollBarMaximum.X,
                    ImageScroller.Offset.Y);
                e.Handled = true;
                break;

            case Key.F:
                vm.FitToWindowCommand.Execute().Subscribe();
                e.Handled = true;
                break;

            case Key.D0 or Key.D1:
                vm.ResetZoomCommand.Execute().Subscribe();
                e.Handled = true;
                break;

            case Key.Delete:
                vm.ClearSelection();
                e.Handled = true;
                break;

            case Key.L:
                vm.ToggleLoopCommand.Execute().Subscribe();
                e.Handled = true;
                break;
        }
    }

    private void UpdateViewportSize()
    {
        if (DataContext is SpectrogramViewerViewModel vm && ImageScroller is not null)
        {
            vm.ViewportWidth = ImageScroller.Bounds.Width;
            vm.ViewportHeight = ImageScroller.Bounds.Height;
        }
    }

    // ---- Helpers ----

    private static double ChooseTimeInterval(double visibleSeconds, double widthPixels)
    {
        double pixelsPerSecond = widthPixels / visibleSeconds;
        double[] candidates = { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 20, 30, 60, 120, 300, 600 };
        foreach (var c in candidates)
        {
            if (c * pixelsPerSecond >= 70) return c;
        }
        return candidates[^1];
    }

    /// <summary>
    /// Format time for the scale labels: sub-second precision when zoomed in.
    /// </summary>
    private static string FormatTimeScaleLabel(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (seconds < 60)
        {
            // Show milliseconds for short intervals
            if (seconds < 1)
                return $"0:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
            return $"{ts.Minutes}:{ts.Seconds:D2}.{ts.Milliseconds / 100}";
        }
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// Export file picker: shows a save dialog and returns the chosen path.
    /// </summary>
    private async Task<string?> ExportFilePickerAsync(string? suggestedName)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var storageProvider = topLevel.StorageProvider;
        var file = await storageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export Selection as WAV",
            SuggestedFileName = suggestedName ?? "export.wav",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("WAV Audio") { Patterns = new[] { "*.wav" } }
            }
        });

        return file?.Path.LocalPath;
    }
}
