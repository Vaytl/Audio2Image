using Microsoft.Data.Sqlite;
using Audio2Image.Core.Abstractions;
using Audio2Image.Core.Models;

namespace Audio2Image.Core.Storage;

public class SpectrogramDatabase : ISpectrogramDatabase
{
    private readonly SqliteConnection _connection;

    /// <summary>Expose the connection so PlaylistService can share it.</summary>
    public SqliteConnection Connection => _connection;

    private readonly object _lock = new();

    public SpectrogramDatabase(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        try
        {
            _connection.Open();
            Initialize();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS spectrograms (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                audio_file_path TEXT NOT NULL,
                audio_file_name TEXT NOT NULL,
                image_path TEXT NOT NULL,
                file_size_bytes INTEGER NOT NULL DEFAULT 0,
                duration_seconds REAL NOT NULL DEFAULT 0,
                sample_rate INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS idx_spectrograms_audio_name ON spectrograms(audio_file_name);
            CREATE TABLE IF NOT EXISTS _schema_version (version INTEGER NOT NULL DEFAULT 0);
            INSERT OR IGNORE INTO _schema_version (rowid, version) VALUES (1, 0);
        ";
        cmd.ExecuteNonQuery();

        RunMigrations();
    }

    private void RunMigrations()
    {
        using var versionCmd = _connection.CreateCommand();
        versionCmd.CommandText = "SELECT version FROM _schema_version WHERE rowid = 1";
        var currentVersion = Convert.ToInt32(versionCmd.ExecuteScalar()!);

        if (currentVersion < 1)
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE spectrograms ADD COLUMN embedding BLOB";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* column already exists */ }

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE spectrograms ADD COLUMN embedding_model TEXT";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* column already exists */ }

            SetSchemaVersion(1);
        }

        if (currentVersion < 2)
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE spectrograms ADD COLUMN tags TEXT";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* column already exists */ }

            SetSchemaVersion(2);
        }

        if (currentVersion < 3)
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE spectrograms ADD COLUMN thumbnail_data BLOB";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* column already exists */ }

            SetSchemaVersion(3);
        }

        if (currentVersion < 4)
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE spectrograms ADD COLUMN rating INTEGER NOT NULL DEFAULT 0";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* column already exists */ }

