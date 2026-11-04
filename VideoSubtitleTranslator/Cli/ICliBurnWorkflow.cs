namespace VideoSubtitleTranslator.Cli;

/// <summary>
/// 把已完成任务的字幕烧录进原视频。烧录与翻译分属两个命令，
/// 这样重复出片不会重跑 Whisper 与 LLM。
/// </summary>
public interface ICliBurnWorkflow
{
    /// <summary>返回生成的烧录视频绝对路径。</summary>
    Task<string> BurnTaskAsync(string taskId, CancellationToken cancellationToken = default);
}
