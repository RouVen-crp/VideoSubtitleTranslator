namespace VideoSubtitleTranslator.Cli;

public sealed record DownloadRequest(string Url, string OutputDirectory);

public sealed record DownloadResult(string VideoPath);

public sealed class VideoDownloadException(string message) : Exception(message);

public interface IVideoDownloadWorkflow
{
    Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default);
}
