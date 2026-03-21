namespace Audio2Image.Core.Models;

/// <summary>
/// A named playlist that contains ordered references to spectrogram records.
/// </summary>
public class Playlist
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// An item in a playlist, referencing a spectrogram record by ID.
/// </summary>
public class PlaylistItem
{
    public long Id { get; set; }
    public long PlaylistId { get; set; }
    public long SpectrogramId { get; set; }
    public int Position { get; set; }
}
