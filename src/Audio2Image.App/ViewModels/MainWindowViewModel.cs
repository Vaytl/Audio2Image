using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using Audio2Image.App.Models;
using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Embeddings;
using Audio2Image.Core.Models;
using Audio2Image.Core.Pipeline;
using Audio2Image.Core.Settings;
using Audio2Image.Core.Storage;
using Avalonia.Threading;
using ReactiveUI;

namespace Audio2Image.App.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ISpectrogramPipeline _pipeline;
    private readonly IAudioScanner _scanner;
    private readonly Func<IAudioPlaybackService> _playbackFactory;
    private IAudioEmbeddingService? _embeddingService;
    private CancellationTokenSource? _embeddingCts;
    private Dictionary<long, float[]>? _embeddingCache;

    /// <summary>Exposes settings service for child VMs (e.g. SettingsViewModel).</summary>
    public ISettingsService SettingsService => _settingsService;

    private string _title = "Audio2Image — Spectrogram Gallery";
    private string _statusText = "Select a folder to scan for audio files";
    private double _progressValue;
    private double _progressMaximum = 100;
    private bool _isProcessing;
    private bool _isProgressIndeterminate;
    private string _currentFile = "";
    private bool _isViewerOpen;
    private SpectrogramViewerViewModel? _viewerVm;
    private CancellationTokenSource? _cts;
    private int _currentViewerIndex = -1;
    private ISpectrogramDatabase? _database;
    private string _searchText = "";
    private bool _isDropTargetActive;
    private bool _isLibraryEmpty = true;
    private int _sortIndex;
    private List<string> _lastErrors = new();
    private bool _hasErrors;
    private bool _hasSearchText;
    private bool _isSimilarityMode;
    private string _similaritySourceName = "";
    private string _embeddingStatusText = "";
    private bool _isComputingEmbeddings;
    private bool _isDownloadingModel;
    private bool _isTagFilterMode;
    private string _tagFilterName = "";
    private bool _isUserTagFilterMode;
    private string _userTagFilterName = "";
    private IUserTagService? _userTagService;
    private IPlaylistService? _playlistService;
    private bool _isPlaylistPanelOpen;
    private bool _isPlaylistMode;
    private long _activePlaylistId;
    private string _activePlaylistName = "";
    private Playlist? _selectedPlaylist;
    private int _selectedCount;
    private bool _isMultiSelectMode;

    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }

    public double ProgressMaximum
    {
        get => _progressMaximum;
        set => this.RaiseAndSetIfChanged(ref _progressMaximum, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set => this.RaiseAndSetIfChanged(ref _isProgressIndeterminate, value);
    }

    public string CurrentFile
    {
        get => _currentFile;
        set => this.RaiseAndSetIfChanged(ref _currentFile, value);
    }

    public bool IsViewerOpen
    {
        get => _isViewerOpen;
        set => this.RaiseAndSetIfChanged(ref _isViewerOpen, value);
    }

    public SpectrogramViewerViewModel? ViewerVm
    {
        get => _viewerVm;
        set => this.RaiseAndSetIfChanged(ref _viewerVm, value);
    }

    public bool IsDropTargetActive
    {
        get => _isDropTargetActive;
        set => this.RaiseAndSetIfChanged(ref _isDropTargetActive, value);
    }

    public bool IsLibraryEmpty
    {
        get => _isLibraryEmpty;
        set => this.RaiseAndSetIfChanged(ref _isLibraryEmpty, value);
    }

    public int SortIndex
    {
        get => _sortIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _sortIndex, value);
            LoadLibrary();
        }
    }

    public bool HasErrors
    {
        get => _hasErrors;
        set => this.RaiseAndSetIfChanged(ref _hasErrors, value);
    }

    public bool HasSearchText
    {
        get => _hasSearchText;
        set => this.RaiseAndSetIfChanged(ref _hasSearchText, value);
    }

    public bool IsSimilarityMode
    {
        get => _isSimilarityMode;
        set => this.RaiseAndSetIfChanged(ref _isSimilarityMode, value);
    }

    public string SimilaritySourceName
    {
        get => _similaritySourceName;
        set => this.RaiseAndSetIfChanged(ref _similaritySourceName, value);
    }

    public string EmbeddingStatusText
    {
        get => _embeddingStatusText;
        set => this.RaiseAndSetIfChanged(ref _embeddingStatusText, value);
    }

    public bool IsComputingEmbeddings
    {
        get => _isComputingEmbeddings;
        set => this.RaiseAndSetIfChanged(ref _isComputingEmbeddings, value);
    }

    public bool IsDownloadingModel
    {
        get => _isDownloadingModel;
        set => this.RaiseAndSetIfChanged(ref _isDownloadingModel, value);
    }

    public bool IsTagFilterMode
    {
        get => _isTagFilterMode;
        set => this.RaiseAndSetIfChanged(ref _isTagFilterMode, value);
    }

    public string TagFilterName
    {
        get => _tagFilterName;
        set => this.RaiseAndSetIfChanged(ref _tagFilterName, value);
    }

    public bool IsUserTagFilterMode
    {
        get => _isUserTagFilterMode;
        set => this.RaiseAndSetIfChanged(ref _isUserTagFilterMode, value);
    }

    public string UserTagFilterName
    {
        get => _userTagFilterName;
        set => this.RaiseAndSetIfChanged(ref _userTagFilterName, value);
    }

    public ObservableCollection<UserTag> UserTags { get; } = new();

    public bool IsPlaylistPanelOpen
    {
        get => _isPlaylistPanelOpen;
        set => this.RaiseAndSetIfChanged(ref _isPlaylistPanelOpen, value);
    }

    public bool IsPlaylistMode
    {
        get => _isPlaylistMode;
        set => this.RaiseAndSetIfChanged(ref _isPlaylistMode, value);
    }

    public string ActivePlaylistName
    {
        get => _activePlaylistName;
        set => this.RaiseAndSetIfChanged(ref _activePlaylistName, value);
    }

    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPlaylist, value);
            if (value != null) ShowPlaylist(value);
        }
    }

    public ObservableCollection<Playlist> Playlists { get; } = new();

    public int SelectedCount
    {
        get => _selectedCount;
        set => this.RaiseAndSetIfChanged(ref _selectedCount, value);
    }

    public bool IsMultiSelectMode
    {
        get => _isMultiSelectMode;
        set => this.RaiseAndSetIfChanged(ref _isMultiSelectMode, value);
    }

    public ObservableCollection<SpectrogramItem> SpectrogramItems { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            HasSearchText = !string.IsNullOrEmpty(value);
            FilterLibrary();
        }
    }

    public ReactiveCommand<Unit, Unit> SelectFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectFilesCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<SpectrogramItem, Unit> OpenViewerCommand { get; }
    public ReactiveCommand<SpectrogramItem, Unit> DeleteItemCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowErrorsCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<SpectrogramItem, Unit> OpenFileLocationCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshLibraryCommand { get; }
    public ReactiveCommand<SpectrogramItem, Unit> FindSimilarCommand { get; }
    public ReactiveCommand<Unit, Unit> BackToLibraryCommand { get; }
    public ReactiveCommand<Unit, Unit> AboutCommand { get; }
    public ReactiveCommand<string, Unit> FilterByTagCommand { get; }
    public ReactiveCommand<Unit, Unit> TogglePlaylistPanelCommand { get; }
    public ReactiveCommand<Unit, Unit> CreatePlaylistCommand { get; }
    public ReactiveCommand<Playlist, Unit> DeletePlaylistCommand { get; }
    public ReactiveCommand<Playlist, Unit> RenamePlaylistCommand { get; }
    public ReactiveCommand<SpectrogramItem, Unit> AddToPlaylistCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveFilterAsPlaylistCommand { get; }
    public ReactiveCommand<SpectrogramItem, Unit> RemoveFromPlaylistCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportPlaylistM3uCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> DeselectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> BatchDeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> BatchAddToPlaylistCommand { get; }
    public ReactiveCommand<Unit, Unit> BatchAssignTagCommand { get; }
    public ReactiveCommand<SpectrogramItem, Unit> AssignUserTagCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateUserTagCommand { get; }
    public ReactiveCommand<UserTag, Unit> FilterByUserTagCommand { get; }
    public ReactiveCommand<SpectrogramItem, Unit> Rate1Command { get; }
    public ReactiveCommand<SpectrogramItem, Unit> Rate2Command { get; }
    public ReactiveCommand<SpectrogramItem, Unit> Rate3Command { get; }
    public ReactiveCommand<SpectrogramItem, Unit> Rate4Command { get; }
    public ReactiveCommand<SpectrogramItem, Unit> Rate5Command { get; }
    public ReactiveCommand<SpectrogramItem, Unit> ClearRatingCommand { get; }

    // File save dialog for export
    public Func<string, string, Task<string?>>? FileSavePicker { get; set; }

    // These will be set by the View (MainWindow) for dialog access
    public Func<Task<string?>>? FolderPicker { get; set; }
    public Func<Task<IReadOnlyList<string>?>>? FilePicker { get; set; }
    public Func<Task>? SettingsOpener { get; set; }
    public Func<string, string, Task<bool>>? ConfirmAction { get; set; }
    public Func<Task>? AboutOpener { get; set; }
    public Func<string, string, Task<string?>>? InputDialog { get; set; }

    public MainWindowViewModel(
        ISettingsService settingsService,
        ISpectrogramPipeline pipeline,
        IAudioScanner scanner,
        Func<IAudioPlaybackService> playbackFactory)
    {
        _settingsService = settingsService;
        _pipeline = pipeline;
        _scanner = scanner;
        _playbackFactory = playbackFactory;

        var canProcess = this.WhenAnyValue(x => x.IsProcessing).Select(p => !p);
        var canCancel = this.WhenAnyValue(x => x.IsProcessing);

        SelectFolderCommand = ReactiveCommand.CreateFromTask(SelectFolderAndProcess, canProcess);
        SelectFilesCommand = ReactiveCommand.CreateFromTask(SelectFilesAndProcess, canProcess);
        CancelCommand = ReactiveCommand.Create(CancelProcessing, canCancel);
        OpenViewerCommand = ReactiveCommand.Create<SpectrogramItem>(OpenViewer);
        DeleteItemCommand = ReactiveCommand.CreateFromTask<SpectrogramItem>(DeleteItem);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(OpenSettings, canProcess);
        ShowErrorsCommand = ReactiveCommand.Create(ShowErrors);
        ClearSearchCommand = ReactiveCommand.Create(ClearSearch);
        OpenFileLocationCommand = ReactiveCommand.Create<SpectrogramItem>(OpenFileLocation);
        RefreshLibraryCommand = ReactiveCommand.Create(RefreshLibrary);
        FindSimilarCommand = ReactiveCommand.CreateFromTask<SpectrogramItem>(FindSimilar);
        BackToLibraryCommand = ReactiveCommand.Create(BackToLibrary);
        AboutCommand = ReactiveCommand.CreateFromTask(OpenAbout);
        FilterByTagCommand = ReactiveCommand.Create<string>(FilterByTag);
        TogglePlaylistPanelCommand = ReactiveCommand.Create(TogglePlaylistPanel);
        CreatePlaylistCommand = ReactiveCommand.CreateFromTask(CreatePlaylist);
        DeletePlaylistCommand = ReactiveCommand.CreateFromTask<Playlist>(DeletePlaylist);
        RenamePlaylistCommand = ReactiveCommand.CreateFromTask<Playlist>(RenamePlaylist);
        AddToPlaylistCommand = ReactiveCommand.CreateFromTask<SpectrogramItem>(AddToPlaylist);
        SaveFilterAsPlaylistCommand = ReactiveCommand.CreateFromTask(SaveFilterAsPlaylist);
        RemoveFromPlaylistCommand = ReactiveCommand.CreateFromTask<SpectrogramItem>(RemoveFromPlaylist);
        ExportPlaylistM3uCommand = ReactiveCommand.CreateFromTask(ExportPlaylistM3u);
        SelectAllCommand = ReactiveCommand.Create(SelectAll);
        DeselectAllCommand = ReactiveCommand.Create(DeselectAll);
        BatchDeleteCommand = ReactiveCommand.CreateFromTask(BatchDelete);
        BatchAddToPlaylistCommand = ReactiveCommand.CreateFromTask(BatchAddToPlaylist);
        BatchAssignTagCommand = ReactiveCommand.CreateFromTask(BatchAssignTag);
        AssignUserTagCommand = ReactiveCommand.CreateFromTask<SpectrogramItem>(AssignUserTag);
        CreateUserTagCommand = ReactiveCommand.CreateFromTask(CreateUserTag);
        FilterByUserTagCommand = ReactiveCommand.Create<UserTag>(FilterByUserTag);
        Rate1Command = ReactiveCommand.Create<SpectrogramItem>(item => SetRating(item, 1));
        Rate2Command = ReactiveCommand.Create<SpectrogramItem>(item => SetRating(item, 2));
        Rate3Command = ReactiveCommand.Create<SpectrogramItem>(item => SetRating(item, 3));
        Rate4Command = ReactiveCommand.Create<SpectrogramItem>(item => SetRating(item, 4));
        Rate5Command = ReactiveCommand.Create<SpectrogramItem>(item => SetRating(item, 5));
        ClearRatingCommand = ReactiveCommand.Create<SpectrogramItem>(item => SetRating(item, 0));

        InitializeLibrary();
    }

    private void ShowErrors()
    {
        if (_lastErrors.Count == 0) return;
        StatusText = string.Join("\n", _lastErrors.Take(10));
        HasErrors = false;
    }

    private void ClearSearch()
    {
        SearchText = "";
    }

    private void OpenFileLocation(SpectrogramItem item)
    {
        var filePath = item.AudioFilePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        try
        {
            // Open Explorer and select the file
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Silently ignore if explorer fails
        }
    }

    private void RefreshLibrary()
    {
        LoadLibrary();
    }

    private void OpenViewer(SpectrogramItem item)
    {
        _currentViewerIndex = -1;
        for (int i = 0; i < SpectrogramItems.Count; i++)
        {
            if (SpectrogramItems[i] == item)
            {
                _currentViewerIndex = i;
                break;
            }
        }

        ShowViewerAtIndex(_currentViewerIndex);
    }

    private void ShowViewerAtIndex(int index)
    {
        if (index < 0 || index >= SpectrogramItems.Count) return;

        // Dispose previous viewer VM
        if (_viewerVm is IDisposable disposable)
            disposable.Dispose();

        var item = SpectrogramItems[index];
        _currentViewerIndex = index;

        var vm = new SpectrogramViewerViewModel(_playbackFactory());
        vm.OnClose = CloseViewer;
        vm.OnNavigate = NavigateViewer;
        vm.HasPrev = index > 0;
        vm.HasNext = index < SpectrogramItems.Count - 1;
        vm.LoadImage(item.ImagePath, item.AudioFileName, item.AudioFilePath);
        ViewerVm = vm;
        IsViewerOpen = true;
    }

    private void NavigateViewer(int direction)
    {
        int newIndex = _currentViewerIndex + direction;
        if (newIndex >= 0 && newIndex < SpectrogramItems.Count)
        {
            ShowViewerAtIndex(newIndex);
        }
    }

    private void CloseViewer()
    {
        if (_viewerVm is IDisposable disposable)
            disposable.Dispose();
        IsViewerOpen = false;
        ViewerVm = null;
        _currentViewerIndex = -1;
    }

    private async Task SelectFilesAndProcess()
    {
        if (FilePicker == null) return;

        var filePaths = await FilePicker();
        if (filePaths == null || filePaths.Count == 0) return;

        await ProcessFiles(filePaths);
    }

    private async Task SelectFolderAndProcess()
    {
        string? folderPath = null;

        if (FolderPicker != null)
        {
            folderPath = await FolderPicker();
        }

        if (string.IsNullOrEmpty(folderPath))
            return;

        await ProcessFolder(folderPath);
    }

    private Core.Settings.AppSettings LoadSettings()
    {
        return _settingsService.Load();
    }

    private string GetLibraryOutputDir()
    {
        var settings = LoadSettings();
        var libPath = string.IsNullOrEmpty(settings.LibraryPath)
            ? _settingsService.GetDefaultLibraryPath()
            : settings.LibraryPath;
        Directory.CreateDirectory(libPath);
        return libPath;
    }

    private Task ProcessFiles(IReadOnlyList<string> filePaths)
    {
        var settings = LoadSettings();
        string outputDir = GetLibraryOutputDir();
        var files = filePaths.Select(fp => new AudioFileInfo(
            fp, Path.GetFileName(fp), new FileInfo(fp).Length)).ToList();

        return ProcessCore(
            indeterminate: filePaths.Count <= 3,
            statusPrefix: $"Processing {files.Count} files...",
            runPipeline: (onProgress, ct) =>
                _pipeline.RunFilesAsync(files, outputDir,
                    fftSize: settings.FftSize, hopSize: settings.HopSize,
                    dynamicRangeDb: settings.DynamicRangeDb, colormap: settings.Colormap,
                    onProgress: onProgress, cancellationToken: ct),
            getAudioFiles: _ => files);
    }

    private Task ProcessFolder(string inputDirectory)
    {
        var settings = LoadSettings();
        string outputDir = GetLibraryOutputDir();
        var options = new PipelineOptions(inputDirectory, outputDir,
            FftSize: settings.FftSize, HopSize: settings.HopSize,
            DynamicRangeDb: settings.DynamicRangeDb, Colormap: settings.Colormap);

        return ProcessCore(
            indeterminate: true,
            statusPrefix: $"Scanning {inputDirectory}...",
            runPipeline: (onProgress, ct) =>
                _pipeline.RunAsync(options, onProgress: onProgress, cancellationToken: ct),
            getAudioFiles: _ => _scanner.Scan(inputDirectory));
    }

    /// <summary>
    /// Common processing logic shared by ProcessFiles and ProcessFolder.
    /// </summary>
    private async Task ProcessCore(
        bool indeterminate,
        string statusPrefix,
        Func<Action<PipelineProgress>, CancellationToken, Task<PipelineResult>> runPipeline,
        Func<PipelineResult, IReadOnlyList<AudioFileInfo>> getAudioFiles)
    {
        IsProcessing = true;
        IsProgressIndeterminate = indeterminate;
        _cts = new CancellationTokenSource();
        HasErrors = false;

        try
        {
            StatusText = statusPrefix;
            ProgressValue = 0;

            long lastProgressUpdate = 0;
            var result = await Task.Run(() =>
                runPipeline(p =>
                {
                    long now = Environment.TickCount64;
                    bool isComplete = (p.ProcessedFiles + p.FailedFiles) >= p.TotalFiles;
                    if (!isComplete && (now - Interlocked.Read(ref lastProgressUpdate)) < 100)
                        return;
                    Interlocked.Exchange(ref lastProgressUpdate, now);

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (IsProgressIndeterminate && p.TotalFiles > 3)
                            IsProgressIndeterminate = false;
                        ProgressMaximum = p.TotalFiles;
                        ProgressValue = p.ProcessedFiles + p.FailedFiles;
                        CurrentFile = p.CurrentFile ?? "";
                        StatusText = $"Processing {p.ProcessedFiles + p.FailedFiles} of {p.TotalFiles}...";
                    }, Avalonia.Threading.DispatcherPriority.Send);
                }, _cts.Token));

            AddPipelineResultsToDb(result, getAudioFiles(result));
            LoadLibrary();

            StatusText = $"Completed: {result.SuccessCount} tracks in {result.Elapsed.TotalSeconds:F1}s";
            if (result.FailureCount > 0)
            {
                StatusText += $" ({result.FailureCount} failed)";
                _lastErrors = result.Errors;
                HasErrors = true;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled by user";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            CurrentFile = "";
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void AddPipelineResultsToDb(PipelineResult result, IReadOnlyList<AudioFileInfo> audioFiles)
    {
        if (_database == null) return;

        // Build lookup from processed metadata
        var metadataByPath = new Dictionary<string, ProcessedFileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var pf in result.ProcessedFiles)
            metadataByPath.TryAdd(pf.AudioFilePath, pf);

        foreach (var audioFile in audioFiles)
        {
            if (_database.ExistsByAudioPath(audioFile.FilePath)) continue;

            // Try to find processed metadata for this file
            metadataByPath.TryGetValue(audioFile.FilePath, out var meta);
            string imagePath = meta?.ImagePath
                ?? ""; // fallback — shouldn't happen for successful files
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) continue;

            var record = new SpectrogramRecord
            {
                AudioFilePath = audioFile.FilePath,
                AudioFileName = Path.GetFileNameWithoutExtension(audioFile.FileName),
                ImagePath = imagePath,
                FileSizeBytes = audioFile.FileSize,
                DurationSeconds = meta?.DurationSeconds ?? 0,
                SampleRate = meta?.SampleRate ?? 0,
                CreatedAt = DateTime.UtcNow,
                ThumbnailData = meta?.ThumbnailData
            };
            _database.Add(record);
        }
    }

    private async Task OpenSettings()
    {
        if (SettingsOpener != null)
            await SettingsOpener();
    }

    private async Task OpenAbout()
    {
        if (AboutOpener != null)
            await AboutOpener();
    }

    /// <summary>
    /// Initialize library with the given database instance. Used by DI container.
    /// </summary>
    public void SetDatabase(ISpectrogramDatabase database)
    {
        _database = database;
    }

    private async void InitializeLibrary()
    {
        try
        {
            if (_database == null)
            {
                // Fallback: create database from settings if not injected
                var settings = _settingsService.Load();
                if (string.IsNullOrEmpty(settings.DatabasePath))
                {
                    settings.DatabasePath = Path.Combine(_settingsService.GetDefaultLibraryPath(), "audio2image.db");
                }
                _database = new Core.Storage.SpectrogramDatabase(settings.DatabasePath);
            }

            // Backfill thumbnails for existing records that don't have them yet
            await BackfillThumbnailsAsync();

            LoadLibrary();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load library: {ex.Message}";
        }
    }

    /// <summary>
    /// Generate and store JPEG thumbnails for legacy records that were added before thumbnail support.
    /// Runs once per DB, then all future loads are instant from BLOB.
    /// </summary>
    private async System.Threading.Tasks.Task BackfillThumbnailsAsync()
    {
        if (_database == null) return;

        var missing = _database.GetRecordsWithoutThumbnail();
        if (missing.Count == 0) return;

        StatusText = $"Generating thumbnails for {missing.Count} existing records...";

        await System.Threading.Tasks.Task.Run(() =>
        {
            Parallel.ForEach(missing,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                entry =>
            {
                var bytes = Core.Rendering.ThumbnailGenerator.FromFile(entry.ImagePath);
                if (bytes != null)
                {
                    _database.SaveThumbnail(entry.Id, bytes);
                }
            });
        });
    }

    private async void LoadLibrary()
    {
        DisposeSpectrogramItems();
        SpectrogramItems.Clear();

        // Move DB query + file existence checks to background thread
        var (items, totalDuration) = await System.Threading.Tasks.Task.Run(() =>
        {
            var records = string.IsNullOrWhiteSpace(_searchText)
                ? _database?.GetAll() ?? []
                : _database?.Search(_searchText) ?? [];

            // Sort records based on selected sort order
            IEnumerable<SpectrogramRecord> sorted = _sortIndex switch
            {
                1 => records.OrderByDescending(r => r.AudioFileName),
                2 => records.OrderByDescending(r => r.CreatedAt),
                3 => records.OrderByDescending(r => r.DurationSeconds),
                4 => records.OrderByDescending(r => r.Rating).ThenBy(r => r.AudioFileName),
                _ => records.OrderBy(r => r.AudioFileName)
            };

            var resultItems = new List<SpectrogramItem>();
            double duration = 0;

            foreach (var record in sorted)
            {
                if (File.Exists(record.ImagePath))
                {
                    resultItems.Add(SpectrogramItem.FromRecord(record));
                    duration += record.DurationSeconds;
                }
            }

            return (resultItems, duration);
        });

        // Batch-add all items at once on UI thread
        foreach (var item in items)
        {
            SpectrogramItems.Add(item);
        }

        LoadUserTagsForItems();
        IsLibraryEmpty = SpectrogramItems.Count == 0;

        if (SpectrogramItems.Count > 0)
        {
            var countLabel = string.IsNullOrWhiteSpace(_searchText)
                ? $"{SpectrogramItems.Count} tracks"
                : $"{SpectrogramItems.Count} results for \"{_searchText}\"";
            StatusText = $"{countLabel} | {FormatTotalDuration(totalDuration)}";
        }
        else if (!string.IsNullOrWhiteSpace(_searchText))
        {
            StatusText = $"No results for \"{_searchText}\"";
        }
        else
        {
            StatusText = "Library is empty. Open a folder or files to generate spectrograms.";
        }
    }

    private void FilterLibrary()
    {
        LoadLibrary();
    }

    private async Task DeleteItem(SpectrogramItem item)
    {
        if (ConfirmAction != null)
        {
            bool confirmed = await ConfirmAction("Delete", $"Delete \"{item.AudioFileName}\" from library?\nThe spectrogram PNG will also be deleted.");
            if (!confirmed) return;
        }

        if (item.RecordId > 0 && _database != null)
        {
            _database.Delete(item.RecordId);
            try { if (File.Exists(item.ImagePath)) File.Delete(item.ImagePath); } catch { }
        }
        SpectrogramItems.Remove(item);
        item.Dispose();
        IsLibraryEmpty = SpectrogramItems.Count == 0;
        StatusText = SpectrogramItems.Count > 0
            ? $"{SpectrogramItems.Count} tracks"
            : "Library is empty. Open a folder or files to generate spectrograms.";
    }

    private void CancelProcessing()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Handle files dropped onto the window via drag-and-drop.
    /// </summary>
    public async void HandleDroppedFiles(IReadOnlyList<string> filePaths)
    {
        if (IsProcessing || filePaths.Count == 0) return;
        await ProcessFiles(filePaths);
    }

    /// <summary>
    /// Handle a folder dropped onto the window via drag-and-drop.
    /// </summary>
    public async void HandleDroppedFolder(string folderPath)
    {
        if (IsProcessing || string.IsNullOrEmpty(folderPath)) return;
        await ProcessFolder(folderPath);
    }

    // ─── Multi-select ─────────────────────────────────────────────────────

    /// <summary>
    /// Handle gallery item click with modifier support.
    /// Ctrl+Click = toggle selection, Shift+Click = range select, normal = open viewer.
    /// </summary>
    public void HandleItemClick(SpectrogramItem item, bool ctrlHeld, bool shiftHeld)
    {
        if (ctrlHeld)
        {
            ToggleItemSelection(item);
        }
        else if (shiftHeld)
        {
            RangeSelect(item);
        }
        else
        {
            // Normal click: if items are selected, deselect all first
            if (IsMultiSelectMode)
            {
                DeselectAll();
            }
            OpenViewer(item);
        }
    }

    /// <summary>Toggle selection on an item (called from View on Ctrl+Click).</summary>
    public void ToggleItemSelection(SpectrogramItem item)
    {
        item.IsSelected = !item.IsSelected;
        UpdateSelectionCount();
    }

    /// <summary>Range-select from last selected to target (called on Shift+Click).</summary>
    public void RangeSelect(SpectrogramItem target)
    {
        int targetIndex = SpectrogramItems.IndexOf(target);
        if (targetIndex < 0) return;

        // Find last selected item index
        int lastSelectedIndex = -1;
        for (int i = SpectrogramItems.Count - 1; i >= 0; i--)
        {
            if (SpectrogramItems[i].IsSelected && SpectrogramItems[i] != target)
            {
                lastSelectedIndex = i;
                break;
            }
        }

        if (lastSelectedIndex < 0)
        {
            target.IsSelected = true;
            UpdateSelectionCount();
            return;
        }

        int from = Math.Min(lastSelectedIndex, targetIndex);
        int to = Math.Max(lastSelectedIndex, targetIndex);

        for (int i = from; i <= to; i++)
            SpectrogramItems[i].IsSelected = true;

        UpdateSelectionCount();
    }

    private void SelectAll()
    {
        foreach (var item in SpectrogramItems)
            item.IsSelected = true;
        UpdateSelectionCount();
    }

    private void DeselectAll()
    {
        foreach (var item in SpectrogramItems)
            item.IsSelected = false;
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        SelectedCount = SpectrogramItems.Count(i => i.IsSelected);
        IsMultiSelectMode = SelectedCount > 0;
    }

    private async Task BatchDelete()
    {
        var selected = SpectrogramItems.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        if (ConfirmAction != null)
        {
            bool confirmed = await ConfirmAction("Delete",
                $"Delete {selected.Count} items from library?\nSpectrogram PNGs will also be deleted.");
            if (!confirmed) return;
        }

        foreach (var item in selected)
        {
            if (item.RecordId > 0 && _database != null)
            {
                _database.Delete(item.RecordId);
                try { if (File.Exists(item.ImagePath)) File.Delete(item.ImagePath); } catch { }
            }
            SpectrogramItems.Remove(item);
            item.Dispose();
        }

        IsLibraryEmpty = SpectrogramItems.Count == 0;
        SelectedCount = 0;
        IsMultiSelectMode = false;
        StatusText = SpectrogramItems.Count > 0
            ? $"{SpectrogramItems.Count} tracks"
            : "Library is empty. Open a folder or files to generate spectrograms.";
    }

    private async Task BatchAddToPlaylist()
    {
        if (_playlistService == null) return;
        var selected = SpectrogramItems.Where(i => i.IsSelected && i.RecordId > 0).ToList();
        if (selected.Count == 0) return;

        var playlists = _playlistService.GetAllPlaylists();
        if (playlists.Count == 0)
        {
            if (InputDialog == null) return;
            var name = await InputDialog("New Playlist", "No playlists yet. Enter name:");
            if (string.IsNullOrWhiteSpace(name)) return;
            var pl = _playlistService.CreatePlaylist(name.Trim());
            _playlistService.AddToPlaylist(pl.Id, selected.Select(i => i.RecordId));
            LoadPlaylists();
            StatusText = $"Added {selected.Count} tracks to \"{pl.Name}\"";
        }
        else
        {
            _playlistService.AddToPlaylist(playlists[0].Id, selected.Select(i => i.RecordId));
            StatusText = $"Added {selected.Count} tracks to \"{playlists[0].Name}\"";
        }
    }

    private async Task BatchAssignTag()
    {
        if (_userTagService == null || InputDialog == null) return;
        var selected = SpectrogramItems.Where(i => i.IsSelected && i.RecordId > 0).ToList();
        if (selected.Count == 0) return;

        var allTags = _userTagService.GetAllTags();
        var prompt = allTags.Count > 0
            ? $"Available: {string.Join(", ", allTags.Select(t => t.Name))}\nEnter tag name:"
            : "Enter new tag name:";
        var input = await InputDialog("Assign Tag to Selection", prompt);
        if (string.IsNullOrWhiteSpace(input)) return;

        var tagName = input.Trim();
        var tag = allTags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (tag == null)
        {
            try
            {
                tag = _userTagService.CreateTag(tagName);
                LoadUserTags();
            }
            catch { StatusText = $"Failed to create tag \"{tagName}\""; return; }
        }

        foreach (var item in selected)
        {
            _userTagService.AssignTag(item.RecordId, tag.Id);
            item.UserTags = _userTagService.GetTagsForSpectrogram(item.RecordId);
        }

        StatusText = $"Tagged {selected.Count} tracks with \"{tag.Name}\"";
    }

    // ─── User Tags ────────────────────────────────────────────────────────

    public void SetUserTagService(IUserTagService userTagService)
    {
        _userTagService = userTagService;
        LoadUserTags();
    }

    private void LoadUserTags()
    {
        UserTags.Clear();
        if (_userTagService == null) return;
        foreach (var tag in _userTagService.GetAllTags())
            UserTags.Add(tag);
    }

    private void LoadUserTagsForItems()
    {
        if (_userTagService == null) return;
        foreach (var item in SpectrogramItems)
        {
            if (item.RecordId > 0)
                item.UserTags = _userTagService.GetTagsForSpectrogram(item.RecordId);
        }
    }

    private async Task CreateUserTag()
    {
        if (_userTagService == null || InputDialog == null) return;

        var name = await InputDialog("New Tag", "Enter tag name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            _userTagService.CreateTag(name.Trim());
            LoadUserTags();
        }
        catch
        {
            StatusText = $"Tag \"{name.Trim()}\" already exists";
        }
    }

    private async Task AssignUserTag(SpectrogramItem item)
    {
        if (_userTagService == null || item.RecordId <= 0 || InputDialog == null) return;

        var allTags = _userTagService.GetAllTags();
        if (allTags.Count == 0)
        {
            var name = await InputDialog("New Tag", "No tags yet. Enter name for new tag:");
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var newTag = _userTagService.CreateTag(name.Trim());
                _userTagService.AssignTag(item.RecordId, newTag.Id);
                item.UserTags = _userTagService.GetTagsForSpectrogram(item.RecordId);
                LoadUserTags();
                StatusText = $"Tagged \"{item.AudioFileName}\" with \"{newTag.Name}\"";
            }
            catch { StatusText = $"Tag \"{name.Trim()}\" already exists"; }
            return;
        }

        // Show tag list as text, let user pick by name
        var tagNames = string.Join(", ", allTags.Select(t => t.Name));
        var input = await InputDialog("Assign Tag", $"Available: {tagNames}\nEnter tag name (or new name):");
        if (string.IsNullOrWhiteSpace(input)) return;

        var tagName = input.Trim();
        var existingTag = allTags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));

        if (existingTag == null)
        {
            try
            {
                existingTag = _userTagService.CreateTag(tagName);
                LoadUserTags();
            }
            catch { StatusText = $"Failed to create tag \"{tagName}\""; return; }
        }

        _userTagService.AssignTag(item.RecordId, existingTag.Id);
        item.UserTags = _userTagService.GetTagsForSpectrogram(item.RecordId);
        StatusText = $"Tagged \"{item.AudioFileName}\" with \"{existingTag.Name}\"";
    }

    private void FilterByUserTag(UserTag tag)
    {
        if (_userTagService == null || _database == null) return;

        IsSimilarityMode = false;
        IsTagFilterMode = false;
        IsPlaylistMode = false;
        IsUserTagFilterMode = true;
        UserTagFilterName = tag.Name;

        var ids = new HashSet<long>(_userTagService.GetSpectrogramIdsByTag(tag.Id));
        var records = _database.GetAll().Where(r => ids.Contains(r.Id)).ToList();

        DisposeSpectrogramItems();
        SpectrogramItems.Clear();

        foreach (var record in records)
        {
            if (File.Exists(record.ImagePath))
                SpectrogramItems.Add(SpectrogramItem.FromRecord(record));
        }

        LoadUserTagsForItems();
        IsLibraryEmpty = SpectrogramItems.Count == 0;
        StatusText = $"Tag \"{tag.Name}\": {SpectrogramItems.Count} tracks";
    }

    // ─── Rating ──────────────────────────────────────────────────────────

    private void SetRating(SpectrogramItem item, int rating)
    {
        // Toggle: if clicking same rating, clear it
        int newRating = item.Rating == rating ? 0 : rating;
        item.Rating = newRating;
        if (item.RecordId > 0)
            _database?.UpdateRating(item.RecordId, newRating);
    }

    // ─── Playlists ──────────────────────────────────────────────────────

    /// <summary>Set the playlist service instance (from DI).</summary>
    public void SetPlaylistService(IPlaylistService playlistService)
    {
        _playlistService = playlistService;
        LoadPlaylists();
    }

    private void LoadPlaylists()
    {
        Playlists.Clear();
        if (_playlistService == null) return;
        foreach (var pl in _playlistService.GetAllPlaylists())
            Playlists.Add(pl);
    }

    private void TogglePlaylistPanel()
    {
        IsPlaylistPanelOpen = !IsPlaylistPanelOpen;
        if (IsPlaylistPanelOpen)
            LoadPlaylists();
    }

    private void ShowPlaylist(Playlist playlist)
    {
        if (_playlistService == null) return;

        _activePlaylistId = playlist.Id;
        ActivePlaylistName = playlist.Name;
        IsPlaylistMode = true;
        IsSimilarityMode = false;
        IsTagFilterMode = false;

        DisposeSpectrogramItems();
        SpectrogramItems.Clear();

        var records = _playlistService.GetPlaylistRecords(playlist.Id);
        double totalDuration = 0;
        foreach (var record in records)
        {
            if (File.Exists(record.ImagePath))
            {
                SpectrogramItems.Add(SpectrogramItem.FromRecord(record));
                totalDuration += record.DurationSeconds;
            }
        }

        IsLibraryEmpty = SpectrogramItems.Count == 0;
        StatusText = $"Playlist \"{playlist.Name}\" — {SpectrogramItems.Count} tracks | {FormatTotalDuration(totalDuration)}";
    }

    private async Task CreatePlaylist()
    {
        if (_playlistService == null || InputDialog == null) return;

        var name = await InputDialog("New Playlist", "Enter playlist name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        var playlist = _playlistService.CreatePlaylist(name.Trim());
        LoadPlaylists();
    }

    private async Task DeletePlaylist(Playlist playlist)
    {
        if (_playlistService == null) return;

        if (ConfirmAction != null)
        {
            bool confirmed = await ConfirmAction("Delete", $"Delete playlist \"{playlist.Name}\"?");
            if (!confirmed) return;
        }

        _playlistService.DeletePlaylist(playlist.Id);
        LoadPlaylists();

        // If we were viewing this playlist, go back to library
        if (IsPlaylistMode && _activePlaylistId == playlist.Id)
            BackToLibrary();
    }

    private async Task RenamePlaylist(Playlist playlist)
    {
        if (_playlistService == null || InputDialog == null) return;

        var newName = await InputDialog("Rename Playlist", playlist.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == playlist.Name) return;

        _playlistService.RenamePlaylist(playlist.Id, newName.Trim());
        LoadPlaylists();

        if (IsPlaylistMode && _activePlaylistId == playlist.Id)
            ActivePlaylistName = newName.Trim();
    }

    private async Task AddToPlaylist(SpectrogramItem item)
    {
        if (_playlistService == null || item.RecordId <= 0) return;

        var playlists = _playlistService.GetAllPlaylists();
        if (playlists.Count == 0)
        {
            // No playlists yet — create one
            if (InputDialog == null) return;
            var name = await InputDialog("New Playlist", "No playlists yet. Enter name for new playlist:");
            if (string.IsNullOrWhiteSpace(name)) return;
            var pl = _playlistService.CreatePlaylist(name.Trim());
            _playlistService.AddToPlaylist(pl.Id, item.RecordId);
            LoadPlaylists();
            StatusText = $"Added to \"{pl.Name}\"";
        }
        else if (playlists.Count == 1)
        {
            // Only one playlist — add directly
            _playlistService.AddToPlaylist(playlists[0].Id, item.RecordId);
            StatusText = $"Added to \"{playlists[0].Name}\"";
        }
        else
        {
            // Multiple playlists — add to first for now (TODO: picker dialog)
            // For simplicity, add to the most recently updated playlist
            _playlistService.AddToPlaylist(playlists[0].Id, item.RecordId);
            StatusText = $"Added to \"{playlists[0].Name}\"";
        }
    }

    private async Task SaveFilterAsPlaylist()
    {
        if (_playlistService == null || InputDialog == null) return;
        if (SpectrogramItems.Count == 0) return;

        string defaultName = IsTagFilterMode ? $"Tag: {TagFilterName}"
            : IsSimilarityMode ? $"Similar to {SimilaritySourceName}"
            : "New Playlist";

        var name = await InputDialog("Save as Playlist", defaultName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var playlist = _playlistService.CreatePlaylist(name.Trim());
        var ids = SpectrogramItems.Where(i => i.RecordId > 0).Select(i => i.RecordId);
        _playlistService.AddToPlaylist(playlist.Id, ids);
        LoadPlaylists();
        StatusText = $"Saved as playlist \"{name.Trim()}\" ({SpectrogramItems.Count} tracks)";
    }

    private async Task RemoveFromPlaylist(SpectrogramItem item)
    {
        if (_playlistService == null || !IsPlaylistMode || item.RecordId <= 0) return;

        _playlistService.RemoveFromPlaylist(_activePlaylistId, item.RecordId);
        SpectrogramItems.Remove(item);
        item.Dispose();
        IsLibraryEmpty = SpectrogramItems.Count == 0;
        StatusText = $"Playlist \"{ActivePlaylistName}\" — {SpectrogramItems.Count} tracks";
    }

    private async Task ExportPlaylistM3u()
    {
        if (SpectrogramItems.Count == 0 || FileSavePicker == null) return;

        var defaultName = IsPlaylistMode ? ActivePlaylistName
            : IsTagFilterMode ? TagFilterName
            : IsUserTagFilterMode ? UserTagFilterName
            : IsSimilarityMode ? $"Similar to {SimilaritySourceName}"
            : "playlist";

        // Sanitize filename — remove invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        defaultName = new string(defaultName.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        var path = await FileSavePicker(defaultName, "m3u");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
            await writer.WriteLineAsync("#EXTM3U");

            foreach (var item in SpectrogramItems)
            {
                var duration = (int)item.DurationSeconds;
                await writer.WriteLineAsync($"#EXTINF:{duration},{item.AudioFileName}");
                await writer.WriteLineAsync(item.AudioFilePath);
            }

            StatusText = $"Exported {SpectrogramItems.Count} tracks to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    // ─── Embedding / Find Similar ────────────────────────────────────────

    /// <summary>Set the embedding service instance (from DI).</summary>
    public void SetEmbeddingService(IAudioEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// Try to load the ONNX model and start background embedding computation.
    /// Called after InitializeLibrary.
    /// </summary>
    public void InitEmbeddingService()
    {
        if (_embeddingService == null || _database == null) return;

        var settings = LoadSettings();
        var libPath = string.IsNullOrEmpty(settings.LibraryPath)
            ? _settingsService.GetDefaultLibraryPath()
            : settings.LibraryPath;
        var modelPath = string.IsNullOrEmpty(settings.ModelPath)
            ? ModelDownloader.GetDefaultModelPath(libPath)
            : settings.ModelPath;

        if (!ModelDownloader.IsModelDownloaded(modelPath)) return; // Model not downloaded yet — no background work

        try
        {
            _embeddingService.LoadModel(modelPath);
        }
        catch (Exception ex)
        {
            EmbeddingStatusText = $"AI model load failed: {ex.Message}";
            IsComputingEmbeddings = true; // make it visible
            return;
        }

        // Reset old embeddings if model version changed (v1 used mel input, v2 uses raw waveform)
        try
        {
            if (_database.HasStaleEmbeddings(_embeddingService.ModelName))
            {
                _database.ResetAllEmbeddings();
                _embeddingCache?.Clear();
            }
        }
        catch { }

        StartBackgroundEmbeddings();
    }

    /// <summary>
    /// Start background task to compute embeddings for records that don't have them yet.
    /// </summary>
    private void StartBackgroundEmbeddings()
    {
        if (_embeddingService == null || !_embeddingService.IsModelAvailable || _database == null)
            return;

        _embeddingCts?.Cancel();
        _embeddingCts = new CancellationTokenSource();
        var ct = _embeddingCts.Token;

        Task.Run(async () =>
        {
            try
            {
                var records = _database.GetRecordsWithoutEmbedding();
                if (records.Count == 0) return;

                int total = records.Count;
                int done = 0;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsComputingEmbeddings = true;
                    EmbeddingStatusText = $"AI: Computing embeddings (0/{total}...)";
                });

                foreach (var (id, audioPath) in records)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!File.Exists(audioPath))
                    {
                        done++;
                        continue;
                    }

                    try
                    {
                        var (embedding, tags) = _embeddingService.ComputeEmbeddingAndTags(audioPath);
                        var tagsStr = tags.Count > 0
                            ? string.Join(",", tags.Select(t => $"{t.Label}:{t.Probability:F2}"))
                            : null;
                        if (tagsStr != null)
                            _database.SaveEmbeddingAndTags(id, embedding, _embeddingService.ModelName, tagsStr);
                        else
                            _database.SaveEmbedding(id, embedding, _embeddingService.ModelName);
                    }
                    catch (Exception ex)
                    {
                        // Show first failure in status for debugging
                        if (done == 0)
                        {
                            var msg = ex.Message;
                            await Dispatcher.UIThread.InvokeAsync(() =>
                                EmbeddingStatusText = $"AI error: {msg}");
                        }
                    }

                    done++;
                    if (done % 5 == 0 || done == total)
                    {
                        int d = done;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            EmbeddingStatusText = $"AI: Computing embeddings ({d}/{total}...)");
                    }
                }

                // Refresh cache
                _embeddingCache = _database.GetAllEmbeddings();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsComputingEmbeddings = false;
                    EmbeddingStatusText = "";
                    // Refresh gallery to show newly computed tags
                    if (!IsSimilarityMode)
                        LoadLibrary();
                });
            }
            catch (OperationCanceledException) { }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsComputingEmbeddings = false;
                    EmbeddingStatusText = "";
                });
            }
        }, ct);
    }

    private async Task FindSimilar(SpectrogramItem item)
    {
        if (_database == null) return;

        // Check if model is available
        if (_embeddingService == null || !_embeddingService.IsModelAvailable)
        {
            // Prompt user to download model
            if (ConfirmAction != null)
            {
                bool confirmed = await ConfirmAction(
                    "Download",
                    "The \"Find Similar\" feature requires the PANNs CNN14 AI model (~320 MB).\n\nDownload it now?");
                if (!confirmed) return;
            }

            await DownloadModel();

            if (_embeddingService == null || !_embeddingService.IsModelAvailable)
                return;
        }

        // Get or compute embedding for this item
        float[]? embedding = null;

        if (item.RecordId > 0)
            embedding = _database.GetEmbedding(item.RecordId);

        if (embedding == null)
        {
            // Compute on-the-fly
            if (!File.Exists(item.AudioFilePath))
            {
                StatusText = "Audio file not found.";
                return;
            }

            StatusText = "Computing embedding...";
            try
            {
                embedding = await Task.Run(() => _embeddingService.ComputeEmbedding(item.AudioFilePath));
                if (item.RecordId > 0)
                    _database.SaveEmbedding(item.RecordId, embedding, _embeddingService.ModelName);
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to compute embedding: {ex.Message}";
                return;
            }
        }

        // Load all embeddings if not cached
        _embeddingCache ??= _database.GetAllEmbeddings();

        // Find similar
        var similar = SimilaritySearch.FindSimilar(embedding, _embeddingCache, topN: 20, excludeId: item.RecordId);

        if (similar.Count == 0)
        {
            StatusText = "No similar tracks found. Add more tracks to the library.";
            return;
        }

        // Build similarity score lookup
        var scoreById = similar.ToDictionary(s => s.Id, s => s.Score);
        var similarIds = new HashSet<long>(similar.Select(s => s.Id));

        // Get records for similar IDs
        var allRecords = _database.GetAll();
        var similarRecords = allRecords
            .Where(r => similarIds.Contains(r.Id))
            .OrderByDescending(r => scoreById.GetValueOrDefault(r.Id, 0f))
            .ToList();

        // Switch to similarity mode
        DisposeSpectrogramItems();
        SpectrogramItems.Clear();

        foreach (var record in similarRecords)
        {
            if (!File.Exists(record.ImagePath)) continue;

            float score = scoreById.GetValueOrDefault(record.Id, 0f);
            SpectrogramItems.Add(new SpectrogramItem
            {
                RecordId = record.Id,
                AudioFilePath = record.AudioFilePath,
                AudioFileName = record.AudioFileName,
                ImagePath = record.ImagePath,
                DurationSeconds = record.DurationSeconds,
                SampleRate = record.SampleRate,
                FileSizeBytes = record.FileSizeBytes,
                Tags = record.Tags,
                ThumbnailData = record.ThumbnailData,
                SimilarityScore = score
            });
        }

        IsSimilarityMode = true;
        SimilaritySourceName = item.AudioFileName;
        IsLibraryEmpty = SpectrogramItems.Count == 0;
        StatusText = $"Similar to \"{item.AudioFileName}\" — {SpectrogramItems.Count} results";
    }

    private void BackToLibrary()
    {
        IsSimilarityMode = false;
        SimilaritySourceName = "";
        IsTagFilterMode = false;
        TagFilterName = "";
        IsUserTagFilterMode = false;
        UserTagFilterName = "";
        IsPlaylistMode = false;
        ActivePlaylistName = "";
        _activePlaylistId = 0;
        LoadLibrary();
    }

    private void FilterByTag(string tagName)
    {
        if (string.IsNullOrEmpty(tagName) || _database == null) return;

        // Exit similarity mode if active
        IsSimilarityMode = false;
        SimilaritySourceName = "";

        // Enter tag filter mode
        IsTagFilterMode = true;
        TagFilterName = tagName;

        // Load all records and filter by tag
        DisposeSpectrogramItems();
        SpectrogramItems.Clear();

        var records = _database.GetAll();
        var matching = records
            .Where(r => !string.IsNullOrEmpty(r.Tags) && r.Tags.Split(',')
                .Any(t => t.Split(':')[0].Equals(tagName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var record in matching)
        {
            if (File.Exists(record.ImagePath))
            {
                SpectrogramItems.Add(SpectrogramItem.FromRecord(record));
            }
        }

        IsLibraryEmpty = SpectrogramItems.Count == 0;
        StatusText = $"Tag \"{tagName}\": {SpectrogramItems.Count} tracks";
    }

    private async Task DownloadModel()
    {
        if (_embeddingService == null) return;

        var settings = LoadSettings();
        var libPath = string.IsNullOrEmpty(settings.LibraryPath)
            ? _settingsService.GetDefaultLibraryPath()
            : settings.LibraryPath;
        var modelPath = ModelDownloader.GetDefaultModelPath(libPath);

        IsDownloadingModel = true;
        StatusText = "Downloading AI model...";

        try
        {
            await ModelDownloader.DownloadModelAsync(
                modelPath,
                onProgress: (downloaded, total) =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (total > 0)
                        {
                            double pct = (double)downloaded / total * 100;
                            double mb = downloaded / 1_048_576.0;
                            double totalMb = total / 1_048_576.0;
                            StatusText = $"Downloading AI model: {mb:F0}/{totalMb:F0} MB ({pct:F0}%)";
                        }
                        else
                        {
                            double mb = downloaded / 1_048_576.0;
                            StatusText = $"Downloading AI model: {mb:F0} MB...";
                        }
                    });
                });

            StatusText = "AI model downloaded. Loading...";
            _embeddingService.LoadModel(modelPath);
            StatusText = "AI model ready.";

            // Start background embedding computation
            StartBackgroundEmbeddings();
        }
        catch (Exception ex)
        {
            StatusText = $"Model download failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingModel = false;
        }
    }

    // ─── Dispose ──────────────────────────────────────────────────────────

    private void DisposeSpectrogramItems()
    {
        foreach (var item in SpectrogramItems)
            item.Dispose();
    }

    private static string FormatTotalDuration(double totalSeconds)
    {
        var d = TimeSpan.FromSeconds(totalSeconds);
        return d.TotalHours >= 1
            ? $"{(int)d.TotalHours}h {d.Minutes}m"
            : $"{(int)d.TotalMinutes}m {d.Seconds}s";
    }

    public void Dispose()
    {
        _embeddingCts?.Cancel();
        _embeddingCts?.Dispose();
        _embeddingCts = null;
        _embeddingService?.Dispose();
        _embeddingService = null;
        _userTagService?.Dispose();
        _userTagService = null;
        _playlistService?.Dispose();
        _playlistService = null;
        DisposeSpectrogramItems();
        SpectrogramItems.Clear();
        _database?.Dispose();
        _database = null;
        _cts?.Dispose();
        _cts = null;
    }
}
