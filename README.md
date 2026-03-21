# Audio2Image

Desktop application for batch-converting audio files (MP3/WAV/OGG) into high-resolution spectrograms with a built-in interactive viewer, audio playback, AI-powered similarity search, and library management. Inspired by the spectrogram display in [iZotope RX 11](https://www.izotope.com/en/products/rx.html).

---

## Features

### Spectrogram Generation
- **Batch processing** — scan entire directories recursively or select individual files
- **High-resolution output** — clean PNG spectrograms without baked-in labels or axes
- **Mel-scale frequency mapping** — perceptually uniform distribution matching professional audio tools
- **Warm orange/amber colormap** — RX 11-inspired aesthetic with deep black backgrounds
- **Configurable analysis** — FFT size (2048–8192), hop size, dynamic range (40–120 dB)
- **Multi-threaded pipeline** — parallel processing across all CPU cores
- **Inline thumbnail generation** — JPEG thumbnails stored as BLOB in SQLite for instant gallery loading

### Interactive Viewer
- **Programmatic frequency scales** — left and right axes (20 Hz – 20 kHz) drawn in real-time, synced with scroll
- **Time axis** — auto-interval labels synced with horizontal scroll and zoom
- **Horizontal zoom** — mouse wheel zooms the time axis while frequency axis stays fixed; fit-to-window as 100% baseline
- **Playback cursor** — yellow teardrop marker with dashed line tracking playback position
- **Time selection** — vertical strip selection for range playback
- **Frequency selection** — horizontal strip selection with real-time IIR Butterworth bandpass filter playback
- **Loop playback** — loop selected time ranges with toggle control
- **Auto loop finder** — automatic detection of optimal loop points via waveform analysis
- **Export selection** — save selected time range as WAV file
- **Click-to-seek** — click anywhere on the spectrogram to jump to that time position
- **Drag-to-pan** — middle mouse button or Ctrl+click to navigate
- **Transport controls** — play/pause, stop, seek bar, volume, time display (h:mm:ss.fff)
- **Cursor info** — real-time display of time and frequency under mouse pointer
- **Keyboard shortcuts** — full set of hotkeys with F1 overlay help

### Library Management
- **SQLite database** — persistent library with metadata, thumbnails, embeddings
- **Versioned schema migrations** — automatic database upgrades across versions
- **Instant gallery loading** — thumbnails stored as JPEG BLOBs in database (no file I/O per item)
- **Search** — filter library by filename with result count display
- **Sort** — by name (A-Z / Z-A), date added, duration, or rating
- **Delete** — remove items from library with confirmation dialog
- **Drag-and-drop** — drop audio files or folders directly onto the window
- **Multi-select** — Ctrl+Click to toggle, Shift+Click for range, Ctrl+A to select all
- **Batch operations** — delete, tag, or add to playlist for multiple selected items

### Playlists
- **Create, rename, delete** playlists
- **Add/remove tracks** — single or batch add via multi-select
- **Export M3U** — export any playlist, tag filter, or similarity results as `.m3u` file for external players

### Star Rating
- **1–5 star rating** — rate tracks via right-click context menu
- **Sort by rating** — dedicated sort option in the toolbar
- **Toggle rating** — click same star to clear

### User Tags
- **Custom tags** — create user-defined tags (e.g. "favorite", "ambient", "drums")
- **Assign tags** — tag individual tracks or batch-tag selected items
- **Visual pills** — colored pill badges displayed alongside AI tags in the gallery
- **Filter by tag** — click to view all tracks with a specific user tag

### AI Features
- **Similarity search** — find similar tracks using PANNs CNN14 audio embeddings (2048-dim cosine similarity)
- **Auto-tagging** — automatic AudioSet classification with 527 labels, top-5 shown as color-coded pills
- **Tag filtering** — click any AI tag to filter library
- **Background processing** — embeddings computed asynchronously during idle time
- **On-demand model download** — PANNs CNN14 ONNX model (~320 MB) downloaded from [HuggingFace](https://huggingface.co/Vaytl/PANNs_CNN14_ONNX)

### Appearance
- **Dark / Light theme** — switchable in Settings, applied instantly
- **Two colormaps** — Hot (orange/amber) or Viridis

### Performance
- **ReadyToRun (R2R)** — pre-compiled native code for faster cold startup
- **Async thumbnail loading** — thumbnails decode from BLOB on UI thread (fast path) or file in background (legacy fallback)
- **Concurrency-limited decoding** — SemaphoreSlim prevents thread pool starvation on large libraries
- **SIMD-accelerated similarity** — Vector<float> optimized cosine similarity for 2048-dim embeddings
- **Thread-safe audio playback** — all AudioPlaybackService methods properly synchronized

---

## Screenshots

> *The application renders spectrograms in a warm orange/amber palette, with interactive frequency and time scales, playback cursor, selection overlays, star ratings, and tag pills — similar to iZotope RX 11.*

---

## Tech Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET | 10.0 |
| UI Framework | Avalonia UI | 11.3.0 |
| MVVM | ReactiveUI | — |
| Audio Decoding & Playback | NAudio | 2.2.1 |
| Vorbis Support | NAudio.Vorbis | 1.5.0 |
| FFT | MathNet.Numerics | 5.0.0 |
| Image Rendering | SkiaSharp | 3.119.0 |
| Database | Microsoft.Data.Sqlite | 9.0.4 |
| ML Inference | Microsoft.ML.OnnxRuntime | 1.24.3 |
| Icons | FluentIcons.Avalonia | 2.0.321 |
| Testing | xUnit + NSubstitute | — |

---

## Project Structure

```
Audio2Image.slnx                        # Solution file
src/
  Audio2Image.Core/                     # Core library (classlib)
    Abstractions/                       # Interfaces (ISpectrogramDatabase, IAudioPlaybackService, IUserTagService, etc.)
    Audio/
      AudioDecoder.cs                   # MP3/WAV/OGG decoding via NAudio, mono downmix
      AudioPlaybackService.cs           # Thread-safe playback engine (WaveOutEvent)
      AudioExporter.cs                  # WAV range export with optional crossfade
      BandpassSampleProvider.cs         # IIR Butterworth biquad bandpass filter
    Dsp/
      WindowFunctions.cs               # Hann window function
      FftProcessor.cs                  # STFT: windowing → FFT → magnitude (dB)
      MelScale.cs                      # Hz↔Mel conversion, frequency mapping
      LoopFinder.cs                    # Automatic loop point detection
    Embeddings/
      AudioEmbeddingService.cs         # PANNs CNN14 inference via ONNX Runtime
      ModelDownloader.cs               # On-demand model download from HuggingFace
      SimilaritySearch.cs              # SIMD-accelerated cosine similarity
    Models/
      AudioFileInfo.cs                 # Scanned file metadata
      AudioData.cs                     # Decoded audio (float[] samples, sample rate, duration)
      SpectrogramData.cs               # FFT result (magnitude matrix)
      SpectrogramRecord.cs             # SQLite record model (thumbnail BLOB, rating)
      Playlist.cs                      # Playlist model
      UserTag.cs                       # User-defined tag model
    Pipeline/
      SpectrogramPipeline.cs           # Orchestration: scan → decode → FFT → render → thumbnail
    Rendering/
      SpectrogramColorMap.cs           # Hot (orange) + Viridis colormaps (256-entry LUT)
      SpectrogramRenderer.cs           # PNG renderer + inline JPEG thumbnail generation
      ThumbnailGenerator.cs            # SkiaSharp JPEG thumbnail from bitmap or file
    Scanning/
      AudioScanner.cs                  # Recursive directory scanner for .mp3/.wav/.ogg
    Settings/
      AppSettings.cs                   # Settings model (FFT, hop, colormap, theme, etc.)
      SettingsService.cs               # JSON persistence (settings.json)
    Storage/
      SpectrogramDatabase.cs           # SQLite CRUD with versioned schema migrations (v4)
      PlaylistService.cs               # Playlist CRUD
      UserTagService.cs                # User tag CRUD + spectrogram-tag assignments

  Audio2Image.App/                      # Avalonia UI application (WinExe)
    Program.cs                         # Entry point
    App.axaml / App.axaml.cs           # Application bootstrap, theme switching, DI container
    app-icon.ico                       # Application icon (multi-size ICO)
    Models/
      SpectrogramItem.cs               # Gallery item (thumbnail, rating, tags, selection state)
      TagDisplayItem.cs                # Color-coded tag display model
      TagCategoryColors.cs             # AudioSet tag → color mapping
    ViewModels/
      ViewModelBase.cs                 # ReactiveObject base class
      MainWindowViewModel.cs           # Gallery, library, processing, playlists, embeddings, tags, rating, multi-select
      SpectrogramViewerViewModel.cs    # Playback, zoom, selection, loop, export
      SettingsViewModel.cs             # Settings UI bindings with validation + theme switching
    Views/
      MainWindow.axaml / .cs           # Gallery view, toolbar, search, drag-and-drop, multi-select, F1 help
      SpectrogramViewer.axaml / .cs    # Viewer with scales, overlays, seekbar drag support
      SettingsWindow.axaml / .cs       # Settings dialog (theme, colormap, FFT, storage)
      AboutWindow.axaml / .cs          # About dialog

tests/
  Audio2Image.Core.Tests/              # Unit tests (98 tests)
  Audio2Image.App.Tests/               # ViewModel tests (36 tests)
```

---

## Build & Run

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.103 or later)

### Build

```bash
dotnet build -c Release
```

### Run Tests

```bash
dotnet test -c Release
```

### Run Application

```bash
dotnet run --project src/Audio2Image.App/Audio2Image.App.csproj -c Release
```

### Publish Self-Contained Executable

```bash
dotnet publish src/Audio2Image.App/Audio2Image.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `src/Audio2Image.App/bin/Release/net10.0/win-x64/publish/Audio2Image.exe`

---

## Usage

### Quick Start

1. **Open files** — click the folder icon to select a directory, or the file icon to pick individual audio files
2. **Drag and drop** — drag audio files or folders directly onto the application window
3. **Wait for processing** — progress bar shows batch conversion status
4. **Browse gallery** — spectrograms appear instantly with cached thumbnails
5. **Click to view** — click any item to open the full spectrogram viewer

### Gallery Shortcuts

| Action | Input |
|--------|-------|
| Open files | Ctrl+O |
| Open folder | Ctrl+Shift+O |
| Focus search | Ctrl+F |
| Select all | Ctrl+A |
| Deselect all | Escape |
| Settings | Ctrl+, |
| Refresh library | F5 |
| Show shortcuts | F1 |

### Viewer Shortcuts

| Action | Input |
|--------|-------|
| Zoom in/out | Mouse wheel / +/- |
| Pan | Middle click + drag / Ctrl+left drag |
| Seek | Left click on spectrogram |
| Time selection | Left drag (in Time mode) |
| Frequency selection | Left drag (in Frequency mode) |
| Play / Pause | Space |
| Toggle loop | L |
| Fit to window | F |
| Actual size (1:1) | 1 |
| Previous / Next | Left / Right arrow |
| Scroll to start/end | Home / End |
| Clear selection | Delete / Escape |
| Show shortcuts | F1 |
| Close viewer | Escape |

### Selection Playback

- **Time selection** — plays only the selected time range
- **Frequency selection** — plays the entire track through a real-time bandpass filter isolating the selected frequency range

### Context Menu (Right-Click)

- Open in Viewer
- Open File Location
- Find Similar
- Rate (1-5 stars / Clear)
- Assign Tag
- Add to Playlist
- Delete from Library

---

## Architecture

### Processing Pipeline

```
Audio Files (.mp3/.wav/.ogg)
    │
    ▼
AudioScanner          Recursive directory scan, filter by extension
    │
    ▼
AudioDecoder          NAudio: decode → mono float[] at original sample rate
    │
    ▼
FftProcessor          Hann window → STFT → magnitude spectrum (dB)
    │
    ▼
SpectrogramRenderer   Mel-scale frequency mapping → colormap → PNG + JPEG thumbnail
    │
    ▼
SpectrogramDatabase   Store metadata + thumbnail BLOB in SQLite, PNG on disk
```

### Database Schema (v4)

```sql
spectrograms (id, audio_file_path, audio_file_name, image_path, file_size_bytes,
              duration_seconds, sample_rate, created_at, embedding, embedding_model,
              tags, thumbnail_data, rating)

playlists (id, name, created_at, updated_at)
playlist_items (id, playlist_id, spectrogram_id, position)

user_tags (id, name, color)
spectrogram_user_tags (id, spectrogram_id, tag_id)
```

### Key Design Decisions

- **Mel-scale mapping** — formula `2595 * log10(1 + f/700)` used consistently in renderer, viewer, and selection calculations
- **Clean PNG output** — spectrogram images contain only pixel data; axes and labels are drawn programmatically in the viewer for zoom/scroll independence
- **Horizontal-only zoom** — frequency axis remains fixed while time axis scales, matching professional spectrogram tools
- **Fit zoom = 100%** — the zoom level that fits the spectrogram to the viewport width is the baseline; users cannot zoom out past it
- **IIR bandpass filter** — cascaded 4th-order Butterworth biquad (24 dB/octave) for real-time frequency selection playback
- **Frequency range** — fixed 20 Hz to 20 kHz regardless of sample rate
- **Gamma correction** — `sqrt(normalized)` applied after dB normalization for balanced visibility across dynamic range
- **DynamicResource brushes** — all theme colors use DynamicResource for runtime theme switching
- **Versioned DB migrations** — `_schema_version` table tracks schema version for safe incremental upgrades
- **Thread-safe playback** — all AudioPlaybackService public methods protected by lock

---

## Configuration

Settings are stored in `settings.json` in the library directory:

```json
{
  "FftSize": 4096,
  "HopSize": 512,
  "Colormap": "Hot",
  "DynamicRangeDb": 90,
  "Theme": "Dark",
  "LibraryPath": "C:\\Users\\...\\Audio2Image",
  "DatabasePath": "C:\\Users\\...\\Audio2Image\\audio2image.db",
  "EmbeddingsEnabled": true
}
```

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| FFT Size | 4096 | 2048–8192 | Frequency resolution (larger = more precise, slower) |
| Hop Size | 512 | 128–FFT Size | Time resolution (smaller = smoother, larger images) |
| Colormap | Hot | Hot, Viridis | Color palette for spectrogram rendering |
| Dynamic Range | 90 dB | 40–120 dB | Visible amplitude range from peak |
| Theme | Dark | Dark, Light | Application color theme |
| Library Path | Documents/Audio2Image | — | Directory for PNG output and database |

---

## Supported Formats

| Format | Extension | Notes |
|--------|-----------|-------|
| MP3 | `.mp3` | All bitrates and sample rates |
| WAV | `.wav` | PCM, IEEE float, mono and stereo |
| OGG | `.ogg` | Vorbis codec via NAudio.Vorbis |

Stereo files are automatically downmixed to mono for analysis.

---

## License

This project is provided as-is for personal and educational use.
