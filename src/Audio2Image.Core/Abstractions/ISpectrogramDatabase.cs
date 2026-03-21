using Audio2Image.Core.Models;

namespace Audio2Image.Core.Abstractions;

/// <summary>
/// Abstraction for the spectrogram library database.
/// </summary>
public interface ISpectrogramDatabase : IDisposable
{
    void Add(SpectrogramRecord record);
    List<SpectrogramRecord> GetAll();
    List<SpectrogramRecord> Search(string query);
    void Delete(long id);
    int Count();
    bool ExistsByAudioPath(string audioFilePath);

    // Rating
    void UpdateRating(long id, int rating);

    // Thumbnail methods
    void SaveThumbnail(long id, byte[] thumbnailData);
    List<(long Id, string ImagePath)> GetRecordsWithoutThumbnail();

    // Embedding methods for similarity search
    void ResetAllEmbeddings();
    bool HasStaleEmbeddings(string currentModelName);
    void SaveEmbedding(long id, float[] embedding, string modelName);
    void SaveTags(long id, string tags);
    void SaveEmbeddingAndTags(long id, float[] embedding, string modelName, string tags);
    float[]? GetEmbedding(long id);
    Dictionary<long, float[]> GetAllEmbeddings();
    List<(long Id, string AudioFilePath)> GetRecordsWithoutEmbedding();
}
