using Audio2Image.Core.Models;
using Audio2Image.Core.Storage;

namespace Audio2Image.Core.Tests.Storage;

public class SpectrogramDatabaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SpectrogramDatabase _db;

    public SpectrogramDatabaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Audio2ImageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _db = new SpectrogramDatabase(Path.Combine(_tempDir, "test.db"));
    }

    [Fact]
    public void Add_And_GetAll_ReturnsRecord()
    {
        var record = new SpectrogramRecord
        {
            AudioFilePath = "/music/test.mp3",
            AudioFileName = "test.mp3",
            ImagePath = "/cache/test.png",
            FileSizeBytes = 1024000,
            DurationSeconds = 180.5,
            SampleRate = 44100,
            CreatedAt = DateTime.UtcNow
        };

        _db.Add(record);
        Assert.True(record.Id > 0);

        var all = _db.GetAll();
        Assert.Single(all);
        Assert.Equal("test.mp3", all[0].AudioFileName);
        Assert.Equal(44100, all[0].SampleRate);
    }

    [Fact]
    public void Search_FindsByPartialName()
    {
        _db.Add(new SpectrogramRecord { AudioFilePath = "/a.mp3", AudioFileName = "alpha song.mp3", ImagePath = "/a.png", CreatedAt = DateTime.UtcNow });
        _db.Add(new SpectrogramRecord { AudioFilePath = "/b.mp3", AudioFileName = "beta track.mp3", ImagePath = "/b.png", CreatedAt = DateTime.UtcNow });
        _db.Add(new SpectrogramRecord { AudioFilePath = "/c.mp3", AudioFileName = "gamma song.wav", ImagePath = "/c.png", CreatedAt = DateTime.UtcNow });

        var results = _db.Search("song");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Delete_RemovesRecord()
    {
        _db.Add(new SpectrogramRecord { AudioFilePath = "/x.mp3", AudioFileName = "x.mp3", ImagePath = "/x.png", CreatedAt = DateTime.UtcNow });
        var all = _db.GetAll();
        Assert.Single(all);

        _db.Delete(all[0].Id);
        Assert.Empty(_db.GetAll());
    }

    [Fact]
    public void Count_ReturnsCorrectCount()
    {
        Assert.Equal(0, _db.Count());
        _db.Add(new SpectrogramRecord { AudioFilePath = "/a.mp3", AudioFileName = "a.mp3", ImagePath = "/a.png", CreatedAt = DateTime.UtcNow });
        _db.Add(new SpectrogramRecord { AudioFilePath = "/b.mp3", AudioFileName = "b.mp3", ImagePath = "/b.png", CreatedAt = DateTime.UtcNow });
        Assert.Equal(2, _db.Count());
    }

    [Fact]
    public void ExistsByAudioPath_Works()
    {
        _db.Add(new SpectrogramRecord { AudioFilePath = "/music/test.mp3", AudioFileName = "test.mp3", ImagePath = "/cache/test.png", CreatedAt = DateTime.UtcNow });
        Assert.True(_db.ExistsByAudioPath("/music/test.mp3"));
        Assert.False(_db.ExistsByAudioPath("/music/other.mp3"));
    }

    [Fact]
    public void SaveEmbedding_And_GetEmbedding_Roundtrip()
    {
        var record = new SpectrogramRecord
        {
            AudioFilePath = "/music/emb.mp3",
            AudioFileName = "emb.mp3",
            ImagePath = "/cache/emb.png",
            CreatedAt = DateTime.UtcNow
        };
        _db.Add(record);

        float[] embedding = new float[2048];
        for (int i = 0; i < embedding.Length; i++)
            embedding[i] = i * 0.001f;

        _db.SaveEmbedding(record.Id, embedding, "panns_cnn14");

        var loaded = _db.GetEmbedding(record.Id);
        Assert.NotNull(loaded);
        Assert.Equal(2048, loaded!.Length);
        for (int i = 0; i < 2048; i++)
            Assert.Equal(embedding[i], loaded[i], 1e-6f);
    }

    [Fact]
    public void GetEmbedding_NoEmbedding_ReturnsNull()
    {
        var record = new SpectrogramRecord
        {
            AudioFilePath = "/music/noemb.mp3",
            AudioFileName = "noemb.mp3",
            ImagePath = "/cache/noemb.png",
            CreatedAt = DateTime.UtcNow
        };
        _db.Add(record);

        var result = _db.GetEmbedding(record.Id);
        Assert.Null(result);
    }

    [Fact]
    public void GetAllEmbeddings_ReturnsOnlyRecordsWithEmbeddings()
    {
        _db.Add(new SpectrogramRecord { AudioFilePath = "/a.mp3", AudioFileName = "a", ImagePath = "/a.png", CreatedAt = DateTime.UtcNow });
        _db.Add(new SpectrogramRecord { AudioFilePath = "/b.mp3", AudioFileName = "b", ImagePath = "/b.png", CreatedAt = DateTime.UtcNow });

        var all = _db.GetAll();
        _db.SaveEmbedding(all[0].Id, new float[] { 1f, 2f, 3f }, "test");

        var embeddings = _db.GetAllEmbeddings();
        Assert.Single(embeddings);
        Assert.True(embeddings.ContainsKey(all[0].Id));
    }

    [Fact]
    public void GetRecordsWithoutEmbedding_ReturnsRecordsWithoutEmbeddingOrTags()
    {
        _db.Add(new SpectrogramRecord { AudioFilePath = "/x.mp3", AudioFileName = "x", ImagePath = "/x.png", CreatedAt = DateTime.UtcNow });
        _db.Add(new SpectrogramRecord { AudioFilePath = "/y.mp3", AudioFileName = "y", ImagePath = "/y.png", CreatedAt = DateTime.UtcNow });

        var all = _db.GetAll();

        // Record with embedding but no tags — still needs reprocessing
        _db.SaveEmbedding(all[0].Id, new float[] { 1f }, "test");
        var without = _db.GetRecordsWithoutEmbedding();
        Assert.Equal(2, without.Count); // both: one has no embedding, other has no tags

        // Record with embedding AND tags — fully processed
        _db.SaveEmbeddingAndTags(all[0].Id, new float[] { 1f }, "test", "Music:0.95");
        without = _db.GetRecordsWithoutEmbedding();
        Assert.Single(without); // only the one without embedding
        Assert.Equal(all[1].Id, without[0].Id);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
