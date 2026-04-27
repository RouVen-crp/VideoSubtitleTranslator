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
        if (File.Exists(dirs.AudioPath))
        {
            Console.WriteLine("Audio exists, skip split");
            return;
        }

        using var process = Process.Start("ffmpeg",
            $"-i {dirs.VideoPath} -vn -acodec pcm_s16le -ar 44100 {dirs.AudioPath}");
        process?.WaitForExit();
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

