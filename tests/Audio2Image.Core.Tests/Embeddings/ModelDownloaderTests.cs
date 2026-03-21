using Audio2Image.Core.Embeddings;

namespace Audio2Image.Core.Tests.Embeddings;

public class ModelDownloaderTests
{
    [Fact]
    public void GetDefaultModelPath_ReturnsCorrectPath()
    {
        var path = ModelDownloader.GetDefaultModelPath("/my/library");
        Assert.Contains("models", path);
        Assert.Contains("Cnn14_16k.onnx", path);
        Assert.StartsWith("/my/library", path);
    }

    [Fact]
    public void GetDefaultModelPath_EmptyLibrary_ReturnsRelativePath()
    {
        var path = ModelDownloader.GetDefaultModelPath("");
        Assert.Contains("Cnn14_16k.onnx", path);
    }
}