            SetSchemaVersion(4);
        }
    }

    private void SetSchemaVersion(int version)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE _schema_version SET version = @version WHERE rowid = 1";
        cmd.Parameters.AddWithValue("@version", version);
        cmd.ExecuteNonQuery();
    }

    public void Add(SpectrogramRecord record)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO spectrograms (audio_file_path, audio_file_name, image_path, file_size_bytes, duration_seconds, sample_rate, created_at, thumbnail_data)
                VALUES (@audioPath, @audioName, @imagePath, @fileSize, @duration, @sampleRate, @createdAt, @thumbnail)
            ";
            cmd.Parameters.AddWithValue("@audioPath", record.AudioFilePath);
            cmd.Parameters.AddWithValue("@audioName", record.AudioFileName);
            cmd.Parameters.AddWithValue("@imagePath", record.ImagePath);
            cmd.Parameters.AddWithValue("@fileSize", record.FileSizeBytes);
            cmd.Parameters.AddWithValue("@duration", record.DurationSeconds);
            cmd.Parameters.AddWithValue("@sampleRate", record.SampleRate);
            cmd.Parameters.AddWithValue("@createdAt", record.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("@thumbnail", (object?)record.ThumbnailData ?? DBNull.Value);
            cmd.ExecuteNonQuery();

            record.Id = GetLastInsertId();
        }
    }

    private long GetLastInsertId()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar()!;
    }

    public List<SpectrogramRecord> GetAll()
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, audio_file_path, audio_file_name, image_path, file_size_bytes, duration_seconds, sample_rate, created_at, tags, thumbnail_data, rating FROM spectrograms ORDER BY created_at DESC";
            return ReadRecords(cmd);
        }
    }

    public List<SpectrogramRecord> Search(string query)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, audio_file_path, audio_file_name, image_path, file_size_bytes, duration_seconds, sample_rate, created_at, tags, thumbnail_data, rating FROM spectrograms WHERE audio_file_name LIKE @query ORDER BY audio_file_name";
            cmd.Parameters.AddWithValue("@query", $"%{query}%");
            return ReadRecords(cmd);
        }
    }

    public void Delete(long id)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM spectrograms WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public int Count()
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM spectrograms";
            return Convert.ToInt32(cmd.ExecuteScalar()!);
        }
    }

    public bool ExistsByAudioPath(string audioFilePath)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM spectrograms WHERE audio_file_path = @path";
            cmd.Parameters.AddWithValue("@path", audioFilePath);
            return Convert.ToInt32(cmd.ExecuteScalar()!) > 0;
        }
    }

    private List<SpectrogramRecord> ReadRecords(SqliteCommand cmd)
    {
        var records = new List<SpectrogramRecord>();
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
                ThumbnailData = reader.IsDBNull(9) ? null : (byte[])reader[9],
                Rating = reader.IsDBNull(10) ? 0 : reader.GetInt32(10)
            });
        }
        return records;
    }

    /// <summary>Update rating for a record (0=unrated, 1-5=stars).</summary>
    public void UpdateRating(long id, int rating)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE spectrograms SET rating = @rating WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@rating", Math.Clamp(rating, 0, 5));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Save thumbnail JPEG bytes for a record.</summary>
    public void SaveThumbnail(long id, byte[] thumbnailData)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE spectrograms SET thumbnail_data = @data WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@data", thumbnailData);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Get records that have no cached thumbnail.</summary>
    public List<(long Id, string ImagePath)> GetRecordsWithoutThumbnail()
    {
        lock (_lock)
        {
            var list = new List<(long, string)>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, image_path FROM spectrograms WHERE thumbnail_data IS NULL";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add((reader.GetInt64(0), reader.GetString(1)));
            }
            return list;
        }
    }

    /// <summary>Reset all embeddings and tags (used when model input format changes).</summary>
    public void ResetAllEmbeddings()
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE spectrograms SET embedding = NULL, embedding_model = NULL, tags = NULL";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Check if any embeddings exist with a model name different from the given one.</summary>
    public bool HasStaleEmbeddings(string currentModelName)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM spectrograms WHERE embedding IS NOT NULL AND (embedding_model IS NULL OR embedding_model != @model)";
            cmd.Parameters.AddWithValue("@model", currentModelName);
            return Convert.ToInt32(cmd.ExecuteScalar()!) > 0;
        }
    }

    /// <summary>Save embedding for a record by ID.</summary>
    public void SaveEmbedding(long id, float[] embedding, string modelName)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE spectrograms SET embedding = @embedding, embedding_model = @model WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@embedding", EmbeddingToBlob(embedding));
            cmd.Parameters.AddWithValue("@model", modelName);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Save tags for a record by ID.</summary>
    public void SaveTags(long id, string tags)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE spectrograms SET tags = @tags WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@tags", tags);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Save embedding and tags together in one update.</summary>
    public void SaveEmbeddingAndTags(long id, float[] embedding, string modelName, string tags)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE spectrograms SET embedding = @embedding, embedding_model = @model, tags = @tags WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@embedding", EmbeddingToBlob(embedding));
            cmd.Parameters.AddWithValue("@model", modelName);
            cmd.Parameters.AddWithValue("@tags", tags);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Get embedding for a record by ID. Returns null if not computed.</summary>
    public float[]? GetEmbedding(long id)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT embedding FROM spectrograms WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            var result = cmd.ExecuteScalar();
            if (result is byte[] blob)
                return BlobToEmbedding(blob);
            return null;
        }
    }

    /// <summary>Get all records that have embeddings (id → embedding).</summary>
    public Dictionary<long, float[]> GetAllEmbeddings()
    {
        lock (_lock)
        {
            var dict = new Dictionary<long, float[]>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, embedding FROM spectrograms WHERE embedding IS NOT NULL";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                if (!reader.IsDBNull(1))
                {
                    var blob = (byte[])reader[1];
                    dict[id] = BlobToEmbedding(blob);
                }
            }
            return dict;
        }
    }

    /// <summary>Get IDs and audio file paths of records without embeddings or without tags.</summary>
    public List<(long Id, string AudioFilePath)> GetRecordsWithoutEmbedding()
    {
        lock (_lock)
        {
            var list = new List<(long, string)>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, audio_file_path FROM spectrograms WHERE embedding IS NULL OR tags IS NULL";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add((reader.GetInt64(0), reader.GetString(1)));
            }
            return list;
        }
    }

    private static byte[] EmbeddingToBlob(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BlobToEmbedding(byte[] blob)
    {
        var embedding = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, embedding, 0, blob.Length);
        return embedding;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
