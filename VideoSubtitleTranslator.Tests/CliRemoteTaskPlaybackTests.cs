using VideoSubtitleTranslator.Cli;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

/// <summary>
/// URL 任务的工作目录只有流水线跑完才确定；任务记录必须回写它，
/// 否则 zimu play 和完成页播放都会报“任务的视频文件信息不完整”。
/// </summary>
[Collection("Shared runtime configuration")]
public sealed class CliRemoteTaskPlaybackTests
{
    [Fact]
    public async Task Remote_translation_persists_video_info_so_playback_opens_the_downloaded_video()
    {
        using var temp = new TemporaryDirectory();
        var store = new CliTaskStore(Path.Combine(temp.Path, "tasks"));
        string? workspacePath = null;
        IReadOnlyList<string>? launchedArguments = null;
        var playback = new VideoPlaybackService(
            findCompatiblePlayer: () => "mpv",
            startPlayer: (_, arguments) => launchedArguments = arguments,
            openDefault: _ => throw new Xunit.Sdk.XunitException("兼容播放器存在时不应回退到系统默认播放器。"));
        var workflow = new PipelineTranslationWorkflow(
            pipelineRunner: (_, context, _) =>
            {
                workspacePath = Path.Combine(context["WorkspaceRoot"], "remote_lesson");
                Directory.CreateDirectory(workspacePath);
                var dirs = new WorkDirs(workspacePath, ".mp4");
                File.WriteAllText(dirs.VideoPath, "downloaded video");
                File.WriteAllText(dirs.ProofreadSubtitlePath, "1\n00:00:00,000 --> 00:00:01,000\n你好\n\n");
                return new Dictionary<string, string>(context, StringComparer.OrdinalIgnoreCase)
                {
                    ["WorkspacePath"] = workspacePath,
                    ["VideoExt"] = ".mp4"
                };
            },
            taskStore: store,
            videoPlayback: playback);

        var result = await workflow.TranslateAsync(
            new TranslationRequest("https://example.com/video", "standard", temp.Path));

        Assert.False(string.IsNullOrWhiteSpace(result.TaskId));
        var saved = store.Get(result.TaskId!)!;
        Assert.Equal(workspacePath, saved.WorkspacePath);
        Assert.Equal(".mp4", saved.Context["VideoExt"]);

        var played = await workflow.PlayTaskAsync(result.TaskId!);

        Assert.True(played.UsedCompatiblePlayer);
        Assert.Equal(Path.Combine(workspacePath!, "video.mp4"), launchedArguments![1]);
        Assert.Equal($"--sub-file={Path.GetFullPath(result.SubtitlePath)}", launchedArguments[0]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
