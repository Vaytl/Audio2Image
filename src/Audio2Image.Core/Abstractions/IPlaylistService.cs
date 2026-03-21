using Audio2Image.Core.Models;

namespace Audio2Image.Core.Abstractions;

/// <summary>
/// Service for managing playlists stored in SQLite.
/// </summary>
public interface IPlaylistService : IDisposable
{
    // Playlist CRUD
    Playlist CreatePlaylist(string name);
    List<Playlist> GetAllPlaylists();
    void RenamePlaylist(long playlistId, string newName);
    void DeletePlaylist(long playlistId);

    // Playlist items
    List<PlaylistItem> GetPlaylistItems(long playlistId);
    void AddToPlaylist(long playlistId, long spectrogramId);
    void AddToPlaylist(long playlistId, IEnumerable<long> spectrogramIds);
    void RemoveFromPlaylist(long playlistId, long spectrogramId);
    void ReorderPlaylist(long playlistId, List<long> spectrogramIdsInOrder);
    int GetItemCount(long playlistId);

    /// <summary>Get spectrogram records for a playlist, in order.</summary>
    List<SpectrogramRecord> GetPlaylistRecords(long playlistId);
}
