using VideoSubtitleTranslator.Cli;

namespace VideoSubtitleTranslator.Tests;

public sealed class GuidedCliInterfaceTests
{
    [Fact]
    public async Task Interactive_no_argument_launch_shows_footer_and_quits_without_running_workflows()
    {
        using var temp = new TemporaryDirectory();
        var terminal = new FakeTerminal([ConsoleKey.Q]);
        var workflow = new FakeWorkflow(temp.Path);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CliApplication.RunAsync([], workflow, output, error, true, temp.Path, terminal: terminal);

        Assert.Equal(0, result);
        Assert.Contains("翻译视频", output.ToString());
        Assert.Contains("任务历史", output.ToString());
        Assert.EndsWith("↑↓ 导航 | Enter 确认 | Esc 返回 | Q 退出" + Environment.NewLine, output.ToString());
        Assert.Equal(0, workflow.TranslateCount);
        Assert.Equal(0, workflow.HistoryCount);
        Assert.Equal(1, terminal.ClearCount);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Noninteractive_no_argument_launch_shows_help_without_waiting_for_input()
    {
        using var temp = new TemporaryDirectory();
        var workflow = new FakeWorkflow(temp.Path);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CliApplication.RunAsync([], workflow, output, error, false, temp.Path);

        Assert.Equal(2, result);
        Assert.Contains("未提供命令", error.ToString());
        Assert.Contains("zimu translate", error.ToString());
        Assert.Empty(output.ToString());
        Assert.Equal(0, workflow.TranslateCount);
    }

    [Fact]
    public async Task Guided_translation_uses_the_cli_workflow_and_prompts_before_playback()
    {
        using var temp = new TemporaryDirectory();
        var terminal = new FakeTerminal([ConsoleKey.Enter, ConsoleKey.Enter, ConsoleKey.Q], ["https://example.com/video", "n"]);
        var workflow = new FakeWorkflow(temp.Path);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CliApplication.RunAsync([], workflow, output, error, true, temp.Path, terminal: terminal);

        Assert.Equal(0, result);
        Assert.Equal(1, workflow.TranslateCount);
        Assert.Equal("https://example.com/video", workflow.LastRequest?.Input);
        Assert.Equal(0, workflow.PlayCount);
        Assert.Contains("中文字幕已生成", output.ToString());
        Assert.Contains("现在播放视频并加载中文字幕？[y/N]", output.ToString());
        Assert.Contains("y 播放 | Enter 返回 | Q 退出", output.ToString());
        Assert.Contains($"中文字幕：{Path.Combine(temp.Path, "translated.srt")}", output.ToString());
        Assert.Equal(0, error.GetStringBuilder().Length);
    }

    [Fact]
    public async Task Guided_history_can_resume_an_interrupted_task_using_the_shared_workflow()
    {
        using var temp = new TemporaryDirectory();
        var task = new CliTaskRecord
        {
            Id = "a1b2c3d4", Input = "https://example.com/video", Status = CliTaskStatus.Interrupted, Stage = "Translate"
        };
        var terminal = new FakeTerminal(
            [ConsoleKey.DownArrow, ConsoleKey.Enter, ConsoleKey.Enter, ConsoleKey.Enter, ConsoleKey.Enter, ConsoleKey.Escape, ConsoleKey.Q]);
        var workflow = new FakeWorkflow(temp.Path, [task]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CliApplication.RunAsync([], workflow, output, error, true, temp.Path, terminal: terminal);

        Assert.Equal(0, result);
        Assert.Equal(1, workflow.ResumeCount);
        Assert.Contains("可能会重新执行并产生费用", error.ToString());
        Assert.Contains("恢复完成", output.ToString());
        Assert.Contains("P 播放 | Enter 返回 | Q 退出", output.ToString());
    }

    [Fact]
    public async Task Guided_history_can_play_a_completed_task_without_auto_playing_on_launch()
    {
        using var temp = new TemporaryDirectory();
        var task = new CliTaskRecord
        {
            Id = "d4c3b2a1", Input = "https://example.com/video", Status = CliTaskStatus.Completed, Stage = "Completed"
        };
        var terminal = new FakeTerminal(
            [ConsoleKey.DownArrow, ConsoleKey.Enter, ConsoleKey.Enter, ConsoleKey.Enter, ConsoleKey.Enter, ConsoleKey.Escape, ConsoleKey.Q]);
        var workflow = new FakeWorkflow(temp.Path, [task]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await CliApplication.RunAsync([], workflow, output, error, true, temp.Path, terminal: terminal);

        Assert.Equal(0, result);
        Assert.Equal(1, workflow.PlayCount);
        Assert.Contains("已使用兼容播放器加载中文字幕", output.ToString());
        Assert.Contains("播放已启动", output.ToString());
        Assert.Empty(error.ToString());
    }

    private sealed class FakeWorkflow(string workspace, IReadOnlyList<CliTaskRecord>? tasks = null)
        : ICliWorkflow, ICliTaskWorkflow, ICliPlaybackWorkflow
    {
        private readonly IReadOnlyList<CliTaskRecord> _tasks = tasks ?? Array.Empty<CliTaskRecord>();
        public int TranslateCount { get; private set; }
        public int HistoryCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int PlayCount { get; private set; }
        public TranslationRequest? LastRequest { get; private set; }

        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            TranslateCount++;
            LastRequest = request;
            var subtitlePath = Path.Combine(workspace, "translated.srt");
            File.WriteAllText(subtitlePath, "subtitle");
            return Task.FromResult(new TranslationResult(subtitlePath, TaskId: "a1b2c3d4"));
        }

        public Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DownloadResult(Path.Combine(workspace, "video.mp4")));

        public Task<IReadOnlyList<CliTaskRecord>> GetTaskHistoryAsync(CancellationToken cancellationToken = default)
        {
            HistoryCount++;
            return Task.FromResult(_tasks);
        }

        public Task<TranslationResult> ResumeTaskAsync(string id, CancellationToken cancellationToken = default)
        {
            ResumeCount++;
            var subtitlePath = Path.Combine(workspace, "resumed.srt");
            File.WriteAllText(subtitlePath, "subtitle");
            return Task.FromResult(new TranslationResult(subtitlePath, TaskId: id));
        }

        public Task<VideoPlaybackResult> PlayTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            PlayCount++;
            var videoPath = Path.Combine(workspace, "video.mp4");
            var subtitlePath = Path.Combine(workspace, "translated.srt");
            File.WriteAllText(videoPath, "video");
            File.WriteAllText(subtitlePath, "subtitle");
            return Task.FromResult(new VideoPlaybackResult(videoPath, subtitlePath, UsedCompatiblePlayer: true));
        }
    }

    private sealed class FakeTerminal(IEnumerable<ConsoleKey> keys, IEnumerable<string?>? lines = null) : ICliTerminal
    {
        private readonly Queue<ConsoleKey> _keys = new(keys);
        private readonly Queue<string?> _lines = new(lines ?? Array.Empty<string?>());
        public int ClearCount { get; private set; }

        public void Clear() => ClearCount++;

        public ConsoleKeyInfo ReadKey()
        {
            var key = _keys.Dequeue();
            return new ConsoleKeyInfo((char)key, key, shift: false, alt: false, control: false);
        }

        public string? ReadLine() => _lines.Dequeue();
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
