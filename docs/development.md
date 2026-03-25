# Audio2Image — Development Guide

Guide for building, testing, and contributing to Audio2Image.

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Build](#build)
- [Testing](#testing)
- [Publishing](#publishing)
- [Project Conventions](#project-conventions)
- [Adding a New Feature](#adding-a-new-feature)
- [Common Pitfalls](#common-pitfalls)
- [Tool Scripts](#tool-scripts)

---

## Prerequisites

- **.NET 10 SDK** (10.0.103 or later) — download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download)
- **Windows 10/11 x64** — required for NAudio playback and SkiaSharp rendering
- **Git** — for version control

### IDE Recommendations

- **JetBrains Rider** — best Avalonia support with previewer
- **Visual Studio 2022** with Avalonia extension
- **VS Code** with C# Dev Kit extension

---

## Build

```bash
# Build in Release mode (always use Release, NOT Debug)
dotnet build -c Release

# Expected: 0 errors, 0 warnings
```

> **Important:** Always compile in Release (`-c Release`), not Debug. The project is configured for Release-mode optimizations including ReadyToRun.

### Solution File

The solution uses `.slnx` format (not `.sln`):

```bash
# Open in IDE
Audio2Image.slnx
```

---

## Testing

```bash
# Run all tests
dotnet test -c Release

# Expected: 134 passed (98 Core + 36 App VM)

# Run specific test project
dotnet test tests/Audio2Image.Core.Tests/ -c Release
dotnet test tests/Audio2Image.App.Tests/ -c Release

# Run with verbose output
dotnet test -c Release -v normal
```

### Test Structure

| Project | Tests | Scope |
|---------|-------|-------|
| `Audio2Image.Core.Tests` | 98 | Core services: decoder, FFT, renderer, database, scanner, embeddings, tags |
| `Audio2Image.App.Tests` | 36 | ViewModels: MainWindow, SpectrogramViewer, Settings |

### Test Conventions

- Tests use **xUnit** framework with **NSubstitute** for mocking
- Interfaces in `Abstractions/` enable mocking of all services
- ViewModel tests verify command execution, property changes, and state transitions
- No integration tests requiring actual audio files (mocked)

---

## Publishing

### Self-Contained Executable

```bash
dotnet publish src/Audio2Image.App/Audio2Image.App.csproj \
    -c Release \
    -r win-x64 \
    --self-contained \
    -p:PublishSingleFile=true
```

Output: `src/Audio2Image.App/bin/Release/net10.0/win-x64/publish/Audio2Image.exe`

### What's Included

The self-contained build bundles:
- .NET 10 runtime
- All NuGet dependencies (NAudio, SkiaSharp, ONNX Runtime, etc.)
- Application icon (`app-icon.ico`)
- ReadyToRun pre-compiled native code for faster startup

### What's NOT Included

- PANNs CNN14 ONNX model (~320 MB) — downloaded on demand from HuggingFace
- User data (database, settings, spectrograms) — created at runtime

---

## Project Conventions

### Code Style

- **Language:** C# 13 (.NET 10)
- **Naming:** PascalCase for public members, camelCase for private fields, `_` prefix for private fields
- **Async:** All I/O-bound operations are async (`Task`/`ValueTask`)
- **Nullability:** Enabled project-wide
- **Comments:** English for all code, comments, and commit messages

### MVVM Rules

1. **Views** contain no business logic — only UI layout (AXAML) and rendering code (code-behind)
2. **ViewModels** contain all state and logic, inherit from `ReactiveObject`
3. **Models** are plain DTOs or records
4. **Services** in Core library are stateless or thread-safe

### Binding Conventions

- Use **compiled bindings** (`{Binding Property}` with `x:DataType`) where possible
- For cross-element bindings: use `{ReflectionBinding #ElementName.DataContext.Property}`
  - Avalonia compiled bindings don't support `((Type)Property)` cast syntax
- All theme colors use `{DynamicResource BrushName}` for runtime theme switching

### FluentIcons

Icons use `FluentIcons.Avalonia` with enum names **without** the `24Regular` suffix:

```xml
<!-- Correct -->
<ic:SymbolIcon Symbol="Play"/>
<ic:SymbolIcon Symbol="FolderOpen"/>

<!-- WRONG — will not compile -->
<ic:SymbolIcon Symbol="Play24Regular"/>
```

---

## Adding a New Feature

### 1. Core Service (if needed)

1. Define interface in `Audio2Image.Core/Abstractions/`
2. Implement in the appropriate subdirectory
3. Write unit tests in `Audio2Image.Core.Tests/`
4. Register in `App.axaml.cs` (manual DI)

### 2. ViewModel Changes

1. Add properties/commands to the relevant ViewModel
2. Use `ReactiveCommand` for async operations
3. Use `this.RaisePropertyChanged()` or `[Reactive]` attribute
4. Write ViewModel tests in `Audio2Image.App.Tests/`

### 3. View Changes

1. Add AXAML markup with compiled bindings
2. Use `DynamicResource` for all theme-dependent colors
3. Canvas drawing goes in code-behind (C#)
4. Input handling (mouse, keyboard) goes in code-behind

### 4. Database Changes

1. Increment schema version in `SpectrogramDatabase.cs`
2. Add migration in the `MigrateSchema` method
3. Update `SpectrogramRecord` model
4. Write migration tests

---

## Common Pitfalls

### Avalonia-Specific

| Issue | Solution |
|-------|----------|
| `ItemsRepeater`/`UniformGridLayout` not found | Not available in base Avalonia 11.3.0 — use `ItemsControl` + `WrapPanel` |
| Canvas inside Panel has 0×0 size | Must set explicit `Width`/`Height` bindings |
| `NumericUpDown.Value` type mismatch | It's `decimal?`, not `int` — cast appropriately |
| Compiled binding cast syntax fails | Use `{ReflectionBinding}` instead of `((Type)Property)` |
| `OnLoaded` fires before DataContext set | Use `OnDataContextChanged` for subscriptions in UserControls |

### Threading

| Issue | Solution |
|-------|----------|
| `Progress<T>` on UI thread causes double marshaling | Use `Action<T>` callback with `Dispatcher.UIThread.InvokeAsync` |
| Thread pool starvation on large libraries | Use `SemaphoreSlim` for concurrency limiting |
| `Parallel.ForEach` blocks UI | Always use `Parallel.ForEachAsync` or `Task.Run` wrapper |
| Audio playback race conditions | All `AudioPlaybackService` methods are `lock`-protected |

### File System

| Issue | Solution |
|-------|----------|
| M3U export fails on Windows | Sanitize filename with `Path.GetInvalidFileNameChars()` — Windows doesn't allow `:` |
| `File.Exists` slow for many files | Batch checks on background thread, don't block UI |

### Performance

| Issue | Solution |
|-------|----------|
| `MaxDegreeOfParallelism = -1` hides progress | Use `Environment.ProcessorCount` for meaningful progress reporting |
| Gallery loads slowly | Ensure thumbnails are stored as BLOBs, not loaded from files |
| Embedding computation slow | Process in background with concurrency-limited async |

---

## Tool Scripts

### Screenshot Capture (`tools/Take-Screenshots.ps1`)

Automated screenshot capture for documentation:

```powershell
# Start Audio2Image first, then run:
.\tools\Take-Screenshots.ps1

# Options:
#   -ProcessName "Audio2Image"  (default)
#   -OutputDir "docs\screenshots"  (default)
#   -DelayMs 800  (delay between actions, default)
```

The script:
1. Finds the running Audio2Image window by process name
2. Captures 10 screenshots of different UI states via Win32 API
3. Navigates the UI using SendKeys and mouse automation
4. Saves PNGs to `docs/screenshots/`

### Icon Generator (`tools/GenerateIcon.csx`)

C# script for generating the application icon in multiple sizes.

---

## Dependencies

### Runtime

| Package | Version | Purpose |
|---------|---------|---------|
| Avalonia | 11.3.0 | UI framework |
| Avalonia.Desktop | 11.3.0 | Windows desktop integration |
| Avalonia.ReactiveUI | 11.3.0 | MVVM bindings |
| NAudio | 2.2.1 | Audio decoding and playback |
| NAudio.Vorbis | 1.5.0 | OGG Vorbis support |
| MathNet.Numerics | 5.0.0 | FFT computation |
| SkiaSharp | 3.119.0 | PNG/JPEG rendering |
| Microsoft.Data.Sqlite | 9.0.4 | Database |
| Microsoft.ML.OnnxRuntime | 1.24.3 | PANNs CNN14 inference |
| FluentIcons.Avalonia | 2.0.321 | Toolbar icons |

### Test

| Package | Purpose |
|---------|---------|
| xUnit | Test framework |
| NSubstitute | Mocking |
| Microsoft.NET.Test.Sdk | Test runner |

---

## Release Checklist

1. [ ] All tests pass: `dotnet test -c Release`
2. [ ] Build clean: `dotnet build -c Release` (0 warnings)
3. [ ] Publish: `dotnet publish ... -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
4. [ ] Test published executable on clean machine
5. [ ] Update version in project file if needed
6. [ ] Update README.md screenshots if UI changed
7. [ ] Create release zip: `Audio2Image-vX.Y.Z-win-x64.zip`
