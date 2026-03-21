namespace Audio2Image.Core.Models;

public class SpectrogramRecord
{
    public long Id { get; set; }
    public required string AudioFilePath { get; set; }
    public required string AudioFileName { get; set; }
    public required string ImagePath { get; set; }
    public long FileSizeBytes { get; set; }
    public double DurationSeconds { get; set; }
    public int SampleRate { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Audio embedding vector for similarity search (e.g. 2048-dim from PANNs CNN14).</summary>
    public float[]? Embedding { get; set; }

    /// <summary>Model identifier used to compute the embedding (e.g. "panns_cnn14").</summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>Top AudioSet tags as comma-separated "label:prob" pairs (e.g. "Music:0.95,Guitar:0.72").</summary>
    public string? Tags { get; set; }

    /// <summary>JPEG thumbnail bytes (~80px height) for fast gallery display.</summary>
    public byte[]? ThumbnailData { get; set; }

    /// <summary>User rating (0 = unrated, 1-5 = stars).</summary>
    public int Rating { get; set; }
}
