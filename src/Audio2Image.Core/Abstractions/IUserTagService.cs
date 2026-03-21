using Audio2Image.Core.Models;

namespace Audio2Image.Core.Abstractions;

public interface IUserTagService : IDisposable
{
    UserTag CreateTag(string name, string color = "#FF6B35");
    List<UserTag> GetAllTags();
    void DeleteTag(long tagId);
    void RenameTag(long tagId, string newName);

    void AssignTag(long spectrogramId, long tagId);
    void RemoveTag(long spectrogramId, long tagId);
    List<UserTag> GetTagsForSpectrogram(long spectrogramId);
    List<long> GetSpectrogramIdsByTag(long tagId);
}
