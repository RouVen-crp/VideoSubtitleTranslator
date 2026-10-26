using System.Collections.Generic;
using System.Diagnostics;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 拆分音频（步骤层）。
/// </summary>
[PipelineStep("SplitAudio", RequiredKeys = new[] { "WorkspacePath", "VideoExt" })]
public abstract class SplitAudioStepBase : IPipelineStep
{
    public string Step => "SplitAudio";

    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

/// <summary>
/// 使用 ffmpeg 从视频中提取音频的默认实现（暂时仍委托 AudioSplitter，后续会内联）。
/// </summary>
[PipelineStep("SplitAudio", Implementation = "Default")]
public sealed class DefaultSplitAudioStep : SplitAudioStepBase
{
    private static void Split(WorkDirs dirs)
    {
        if (File.Exists(dirs.AudioPath) && new FileInfo(dirs.AudioPath).Length > 0)
        {
            Console.WriteLine("Audio exists, skip split");
            return;
        }
        if (File.Exists(dirs.AudioPath)) File.Delete(dirs.AudioPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = GlobalRuntimeConfig.Current.Tools.FfmpegPath,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "-i", dirs.VideoPath, "-vn", "-acodec", "pcm_s16le", "-ar", "44100", dirs.AudioPath })
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process is null) throw new PipelineStageFailureException("无法启动 ffmpeg 音频提取进程。");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new PipelineStageFailureException($"ffmpeg 音频提取失败（exit code {process.ExitCode}）。请确认视频已下载且 ffmpeg 可用。");
        if (!File.Exists(dirs.AudioPath) || new FileInfo(dirs.AudioPath).Length == 0)
            throw new PipelineStageFailureException("ffmpeg 未生成音频文件；请确认输入视频有效。");
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var workspacePath = context["WorkspacePath"];
        var ext = context["VideoExt"];
        var dirs = new WorkDirs(workspacePath, ext);

        Split(dirs);
        return context;
    }
}

