using System.Net.Http;

namespace Audio2Image.Core.Embeddings;

/// <summary>
/// Downloads the PANNs CNN14 ONNX model from a remote URL with progress reporting.
/// The model uses ONNX external data format: .onnx (graph ~87KB) + .onnx.data (weights ~327MB).
/// Both files must reside in the same directory.
/// </summary>
public static class ModelDownloader
{
    // PANNs CNN14 16kHz ONNX (MIT license) — our own HuggingFace mirror
    private const string BaseUrl =
        "https://huggingface.co/Vaytl/PANNs_CNN14_ONNX/resolve/main/";

    /// <summary>Files to download: (remoteName, localName).</summary>
    private static readonly (string Remote, string Local)[] ModelFiles =
    [
        ("Cnn14_16k.onnx", "Cnn14_16k.onnx"),
        ("Cnn14_16k.onnx.data", "Cnn14_16k.onnx.data"),
    ];

    public const string DefaultModelFileName = "Cnn14_16k.onnx";

    /// <summary>
    /// Download the ONNX model (graph + external weights) to the specified path.
    /// Both files are placed in the same directory as <paramref name="destinationPath"/>.
    /// </summary>
    /// <param name="destinationPath">Full path for the .onnx graph file.</param>
    /// <param name="onProgress">Progress callback: (bytesDownloaded, totalBytes) across all files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task DownloadModelAsync(
        string destinationPath,
        Action<long, long>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Total known size: ~327,570,041 bytes (graph 86KB + data 327MB)
        const long estimatedTotal = 327_570_041;
        long cumulativeDownloaded = 0;

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
        };
        using var httpClient = new HttpClient(handler);
        httpClient.Timeout = TimeSpan.FromMinutes(30);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Audio2Image/1.0");

        foreach (var (remote, local) in ModelFiles)
        {
            var filePath = Path.Combine(dir ?? ".", local);
            var tempPath = filePath + ".downloading";
            var url = BaseUrl + remote;

            try
            {
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var fileSize = response.Content.Headers.ContentLength ?? -1;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

                var buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    cumulativeDownloaded += bytesRead;
                    onProgress?.Invoke(cumulativeDownloaded, estimatedTotal);
                }

                await fileStream.FlushAsync(cancellationToken);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }

            // Move temp to final destination
            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempPath, filePath);
        }
    }

    /// <summary>
    /// Check whether the model is fully downloaded (both graph and data files exist).
    /// </summary>
    public static bool IsModelDownloaded(string modelPath)
    {
        if (!File.Exists(modelPath)) return false;
        var dir = Path.GetDirectoryName(modelPath) ?? ".";
        foreach (var (_, local) in ModelFiles)
        {
            if (!File.Exists(Path.Combine(dir, local)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Get the default model path under the library directory.
    /// </summary>
    public static string GetDefaultModelPath(string libraryPath)
    {
        return Path.Combine(libraryPath, "models", DefaultModelFileName);
    }
}
