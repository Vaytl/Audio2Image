using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Audio2Image.Core.Models;

namespace Audio2Image.App.Models;

public class SpectrogramItem : IDisposable, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Limit concurrent thumbnail decodes to avoid thread pool starvation
    private static readonly SemaphoreSlim ThumbnailSemaphore = new(Math.Max(2, Environment.ProcessorCount / 2));

    /// <summary>Create from a database record.</summary>
    public static SpectrogramItem FromRecord(SpectrogramRecord record) => new()
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
        _rating = record.Rating
    };

    public long RecordId { get; init; }
    public required string AudioFilePath { get; init; }
    public required string AudioFileName { get; init; }
    public required string ImagePath { get; init; }
    public double DurationSeconds { get; init; }
    public int SampleRate { get; init; }
    public long FileSizeBytes { get; init; }

    /// <summary>JPEG thumbnail bytes from DB for instant gallery display.</summary>
    public byte[]? ThumbnailData { get; init; }

    private int _rating;
    private bool _isSelected;

    /// <summary>Whether this item is selected in multi-select mode.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>User rating (0 = unrated, 1-5 = stars).</summary>
    public int Rating
    {
        get => _rating;
        set
        {
            if (_rating == value) return;
            _rating = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RatingStars));
            OnPropertyChanged(nameof(HasRating));
        }
    }

    /// <summary>Star display string (e.g. "★★★☆☆").</summary>
    public string RatingStars
    {
        get
        {
            if (_rating <= 0) return "";
            return new string('★', _rating) + new string('☆', 5 - _rating);
        }
    }

    /// <summary>Whether this item has a rating.</summary>
    public bool HasRating => _rating > 0;

    /// <summary>Similarity score (0..1) when in Find Similar mode. -1 = not in similarity mode.</summary>
    public float SimilarityScore { get; init; } = -1f;

    /// <summary>Raw tags string from DB (e.g. "Music:0.95,Guitar:0.72,Rock:0.44").</summary>
    public string? Tags { get; init; }

    /// <summary>Whether this item has AI-generated tags.</summary>
    public bool HasTags => !string.IsNullOrEmpty(Tags);

    // Lazy-cached parsed tag labels — avoids re-parsing Tags string 3 times per item
    private List<string>? _parsedLabels;
    private List<string> ParsedLabels => _parsedLabels ??= ParseTagLabels();

    private List<string> ParseTagLabels()
    {
        if (string.IsNullOrEmpty(Tags)) return [];
        var parts = Tags.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var labels = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var colonIdx = part.LastIndexOf(':');
            labels.Add(colonIdx > 0 ? part[..colonIdx] : part);
        }
        return labels;
    }

    /// <summary>Top tags as short display text (e.g. "Music · Guitar · Rock").</summary>
    public string TagsText => ParsedLabels.Count > 0 ? string.Join(" · ", ParsedLabels) : "";

    /// <summary>Individual tag labels for pill badge display.</summary>
    public List<string> TagLabels => ParsedLabels;

    // Lazy-cached tag display items with category-based colors
    private List<TagDisplayItem>? _cachedDisplayItems;

    /// <summary>Tag display items with category-based colors for UI binding.</summary>
    public List<TagDisplayItem> TagDisplayItems =>
        _cachedDisplayItems ??= ParsedLabels.Count > 0
            ? ParsedLabels.Select(TagCategoryColors.GetDisplayItem).ToList()
            : [];

    // ─── User Tags ────────────────────────────────────────────────────

    private List<UserTag>? _userTags;

    /// <summary>User-assigned tags (loaded separately from DB).</summary>
    public List<UserTag> UserTags
    {
        get => _userTags ?? [];
        set
        {
            _userTags = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUserTags));
            OnPropertyChanged(nameof(HasAnyTags));
            _cachedUserTagDisplayItems = null;
            _cachedAllTagDisplayItems = null;
            OnPropertyChanged(nameof(UserTagDisplayItems));
            OnPropertyChanged(nameof(AllTagDisplayItems));
        }
    }

    public bool HasUserTags => _userTags is { Count: > 0 };

    private List<TagDisplayItem>? _cachedUserTagDisplayItems;

    public List<TagDisplayItem> UserTagDisplayItems =>
        _cachedUserTagDisplayItems ??= (_userTags ?? [])
            .Select(t => new TagDisplayItem(t.Name,
                new SolidColorBrush(Color.Parse(t.Color + "88")),
                new SolidColorBrush(Color.Parse(t.Color))))
            .ToList();

    /// <summary>Whether this item has any tags (AI or user).</summary>
    public bool HasAnyTags => HasTags || HasUserTags;

    private List<TagDisplayItem>? _cachedAllTagDisplayItems;

    /// <summary>Combined AI + user tags for single-row display.</summary>
    public List<TagDisplayItem> AllTagDisplayItems =>
        _cachedAllTagDisplayItems ??= BuildAllTagDisplayItems();

    private List<TagDisplayItem> BuildAllTagDisplayItems()
    {
        var items = new List<TagDisplayItem>();
        // AI tags first
        if (ParsedLabels.Count > 0)
            items.AddRange(ParsedLabels.Select(TagCategoryColors.GetDisplayItem));
        // User tags with border-style (distinguished by brighter color)
        if (_userTags is { Count: > 0 })
            items.AddRange(_userTags.Select(t => new TagDisplayItem(t.Name,
                new SolidColorBrush(Color.Parse(t.Color + "88")),
                new SolidColorBrush(Color.Parse(t.Color)))));
        return items;
    }

    /// <summary>Whether this item has a similarity score to display.</summary>
    public bool HasSimilarityScore => SimilarityScore >= 0f;

    /// <summary>Similarity percentage text (e.g. "87%").</summary>
    public string SimilarityText => SimilarityScore >= 0f ? $"{SimilarityScore * 100:F0}%" : "";

    /// <summary>
    /// File extension without dot, uppercased (e.g. "MP3", "WAV").
    /// </summary>
    public string FormatText
    {
        get
        {
            var ext = Path.GetExtension(AudioFilePath);
            return string.IsNullOrEmpty(ext) ? "" : ext.TrimStart('.').ToUpperInvariant();
        }
    }

    /// <summary>
    /// Human-readable file size (e.g. "4.2 MB", "320 KB").
    /// </summary>
    public string FileSizeText
    {
        get
        {
            if (FileSizeBytes <= 0) return "";
            if (FileSizeBytes >= 1_073_741_824)
                return $"{FileSizeBytes / 1_073_741_824.0:F1} GB";
            if (FileSizeBytes >= 1_048_576)
                return $"{FileSizeBytes / 1_048_576.0:F1} MB";
            if (FileSizeBytes >= 1024)
                return $"{FileSizeBytes / 1024.0:F0} KB";
            return $"{FileSizeBytes} B";
        }
    }

    /// <summary>
    /// Sample rate formatted (e.g. "44.1 kHz", "48 kHz").
    /// </summary>
    public string SampleRateText
    {
        get
        {
            if (SampleRate <= 0) return "";
            return SampleRate % 1000 == 0
                ? $"{SampleRate / 1000} kHz"
                : $"{SampleRate / 1000.0:F1} kHz";
        }
    }

    /// <summary>
    /// Formatted duration string (e.g. "3:42" or "1:05:30").
    /// </summary>
    public string DurationText
    {
        get
        {
            if (DurationSeconds <= 0) return "";
            var ts = TimeSpan.FromSeconds(DurationSeconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }
    }

    /// <summary>
    /// Secondary info line: "MP3 · 44.1 kHz · 4.2 MB"
    /// </summary>
    public string MetadataText
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(FormatText)) parts.Add(FormatText);
            if (!string.IsNullOrEmpty(SampleRateText)) parts.Add(SampleRateText);
            if (!string.IsNullOrEmpty(FileSizeText)) parts.Add(FileSizeText);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Metadata without format (used when format is shown as badge): "44.1 kHz · 4.2 MB"
    /// </summary>
    public string MetadataShortText
    {
        get
        {
            var parts = new List<string>(2);
            if (!string.IsNullOrEmpty(SampleRateText)) parts.Add(SampleRateText);
            if (!string.IsNullOrEmpty(FileSizeText)) parts.Add(FileSizeText);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Badge color based on audio format.
    /// MP3 = blue-ish, WAV = green-ish.
    /// </summary>
    // Cached brushes — avoid allocating SolidColorBrush on every access (hot path in virtualized ListBox)
    private static readonly SolidColorBrush WavBrush = new(Color.Parse("#2E7D32"));
    private static readonly SolidColorBrush Mp3Brush = new(Color.Parse("#1565C0"));
    private static readonly SolidColorBrush OggBrush = new(Color.Parse("#6A1B9A"));
    private static readonly SolidColorBrush DefaultFormatBrush = new(Color.Parse("#555555"));

    public IBrush FormatBadgeColor => FormatText.ToUpperInvariant() switch
    {
        "WAV" => WavBrush,
        "MP3" => Mp3Brush,
        "OGG" => OggBrush,
        _ => DefaultFormatBrush
    };

    private Bitmap? _thumbnail;
    private bool _thumbnailRequested;

    /// <summary>
    /// Thumbnail for gallery display.
    /// Fast path: decode from in-memory JPEG bytes (from DB BLOB).
    /// Fallback: decode from full PNG file on disk (for legacy records without thumbnail).
    /// </summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (_thumbnail is not null) return _thumbnail;
            if (_thumbnailRequested) return null;
            _thumbnailRequested = true;

            // Fast path: decode from cached JPEG bytes (no file I/O)
            if (ThumbnailData is { Length: > 0 })
            {
                try
                {
                    using var ms = new MemoryStream(ThumbnailData);
                    _thumbnail = new Bitmap(ms);
                    return _thumbnail;
                }
                catch
                {
                    // Fall through to file-based fallback
                }
            }

            // Slow fallback: decode from full PNG file in background (legacy records)
            _ = LoadThumbnailFromFileAsync();
            return null;
        }
    }

    private async Task LoadThumbnailFromFileAsync()
    {
        await ThumbnailSemaphore.WaitAsync();
        try
        {
            var bmp = await Task.Run(() =>
            {
                if (!File.Exists(ImagePath)) return null;
                using var stream = File.OpenRead(ImagePath);
                return Bitmap.DecodeToHeight(stream, 80);
            });

            if (bmp is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _thumbnail = bmp;
                    OnPropertyChanged(nameof(Thumbnail));
                });
            }
        }
        catch
        {
            // If image can't be loaded, leave as null
        }
        finally
        {
            ThumbnailSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _thumbnail?.Dispose();
        _thumbnail = null;
    }
}
