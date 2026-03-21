using Microsoft.Data.Sqlite;
using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Models;

namespace Audio2Image.Core.Storage;

public class UserTagService : IUserTagService
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();

    public UserTagService(SqliteConnection connection)
    {
        _connection = connection;
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS user_tags (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                color TEXT NOT NULL DEFAULT '#FF6B35'
            );
            CREATE TABLE IF NOT EXISTS spectrogram_user_tags (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                spectrogram_id INTEGER NOT NULL,
                tag_id INTEGER NOT NULL,
                FOREIGN KEY (spectrogram_id) REFERENCES spectrograms(id) ON DELETE CASCADE,
                FOREIGN KEY (tag_id) REFERENCES user_tags(id) ON DELETE CASCADE,
                UNIQUE(spectrogram_id, tag_id)
            );
            CREATE INDEX IF NOT EXISTS idx_spec_user_tags_spec ON spectrogram_user_tags(spectrogram_id);
            CREATE INDEX IF NOT EXISTS idx_spec_user_tags_tag ON spectrogram_user_tags(tag_id);
        ";
        cmd.ExecuteNonQuery();
    }

    public UserTag CreateTag(string name, string color = "#FF6B35")
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT INTO user_tags (name, color) VALUES (@name, @color)";
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@color", color);
            cmd.ExecuteNonQuery();

            using var idCmd = _connection.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid()";
            var id = (long)idCmd.ExecuteScalar()!;

            return new UserTag { Id = id, Name = name, Color = color };
        }
    }

    public List<UserTag> GetAllTags()
    {
        lock (_lock)
        {
            var list = new List<UserTag>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, color FROM user_tags ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new UserTag
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Color = reader.GetString(2)
                });
            }
            return list;
        }
    }

    public void DeleteTag(long tagId)
    {
        lock (_lock)
        {
            using var delLinks = _connection.CreateCommand();
            delLinks.CommandText = "DELETE FROM spectrogram_user_tags WHERE tag_id = @id";
            delLinks.Parameters.AddWithValue("@id", tagId);
            delLinks.ExecuteNonQuery();

            using var delTag = _connection.CreateCommand();
            delTag.CommandText = "DELETE FROM user_tags WHERE id = @id";
            delTag.Parameters.AddWithValue("@id", tagId);
            delTag.ExecuteNonQuery();
        }
    }

    public void RenameTag(long tagId, string newName)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE user_tags SET name = @name WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", newName);
            cmd.Parameters.AddWithValue("@id", tagId);
            cmd.ExecuteNonQuery();
        }
    }

    public void AssignTag(long spectrogramId, long tagId)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO spectrogram_user_tags (spectrogram_id, tag_id) VALUES (@sid, @tid)";
            cmd.Parameters.AddWithValue("@sid", spectrogramId);
            cmd.Parameters.AddWithValue("@tid", tagId);
            cmd.ExecuteNonQuery();
        }
    }

    public void RemoveTag(long spectrogramId, long tagId)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM spectrogram_user_tags WHERE spectrogram_id = @sid AND tag_id = @tid";
            cmd.Parameters.AddWithValue("@sid", spectrogramId);
            cmd.Parameters.AddWithValue("@tid", tagId);
            cmd.ExecuteNonQuery();
        }
    }

    public List<UserTag> GetTagsForSpectrogram(long spectrogramId)
    {
        lock (_lock)
        {
            var list = new List<UserTag>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT t.id, t.name, t.color
                FROM user_tags t
                JOIN spectrogram_user_tags st ON st.tag_id = t.id
                WHERE st.spectrogram_id = @sid
                ORDER BY t.name";
            cmd.Parameters.AddWithValue("@sid", spectrogramId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new UserTag
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Color = reader.GetString(2)
                });
            }
            return list;
        }
    }

    public List<long> GetSpectrogramIdsByTag(long tagId)
    {
        lock (_lock)
        {
            var ids = new List<long>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT spectrogram_id FROM spectrogram_user_tags WHERE tag_id = @tid";
            cmd.Parameters.AddWithValue("@tid", tagId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
            return ids;
        }
    }

    public void Dispose()
    {
        // Connection is shared, don't dispose
    }
}
