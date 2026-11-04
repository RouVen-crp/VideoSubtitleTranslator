using VideoSubtitleTranslator.Cli;

namespace VideoSubtitleTranslator.Tests;

/// <summary>
/// 烧录是独立命令：只复用已完成任务的视频与字幕，不重新下载、转录或翻译。
/// </summary>
public sealed class CliBurnTests
{
    [Fact]
    public async Task Burn_reuses_the_completed_tasks_video_and_subtitle()
    {
        using var temp = new TemporaryDirectory();
        var store = new CliTaskStore(Path.Combine(temp.Path, "tasks"));
        var workspace = Path.Combine(temp.Path, "lesson");
        Directory.CreateDirectory(workspace);
        var videoPath = Path.Combine(workspace, "video.mp4");
        var subtitlePath = Path.Combine(workspace, "translated_subtitle.proofread.srt");
        File.WriteAllText(videoPath, "video");
        File.WriteAllText(subtitlePath, "1\n00:00:00,000 --> 00:00:01,000\n你好\n\n");
        var task = store.Create(new TranslationRequest("https://example.com/video", "standard", temp.Path));
        task.Status = CliTaskStatus.Completed;
        task.WorkspacePath = workspace;
        task.Context["VideoExt"] = ".mp4";
        store.Save(task);

        string? executable = null;
        IReadOnlyList<string>? arguments = null;
        var workflow = new PipelineTranslationWorkflow(
            mediaProcessRunner: (path, args, _, _) =>
            {
                executable = path;
                arguments = args;
                File.WriteAllText(args[^1], "burned video");
                return Task.FromResult(0);
            },
            taskStore: store);

        var burnedPath = await workflow.BurnTaskAsync(task.Id);

        Assert.Equal("ffmpeg", executable);
        Assert.Equal(Path.Combine(workspace, "burned_video.mp4"), burnedPath);
        Assert.True(File.Exists(burnedPath));
        Assert.Equal(videoPath, arguments![Array.IndexOf(arguments.ToArray(), "-i") + 1]);
        Assert.Contains("subtitle.srt", arguments[Array.IndexOf(arguments.ToArray(), "-vf") + 1]);

        // 重复烧录不覆盖已有产物，编号递增。
        var secondPath = await workflow.BurnTaskAsync(task.Id);
        Assert.Equal(Path.Combine(workspace, "burned_video.2.mp4"), secondPath);
    }

    [Fact]
    public async Task Burn_rejects_tasks_that_are_not_completed()
    {
        using var temp = new TemporaryDirectory();
        var store = new CliTaskStore(Path.Combine(temp.Path, "tasks"));
        var task = store.Create(new TranslationRequest("https://example.com/video", "standard", temp.Path));
        var workflow = new PipelineTranslationWorkflow(taskStore: store);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.BurnTaskAsync(task.Id));

        Assert.Contains("尚未完成", exception.Message);
    }

    [Fact]
    public async Task Burn_command_rejects_invalid_task_ids_without_running_the_workflow()
    {
        using var temp = new TemporaryDirectory();
        var workflow = new RecordingBurnWorkflow();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await CliApplication.RunAsync(
            ["burn", "bad-id"], workflow, output, error, isInteractive: false, currentDirectory: temp.Path);

        Assert.Equal(2, exit);
        Assert.Contains("zimu history", error.ToString());
        Assert.Equal(0, workflow.Burns);
    }

    [Fact]
    public async Task Burn_command_reports_the_generated_video_path()
    {
        using var temp = new TemporaryDirectory();
        var burnedPath = Path.Combine(temp.Path, "burned_video.mp4");
        File.WriteAllText(burnedPath, "burned");
        var workflow = new RecordingBurnWorkflow { ResultPath = burnedPath };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await CliApplication.RunAsync(
            ["burn", "a1b2c3d4"], workflow, output, error, isInteractive: false, currentDirectory: temp.Path);

        Assert.Equal(0, exit);
        Assert.Contains(Path.GetFullPath(burnedPath), output.ToString());
        Assert.Equal(1, workflow.Burns);
        Assert.Empty(error.ToString());
    }

    private sealed class RecordingBurnWorkflow : ICliWorkflow, ICliBurnWorkflow
    {
        public string? ResultPath { get; init; }
        public int Burns { get; private set; }

        public Task<string> BurnTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            Burns++;
            return Task.FromResult(ResultPath ?? string.Empty);
        }

        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
