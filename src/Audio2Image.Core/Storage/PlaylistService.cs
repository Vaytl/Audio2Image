using Microsoft.Data.Sqlite;
using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Models;

namespace Audio2Image.Core.Storage;

public class PlaylistService : IPlaylistService
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();

    public PlaylistService(SqliteConnection connection)
    {
        _connection = connection;
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS playlists (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE TABLE IF NOT EXISTS playlist_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                playlist_id INTEGER NOT NULL,
                spectrogram_id INTEGER NOT NULL,
                position INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE,
                FOREIGN KEY (spectrogram_id) REFERENCES spectrograms(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_playlist_items_playlist ON playlist_items(playlist_id, position);
        ";
        cmd.ExecuteNonQuery();
    }

    public Playlist CreatePlaylist(string name)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO playlists (name, created_at, updated_at) VALUES (@name, @now, @now)";
            var now = DateTime.UtcNow.ToString("o");
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();

            using var idCmd = _connection.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid()";
            var id = (long)idCmd.ExecuteScalar()!;

            return new Playlist { Id = id, Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        }
    }

    public List<Playlist> GetAllPlaylists()
    {
        lock (_lock)
        {
            var list = new List<Playlist>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, created_at, updated_at FROM playlists ORDER BY updated_at DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Playlist
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    CreatedAt = DateTime.Parse(reader.GetString(2)),
                    UpdatedAt = DateTime.Parse(reader.GetString(3))
                });
            }
            return list;
        }
    }

    public void RenamePlaylist(long playlistId, string newName)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE playlists SET name = @name, updated_at = @now WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", newName);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@id", playlistId);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeletePlaylist(long playlistId)
    {
        lock (_lock)
        {
            using var delItems = _connection.CreateCommand();
            delItems.CommandText = "DELETE FROM playlist_items WHERE playlist_id = @id";
            delItems.Parameters.AddWithValue("@id", playlistId);
            delItems.ExecuteNonQuery();

            using var delPl = _connection.CreateCommand();
            delPl.CommandText = "DELETE FROM playlists WHERE id = @id";
            delPl.Parameters.AddWithValue("@id", playlistId);
            delPl.ExecuteNonQuery();
        }
    }

    public List<PlaylistItem> GetPlaylistItems(long playlistId)
    {
        lock (_lock)
        {
            return GetPlaylistItemsInternal(playlistId);
        }
    }

    private List<PlaylistItem> GetPlaylistItemsInternal(long playlistId)
    {
        var list = new List<PlaylistItem>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, playlist_id, spectrogram_id, position FROM playlist_items WHERE playlist_id = @pid ORDER BY position";
        cmd.Parameters.AddWithValue("@pid", playlistId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PlaylistItem
            {
                Id = reader.GetInt64(0),
                PlaylistId = reader.GetInt64(1),
                SpectrogramId = reader.GetInt64(2),
                Position = reader.GetInt32(3)
            });
        }
        return list;
    }

    public void AddToPlaylist(long playlistId, long spectrogramId)
    {
        lock (_lock)
        {
            int maxPos = GetMaxPositionInternal(playlistId);

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT INTO playlist_items (playlist_id, spectrogram_id, position) VALUES (@pid, @sid, @pos)";
            cmd.Parameters.AddWithValue("@pid", playlistId);
            cmd.Parameters.AddWithValue("@sid", spectrogramId);
            cmd.Parameters.AddWithValue("@pos", maxPos + 1);
            cmd.ExecuteNonQuery();

            TouchPlaylistInternal(playlistId);
        }
    }

    public void AddToPlaylist(long playlistId, IEnumerable<long> spectrogramIds)
    {
        lock (_lock)
        {
            int pos = GetMaxPositionInternal(playlistId) + 1;

            using var transaction = _connection.BeginTransaction();
            try
            {
                foreach (var sid in spectrogramIds)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO playlist_items (playlist_id, spectrogram_id, position) VALUES (@pid, @sid, @pos)";
                    cmd.Parameters.AddWithValue("@pid", playlistId);
                    cmd.Parameters.AddWithValue("@sid", sid);
                    cmd.Parameters.AddWithValue("@pos", pos++);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            TouchPlaylistInternal(playlistId);
        }
    }

    public void RemoveFromPlaylist(long playlistId, long spectrogramId)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM playlist_items WHERE playlist_id = @pid AND spectrogram_id = @sid";
            cmd.Parameters.AddWithValue("@pid", playlistId);
            cmd.Parameters.AddWithValue("@sid", spectrogramId);
            cmd.ExecuteNonQuery();

            RenumberPositionsInternal(playlistId);
            TouchPlaylistInternal(playlistId);
        }
    }

    public void ReorderPlaylist(long playlistId, List<long> spectrogramIdsInOrder)
    {
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();
            try
            {
                for (int i = 0; i < spectrogramIdsInOrder.Count; i++)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "UPDATE playlist_items SET position = @pos WHERE playlist_id = @pid AND spectrogram_id = @sid";
                    cmd.Parameters.AddWithValue("@pos", i);
                    cmd.Parameters.AddWithValue("@pid", playlistId);
                    cmd.Parameters.AddWithValue("@sid", spectrogramIdsInOrder[i]);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            TouchPlaylistInternal(playlistId);
        }
    }

    public int GetItemCount(long playlistId)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM playlist_items WHERE playlist_id = @pid";
            cmd.Parameters.AddWithValue("@pid", playlistId);
            return Convert.ToInt32(cmd.ExecuteScalar()!);
        }
    }

    public List<SpectrogramRecord> GetPlaylistRecords(long playlistId)
    {
        lock (_lock)
        {
            return GetPlaylistRecordsInternal(playlistId);
        }
    }

    private List<SpectrogramRecord> GetPlaylistRecordsInternal(long playlistId)
    {
        var records = new List<SpectrogramRecord>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT s.id, s.audio_file_path, s.audio_file_name, s.image_path,
                   s.file_size_bytes, s.duration_seconds, s.sample_rate, s.created_at, s.tags, s.rating, s.thumbnail_data
            FROM playlist_items pi
            JOIN spectrograms s ON pi.spectrogram_id = s.id
            WHERE pi.playlist_id = @pid
            ORDER BY pi.position";
        cmd.Parameters.AddWithValue("@pid", playlistId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new SpectrogramRecord
            {
                Id = reader.GetInt64(0),
                AudioFilePath = reader.GetString(1),
                AudioFileName = reader.GetString(2),
                ImagePath = reader.GetString(3),
                FileSizeBytes = reader.GetInt64(4),
                DurationSeconds = reader.GetDouble(5),
                SampleRate = reader.GetInt32(6),
                CreatedAt = DateTime.Parse(reader.GetString(7)),
                Tags = reader.IsDBNull(8) ? null : reader.GetString(8),
                Rating = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                ThumbnailData = reader.IsDBNull(10) ? null : (byte[])reader[10]
            });
        }
        return records;
    }

    private int GetMaxPositionInternal(long playlistId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(position), -1) FROM playlist_items WHERE playlist_id = @pid";
        cmd.Parameters.AddWithValue("@pid", playlistId);
        return Convert.ToInt32(cmd.ExecuteScalar()!);
    }

    private void RenumberPositionsInternal(long playlistId)
    {
        var items = GetPlaylistItemsInternal(playlistId);
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Position != i)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE playlist_items SET position = @pos WHERE id = @id";
                cmd.Parameters.AddWithValue("@pos", i);
                cmd.Parameters.AddWithValue("@id", items[i].Id);
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void TouchPlaylistInternal(long playlistId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET updated_at = @now WHERE id = @id";
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@id", playlistId);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        // Connection is shared with SpectrogramDatabase, don't dispose it here
    }
}
