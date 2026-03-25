# Audio2Image — Architecture

Technical architecture documentation for developers and contributors.

---

## Table of Contents

- [Overview](#overview)
- [Project Structure](#project-structure)
- [Processing Pipeline](#processing-pipeline)
- [Core Components](#core-components)
- [Database Schema](#database-schema)
- [UI Architecture](#ui-architecture)
- [Audio Playback](#audio-playback)
- [AI / ML System](#ai--ml-system)
- [Theming](#theming)
- [Key Design Decisions](#key-design-decisions)

---

## Overview

Audio2Image is a .NET 10 desktop application built with Avalonia UI (11.3.0) and ReactiveUI for MVVM. It converts audio files into high-resolution spectrograms and provides an interactive viewer, audio playback, AI-powered similarity search, and library management.

### Architecture Layers

```
┌──────────────────────────────────────────────────────┐
│                  Audio2Image.App                      │
│  ┌──────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │  Views    │  │  ViewModels  │  │    Models      │  │
│  │  (AXAML)  │←→│ (ReactiveUI) │←→│ (Display DTOs) │  │
│  └──────────┘  └──────┬───────┘  └───────────────┘  │
│                       │                               │
├───────────────────────┼──────────────────────────────┤
│                Audio2Image.Core                       │
│  ┌─────────┐  ┌──────┴──────┐  ┌─────────────────┐  │
│  │ Pipeline │  │  Services   │  │    Storage       │  │
│  │ (Scan →  │  │ (Playback,  │  │ (SQLite DB,     │  │
│  │  Decode →│  │  Embeddings,│  │  Playlists,     │  │
│  │  FFT →   │  │  Settings)  │  │  UserTags)      │  │
│  │  Render) │  │             │  │                 │  │
│  └─────────┘  └─────────────┘  └─────────────────┘  │
└──────────────────────────────────────────────────────┘
```

---

## Project Structure

```
Audio2Image.slnx                         # Solution file (.slnx, NOT .sln)
src/
  Audio2Image.Core/                      # Core library (classlib)
    Abstractions/                        # Interfaces
    Audio/                               # Decoding, playback, filtering, export
    Dsp/                                 # FFT, mel-scale, window functions, loop finder
    Embeddings/                          # PANNs CNN14, model download, similarity
    Models/                              # Data transfer objects
    Pipeline/                            # Orchestration
    Rendering/                           # PNG/JPEG spectrogram rendering
    Scanning/                            # File system scanner
    Settings/                            # JSON settings persistence
    Storage/                             # SQLite database, playlists, user tags

  Audio2Image.App/                       # Avalonia UI application (WinExe)
    Models/                              # UI display models
    ViewModels/                          # ReactiveUI view models
    Views/                               # AXAML views + code-behind

tests/
  Audio2Image.Core.Tests/                # 98 unit tests
  Audio2Image.App.Tests/                 # 36 view model tests
```

### Key Files

| File | Purpose | Lines |
|------|---------|-------|
| `MainWindowViewModel.cs` | Gallery, library, processing, search, sort, playlists, tags, rating, multi-select | ~1450 |
| `SpectrogramViewerViewModel.cs` | Playback, zoom, selection, loop, export | ~500 |
| `SpectrogramViewer.axaml.cs` | Canvas rendering (scales, cursor, selection, loop markers), input handling | ~890 |
| `MainWindow.axaml` | Gallery layout, toolbar, batch bar, overlays | ~710 |
| `SpectrogramDatabase.cs` | SQLite CRUD with versioned migrations | ~400 |
| `SpectrogramPipeline.cs` | Scan → decode → FFT → render orchestration | ~200 |

---

## Processing Pipeline

### Flow

```
Audio Files (.mp3/.wav/.ogg)
    │
    ▼
AudioScanner              Recursive directory scan
    │                     Filters: .mp3, .wav, .ogg
    ▼
AudioDecoder              NAudio: decode to mono float[]
    │                     Stereo → mono downmix
    │                     Original sample rate preserved
    ▼
FftProcessor              Hann window → STFT
    │                     Output: magnitude spectrum (dB)
    │                     Configurable FFT size (2048-8192)
    ▼
SpectrogramRenderer       Mel-scale frequency mapping
    │                     256-entry color LUT (Hot/Viridis)
    │                     Gamma: sqrt(normalized)
    │                     Output: PNG + JPEG thumbnail
    ▼
SpectrogramDatabase       Store metadata + thumbnail BLOB
                          PNG saved to disk
```

### Parallel Processing

The pipeline uses `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = Environment.ProcessorCount`. Each file is processed independently: decode → FFT → render → save. Progress is reported via `Action<T>` callback with `Dispatcher.UIThread.InvokeAsync` (not `Progress<T>` to avoid double marshaling).

### Mel-Scale Mapping

The mel-scale formula is used consistently across the entire application:

```
mel = 2595 * log10(1 + freq / 700)
freq = 700 * (10^(mel / 2595) - 1)
```

Frequency range is fixed at **20 Hz to 20 kHz** regardless of sample rate. The mel-scale provides perceptually uniform spacing where lower frequencies (more important to human hearing) get more visual resolution.

---

## Core Components

### AudioDecoder (`Audio/AudioDecoder.cs`)

Decodes MP3, WAV, and OGG files to `AudioData` (mono float[] + sample rate + duration).

- Uses `AudioFileReader` (MP3/WAV) and `VorbisWaveReader` (OGG) from NAudio
- Automatic stereo-to-mono downmix
- Preserves original sample rate (no resampling for rendering)
- For embeddings: resamples to 32 kHz mono

### FftProcessor (`Dsp/FftProcessor.cs`)

Performs Short-Time Fourier Transform (STFT):

1. Apply Hann window to each frame
2. Compute FFT via MathNet.Numerics
3. Convert to magnitude spectrum in dB
4. Output: 2D magnitude matrix (frames × frequency bins)

### SpectrogramRenderer (`Rendering/SpectrogramRenderer.cs`)

Converts FFT output to visual spectrogram:

1. Map frequency bins to mel-scale vertical positions
2. Normalize dB values within dynamic range
3. Apply gamma correction: `sqrt(normalized)`
4. Look up color from 256-entry LUT (Hot or Viridis)
5. Render to SkiaSharp `SKBitmap`
6. Save as PNG (full resolution) + JPEG thumbnail (inline BLOB)

### SpectrogramColorMap (`Rendering/SpectrogramColorMap.cs`)

Two colormaps with 256-entry lookup tables:

- **Hot**: Black → dark red → orange → amber → white (RX 11 aesthetic)
- **Viridis**: Dark purple → blue → teal → green → yellow (scientific standard)

### AudioPlaybackService (`Audio/AudioPlaybackService.cs`)

Thread-safe playback engine wrapping NAudio's `WaveOutEvent`:

- All public methods protected by `lock`
- Supports seek, volume, position tracking
- Integrates with `BandpassSampleProvider` for frequency selection playback
- Position reported via polling (not events) for UI cursor updates

### BandpassSampleProvider (`Audio/BandpassSampleProvider.cs`)

Real-time IIR Butterworth biquad bandpass filter:

- Cascaded 4th-order (24 dB/octave rolloff)
- Coefficient calculation from center frequency and bandwidth
- Applied sample-by-sample during playback
- Used for frequency selection playback in the viewer

### LoopFinder (`Dsp/LoopFinder.cs`)

Automatic detection of optimal loop points:

- Analyzes waveform for zero-crossings and amplitude envelopes
- Finds matching points where audio can seamlessly loop
- Results shown as green dashed markers in the viewer

---

## Database Schema

### Version Management

The database uses a `_schema_version` table to track and apply incremental migrations. Current version: **v4**.

### Tables

```sql
-- Core spectrogram data
spectrograms (
    id                INTEGER PRIMARY KEY,
    audio_file_path   TEXT NOT NULL,
    audio_file_name   TEXT NOT NULL,
    image_path        TEXT NOT NULL,
    file_size_bytes   INTEGER,
    duration_seconds  REAL,
    sample_rate       INTEGER,
    created_at        TEXT,
    embedding         BLOB,            -- v2: 2048-dim float[] (PANNs CNN14)
    embedding_model   TEXT,            -- v2: model identifier
    tags              TEXT,            -- v3: JSON array of AudioSet labels
    thumbnail_data    BLOB,            -- v3: JPEG thumbnail for instant gallery loading
    rating            INTEGER          -- v4: 1-5 star rating
)

-- Playlists
playlists (
    id         INTEGER PRIMARY KEY,
    name       TEXT NOT NULL,
    created_at TEXT,
    updated_at TEXT
)

playlist_items (
    id              INTEGER PRIMARY KEY,
    playlist_id     INTEGER REFERENCES playlists(id),
    spectrogram_id  INTEGER REFERENCES spectrograms(id),
    position        INTEGER
)

-- User-defined tags
user_tags (
    id    INTEGER PRIMARY KEY,
    name  TEXT NOT NULL UNIQUE,
    color TEXT              -- hex color string
)

spectrogram_user_tags (
    id              INTEGER PRIMARY KEY,
    spectrogram_id  INTEGER REFERENCES spectrograms(id),
    tag_id          INTEGER REFERENCES user_tags(id)
)
```

### Migration History

| Version | Changes |
|---------|---------|
| v1 | Base schema: spectrograms table |
| v2 | Added embedding, embedding_model columns |
| v3 | Added tags, thumbnail_data columns |
| v4 | Added rating column |

---

## UI Architecture

### MVVM Pattern

The application follows strict MVVM with ReactiveUI:

```
View (AXAML + code-behind)
  ↕ Compiled Bindings / ReflectionBinding
ViewModel (ReactiveObject)
  ↕ Direct references
Model / Service (Core library)
```

**Code-behind is used for:**
- Canvas drawing (frequency scales, time axis, cursor, selection)
- Input handling (mouse events, keyboard shortcuts)
- Programmatic dialog creation (Confirm, Input dialogs)
- Drag-and-drop handling

**ViewModels handle:**
- All business logic and state management
- Command binding (ReactiveCommand)
- Property change notification (ReactiveObject)
- Async operations

### Views

| View | Type | ViewModel | Purpose |
|------|------|-----------|---------|
| MainWindow | Window (1280×800) | MainWindowViewModel | Gallery, toolbar, search, playlists |
| SpectrogramViewer | UserControl (overlay) | SpectrogramViewerViewModel | Full spectrogram view with playback |
| SettingsWindow | Window (520×520, modal) | SettingsViewModel | Configuration dialog |
| AboutWindow | Window (440×460, modal) | — (code-behind) | App info |

### SpectrogramViewer Rendering

The viewer uses programmatic Canvas drawing rather than AXAML templates for performance:

- **Frequency scales**: Mel-mapped tick marks and labels (20 Hz – 20 kHz)
- **Time axis**: Auto-interval selection (0.01s to 600s) based on zoom level
- **Playback cursor**: Yellow teardrop (Line + Ellipse + Path triangle)
- **Selection overlay**: Blue semi-transparent Rectangle + yellow dashed border Lines
- **Loop markers**: Green dashed vertical Lines with "L" labels

Canvas elements are cached and repositioned rather than recreated on each frame.

### Gallery Item Template

Each gallery item is a 72px-high card with z-ordered layers:

1. Spectrogram thumbnail (full-width background, Stretch=Fill)
2. Dark semi-transparent overlay (opacity 0.55, reduces to 0.35 on hover)
3. Selection highlight (blue border, visible during multi-select)
4. Text content (filename, format badge, metadata, tags, rating, duration)

---

## Audio Playback

### Architecture

```
AudioPlaybackService
    │
    ├─── WaveOutEvent (NAudio)          # Audio output device
    │
    ├─── AudioFileReader                 # MP3/WAV source
    │    └── or VorbisWaveReader         # OGG source
    │
    └─── BandpassSampleProvider          # Optional: frequency selection filter
         └── ISampleProvider chain
```

### Thread Safety

All `AudioPlaybackService` public methods are protected by `lock(syncLock)`. Playback state changes (play, pause, stop, seek) are atomic.

### Frequency Selection Playback

When a frequency selection is active:
1. Calculate center frequency and bandwidth from selection bounds
2. Create `BandpassSampleProvider` wrapping the audio source
3. Apply cascaded 4th-order Butterworth biquad filter in real-time
4. Result: only the selected frequency band is audible

---

## AI / ML System

### PANNs CNN14

Audio2Image uses **PANNs CNN14** (Pre-trained Audio Neural Networks) for audio understanding:

- **Model**: CNN14 architecture trained on AudioSet
- **Format**: ONNX (~320 MB)
- **Input**: 32 kHz mono audio (resampled from source)
- **Output**: 2048-dim embedding vector + 527-class AudioSet probabilities
- **Source**: [HuggingFace/Vaytl/PANNs_CNN14_ONNX](https://huggingface.co/Vaytl/PANNs_CNN14_ONNX)

### Similarity Search

1. Compute embedding for query track (2048-dim float[])
2. Compare against all stored embeddings using cosine similarity
3. SIMD-accelerated via `Vector<float>` for batch computation
4. Results sorted by similarity score (0-100%)

### Auto-Tagging

1. Run CNN14 inference to get 527-class probabilities
2. Select top-5 labels above confidence threshold
3. Store as JSON array in `tags` column
4. Display as color-coded pills in gallery (colors mapped by AudioSet category)

### Background Processing

Embeddings are computed asynchronously during idle time:
1. On startup, scan for records without embeddings
2. Process in background with concurrency-limited parallelism
3. Status shown in gallery status bar ("Computing embeddings: 42/100")

---

## Theming

### Theme System

Audio2Image uses Avalonia's `ThemeDictionaries` with `DynamicResource` bindings for runtime theme switching:

```xml
<!-- App.axaml -->
<ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Dark">
        <SolidColorBrush x:Key="PanelBackgroundBrush" Color="#111111"/>
        <SolidColorBrush x:Key="AccentBrush" Color="#FF6B35"/>
        ...
    </ResourceDictionary>
    <ResourceDictionary x:Key="Light">
        <SolidColorBrush x:Key="PanelBackgroundBrush" Color="#F5F5F5"/>
        <SolidColorBrush x:Key="AccentBrush" Color="#E05A2A"/>
        ...
    </ResourceDictionary>
</ResourceDictionary.ThemeDictionaries>
```

### Theme Brushes

| Brush | Dark | Light | Usage |
|-------|------|-------|-------|
| PanelBackgroundBrush | #111111 | #F5F5F5 | Main backgrounds |
| ToolbarBackgroundBrush | #1A1A1A | #E8E8E8 | Toolbar, status bar |
| CardBackgroundBrush | #1E1E1E | #FFFFFF | Gallery items |
| AccentBrush | #FF6B35 | #E05A2A | Buttons, highlights |
| CursorBrush | #FFC800 | #FFC800 | Playback cursor |
| PrimaryTextBrush | #F0F0F0 | #1A1A1A | Main text |
| SecondaryTextBrush | #B0B0B0 | #666666 | Secondary text |
| TimeDisplayBrush | #44CCFF | #0088CC | Time display |

### Switching

Theme switching is handled in `App.axaml.cs`:
```csharp
Application.Current.RequestedThemeVariant = theme == "Light" 
    ? ThemeVariant.Light 
    : ThemeVariant.Dark;
```

Changes are applied instantly without restart. All UI elements using `DynamicResource` update automatically.

---

## Key Design Decisions

### Mel-Scale Everywhere
The mel-scale formula `2595 * log10(1 + f/700)` is used consistently in the renderer, viewer frequency scales, and selection calculations. This ensures pixel-perfect alignment between the PNG image and the interactive overlays.

### Clean PNG Output
Spectrogram images contain only pixel data — no axes, labels, or annotations. Scales are drawn programmatically in the viewer, enabling zoom/scroll independence and theme-aware rendering.

### Horizontal-Only Zoom
The frequency axis remains fixed while the time axis scales. This matches professional tools (iZotope RX, Adobe Audition) where frequency range is always visible. Fit-to-window zoom is the 100% baseline; users cannot zoom out past the viewport width.

### Thumbnail BLOBs
JPEG thumbnails are stored directly in SQLite as BLOBs rather than as separate files. This eliminates per-item file I/O during gallery loading, enabling near-instant display of hundreds of items.

### IIR vs FFT Filtering
Frequency selection uses IIR Butterworth biquad filters (time-domain) rather than FFT-based filtering (frequency-domain). IIR provides lower latency, constant memory usage, and real-time sample-by-sample processing suitable for audio playback.

### Versioned Schema Migrations
The `_schema_version` table enables safe incremental database upgrades. Each version adds columns or tables without dropping existing data, allowing seamless updates across application versions.

### No Dependency Injection Container
The application uses manual DI in `App.axaml.cs` rather than a DI container (like Microsoft.Extensions.DependencyInjection). This reduces complexity and startup time for a single-window desktop application where the object graph is small and static.
