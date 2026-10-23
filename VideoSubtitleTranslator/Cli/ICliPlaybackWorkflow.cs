namespace VideoSubtitleTranslator.Cli;

public interface ICliPlaybackWorkflow
{
    Task<VideoPlaybackResult> PlayTaskAsync(string taskId, CancellationToken cancellationToken = default);
}
