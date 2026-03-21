using Audio2Image.Core.Models;
using Audio2Image.Core.Storage;

namespace Audio2Image.Core.Tests.Storage;

public class PlaylistServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SpectrogramDatabase _db;
    private readonly PlaylistService _service;

    public PlaylistServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Audio2ImageTests_PL_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _db = new SpectrogramDatabase(Path.Combine(_tempDir, "test.db"));
        _service = new PlaylistService(_db.Connection);
    }

    private long AddTestSpectrogram(string name = "test.mp3")
    {
        var record = new SpectrogramRecord
        {
            AudioFilePath = $"/music/{name}",
            AudioFileName = name,
            ImagePath = $"/cache/{Path.GetFileNameWithoutExtension(name)}.png",
            FileSizeBytes = 1024,
            DurationSeconds = 120,
            SampleRate = 44100,
            CreatedAt = DateTime.UtcNow
        };
        _db.Add(record);
        return record.Id;
    }

    [Fact]
    public void CreatePlaylist_ReturnsPlaylistWithId()
    {
        var playlist = _service.CreatePlaylist("My Playlist");

        Assert.True(playlist.Id > 0);
        Assert.Equal("My Playlist", playlist.Name);
    }

    [Fact]
    public void GetAllPlaylists_ReturnsCreatedPlaylists()
    {
        _service.CreatePlaylist("First");
        _service.CreatePlaylist("Second");

        var all = _service.GetAllPlaylists();

        Assert.Equal(2, all.Count);
        // Ordered by updated_at DESC, so Second first
        Assert.Contains(all, p => p.Name == "First");
        Assert.Contains(all, p => p.Name == "Second");
    }

    [Fact]
    public void GetAllPlaylists_EmptyReturnsEmpty()
    {
        var all = _service.GetAllPlaylists();
        Assert.Empty(all);
    }

    [Fact]
    public void RenamePlaylist_ChangesName()
    {
        var playlist = _service.CreatePlaylist("Old Name");
        _service.RenamePlaylist(playlist.Id, "New Name");

        var all = _service.GetAllPlaylists();
        Assert.Single(all);
        Assert.Equal("New Name", all[0].Name);
    }

    [Fact]
    public void DeletePlaylist_RemovesPlaylistAndItems()
    {
        var playlist = _service.CreatePlaylist("ToDelete");
        var specId = AddTestSpectrogram();
        _service.AddToPlaylist(playlist.Id, specId);

        _service.DeletePlaylist(playlist.Id);

        Assert.Empty(_service.GetAllPlaylists());
        Assert.Equal(0, _service.GetItemCount(playlist.Id));
    }

    [Fact]
    public void AddToPlaylist_SingleItem_Works()
    {
        var playlist = _service.CreatePlaylist("Test");
        var specId = AddTestSpectrogram("track1.mp3");

        _service.AddToPlaylist(playlist.Id, specId);

        var items = _service.GetPlaylistItems(playlist.Id);
        Assert.Single(items);
        Assert.Equal(specId, items[0].SpectrogramId);
        Assert.Equal(0, items[0].Position);
    }

    [Fact]
    public void AddToPlaylist_MultipleItems_CorrectPositions()
    {
        var playlist = _service.CreatePlaylist("Test");
        var id1 = AddTestSpectrogram("track1.mp3");
        var id2 = AddTestSpectrogram("track2.mp3");
        var id3 = AddTestSpectrogram("track3.mp3");

        _service.AddToPlaylist(playlist.Id, id1);
        _service.AddToPlaylist(playlist.Id, id2);
        _service.AddToPlaylist(playlist.Id, id3);

        var items = _service.GetPlaylistItems(playlist.Id);
        Assert.Equal(3, items.Count);
        Assert.Equal(0, items[0].Position);
        Assert.Equal(1, items[1].Position);
        Assert.Equal(2, items[2].Position);
    }

    [Fact]
    public void AddToPlaylist_Batch_Works()
    {
        var playlist = _service.CreatePlaylist("Batch");
        var id1 = AddTestSpectrogram("a.mp3");
        var id2 = AddTestSpectrogram("b.mp3");

        _service.AddToPlaylist(playlist.Id, new[] { id1, id2 });

        Assert.Equal(2, _service.GetItemCount(playlist.Id));
    }

    [Fact]
    public void RemoveFromPlaylist_RemovesAndRenumbers()
    {
        var playlist = _service.CreatePlaylist("Test");
        var id1 = AddTestSpectrogram("a.mp3");
        var id2 = AddTestSpectrogram("b.mp3");
        var id3 = AddTestSpectrogram("c.mp3");

        _service.AddToPlaylist(playlist.Id, new[] { id1, id2, id3 });
        _service.RemoveFromPlaylist(playlist.Id, id2);

        var items = _service.GetPlaylistItems(playlist.Id);
        Assert.Equal(2, items.Count);
        // Positions renumbered: 0, 1
        Assert.Equal(0, items[0].Position);
        Assert.Equal(1, items[1].Position);
        Assert.Equal(id1, items[0].SpectrogramId);
        Assert.Equal(id3, items[1].SpectrogramId);
    }

    [Fact]
    public void ReorderPlaylist_ChangesOrder()
    {
        var playlist = _service.CreatePlaylist("Test");
        var id1 = AddTestSpectrogram("a.mp3");
        var id2 = AddTestSpectrogram("b.mp3");
        var id3 = AddTestSpectrogram("c.mp3");

        _service.AddToPlaylist(playlist.Id, new[] { id1, id2, id3 });

        // Reverse order
        _service.ReorderPlaylist(playlist.Id, new List<long> { id3, id1, id2 });

        var items = _service.GetPlaylistItems(playlist.Id);
        Assert.Equal(id3, items[0].SpectrogramId);
        Assert.Equal(id1, items[1].SpectrogramId);
        Assert.Equal(id2, items[2].SpectrogramId);
    }

    [Fact]
    public void GetItemCount_ReturnsCorrectCount()
    {
        var playlist = _service.CreatePlaylist("Test");
        Assert.Equal(0, _service.GetItemCount(playlist.Id));

        var id1 = AddTestSpectrogram("a.mp3");
        _service.AddToPlaylist(playlist.Id, id1);
        Assert.Equal(1, _service.GetItemCount(playlist.Id));
    }

    [Fact]
    public void GetPlaylistRecords_ReturnsJoinedRecords()
    {
        var playlist = _service.CreatePlaylist("Test");
        var id1 = AddTestSpectrogram("track1.mp3");
        var id2 = AddTestSpectrogram("track2.wav");

        _service.AddToPlaylist(playlist.Id, new[] { id1, id2 });

        var records = _service.GetPlaylistRecords(playlist.Id);
        Assert.Equal(2, records.Count);
        Assert.Equal("track1.mp3", records[0].AudioFileName);
        Assert.Equal("track2.wav", records[1].AudioFileName);
    }

    public void Dispose()
    {
        _service.Dispose();
        _db.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
