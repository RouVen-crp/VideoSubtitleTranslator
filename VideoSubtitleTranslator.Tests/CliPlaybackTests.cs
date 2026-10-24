using VideoSubtitleTranslator.Cli;
using System.ComponentModel;

namespace VideoSubtitleTranslator.Tests;

public sealed class CliPlaybackTests
{
    [Fact]
    public async Task Play_command_loads_the_task_subtitle_through_a_compatible_player()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "video.mp4");
        var subtitlePath = Path.Combine(temp.Path, "translated.srt");
        await File.WriteAllTextAsync(videoPath, "video");
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        string? executable = null;
        IReadOnlyList<string>? arguments = null;
        var service = new VideoPlaybackService(
            findCompatiblePlayer: () => "mpv",
            startPlayer: (path, args) => (executable, arguments) = (path, args),
            openDefault: _ => throw new Xunit.Sdk.XunitException("The default player should not be used."));
        var workflow = new FakePlaybackWorkflow(service, videoPath, subtitlePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["play", "a1b2c3d4"], workflow, output, error, false, temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Equal("mpv", executable);
        Assert.Equal([$"--sub-file={Path.GetFullPath(subtitlePath)}", Path.GetFullPath(videoPath)], arguments);
        Assert.Contains("兼容播放器", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Play_falls_back_to_default_player_and_shows_manual_subtitle_path()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "video.mp4");
        var subtitlePath = Path.Combine(temp.Path, "translated.srt");
        await File.WriteAllTextAsync(videoPath, "video");
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        string? openedVideo = null;
        var service = new VideoPlaybackService(
            findCompatiblePlayer: () => null,
            startPlayer: (_, _) => throw new Xunit.Sdk.XunitException("No compatible player should be started."),
            openDefault: path => openedVideo = path);
        var workflow = new FakePlaybackWorkflow(service, videoPath, subtitlePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["play", "a1b2c3d4"], workflow, output, error, false, temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Equal(Path.GetFullPath(videoPath), openedVideo);
        Assert.Contains(Path.GetFullPath(subtitlePath), output.ToString());
        Assert.Contains("手动加载", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Vlc_receives_the_subtitle_as_a_file_url()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "video.mp4");
        var subtitlePath = Path.Combine(temp.Path, "translated.srt");
        await File.WriteAllTextAsync(videoPath, "video");
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        IReadOnlyList<string>? arguments = null;
        var service = new VideoPlaybackService(
            findCompatiblePlayer: () => "vlc",
            startPlayer: (_, args) => arguments = args,
            openDefault: _ => throw new Xunit.Sdk.XunitException("The default player should not be used."));

        service.Play(videoPath, subtitlePath);

        Assert.Equal(
            [$"--sub-url={new Uri(Path.GetFullPath(subtitlePath)).AbsoluteUri}", Path.GetFullPath(videoPath)],
            arguments);
    }

    [Fact]
    public async Task Translation_does_not_probe_or_open_any_player()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "video.mp4");
        var subtitlePath = Path.Combine(temp.Path, "translated.srt");
        await File.WriteAllTextAsync(videoPath, "video");
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        var probeCount = 0;
        var service = new VideoPlaybackService(findCompatiblePlayer: () =>
        {
            probeCount++;
            return null;
        });
        var workflow = new FakePlaybackWorkflow(service, videoPath, subtitlePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["translate", "https://example.com/video"], workflow, output, error, false, temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, probeCount);
    }

    [Fact]
    public async Task Failed_compatible_player_start_falls_back_to_the_system_default()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "video.mp4");
        var subtitlePath = Path.Combine(temp.Path, "translated.srt");
        await File.WriteAllTextAsync(videoPath, "video");
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        string? openedVideo = null;
        var service = new VideoPlaybackService(
            findCompatiblePlayer: () => "mpv",
            startPlayer: (_, _) => throw new Win32Exception("controlled process failure"),
            openDefault: path => openedVideo = path);

        var result = service.Play(videoPath, subtitlePath);

        Assert.False(result.UsedCompatiblePlayer);
        Assert.Equal(Path.GetFullPath(videoPath), openedVideo);
    }

    [Fact]
    public async Task Playback_workflow_uses_the_completed_local_tasks_original_video_and_subtitle()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "lesson.mp4");
        var subtitlePath = Path.Combine(temp.Path, "lesson.zh.srt");
        await File.WriteAllTextAsync(videoPath, "video");
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        var store = new CliTaskStore(Path.Combine(temp.Path, "tasks"));
        var task = store.Create(new TranslationRequest(videoPath, "standard", temp.Path, IsLocalFile: true));
        task.Status = CliTaskStatus.Completed;
        task.SubtitlePath = subtitlePath;
        store.Save(task);
        string? launchedVideo = null;
        IReadOnlyList<string>? launchedArguments = null;
        var service = new VideoPlaybackService(
            findCompatiblePlayer: () => "mpv",
            startPlayer: (_, args) => (launchedVideo, launchedArguments) = (args[1], args),
            openDefault: _ => throw new Xunit.Sdk.XunitException("The default player should not be used."));
        var workflow = new PipelineTranslationWorkflow(taskStore: store, videoPlayback: service);

        await workflow.PlayTaskAsync(task.Id);

        Assert.Equal(Path.GetFullPath(videoPath), launchedVideo);
        Assert.Equal($"--sub-file={Path.GetFullPath(subtitlePath)}", launchedArguments![0]);
    }

    [Fact]
    public async Task Play_rejects_invalid_task_ids_without_calling_playback_workflow()
    {
        using var temp = new TemporaryDirectory();
        var service = new VideoPlaybackService(findCompatiblePlayer: () => throw new Xunit.Sdk.XunitException("Player detection must not run."));
        var workflow = new FakePlaybackWorkflow(service, "", "");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["play", "bad-id"], workflow, output, error, false, temp.Path);

        Assert.Equal(2, exitCode);
        Assert.Contains("zimu history", error.ToString());
        Assert.Equal(0, workflow.PlayCount);
    }

    [Fact]
    public async Task Interactive_translation_offers_playback_only_after_completion_and_plays_on_yes()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "video.mp4");
        var subtitlePath = Path.Combine(temp.Path, "translated.srt");
        await File.WriteAllTextAsync(videoPath, "video");
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        var startCount = 0;
        var service = new VideoPlaybackService(
            findCompatiblePlayer: () => "mpv",
            startPlayer: (_, _) => startCount++,
            openDefault: _ => throw new Xunit.Sdk.XunitException("The default player should not be used."));
        var workflow = new FakePlaybackWorkflow(service, videoPath, subtitlePath) { TaskId = "a1b2c3d4" };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", "https://example.com/video"], workflow, output, error,
            isInteractive: true, currentDirectory: temp.Path, standardInput: new StringReader("y\n"));

        Assert.Equal(0, exitCode);
        Assert.Contains("现在播放视频", output.ToString());
        Assert.Equal(1, startCount);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Default_player_failure_displays_actionable_diagnostic_and_subtitle_location()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "video.mp4");
        var subtitlePath = Path.Combine(temp.Path, "translated.srt");
        await File.WriteAllTextAsync(videoPath, "video");
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        var service = new VideoPlaybackService(
            findCompatiblePlayer: () => null,
            openDefault: _ => throw new Win32Exception("controlled open failure"));
        var workflow = new FakePlaybackWorkflow(service, videoPath, subtitlePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["play", "a1b2c3d4"], workflow, output, error, false, temp.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("请为该视频格式设置默认播放器", error.ToString());
        Assert.Contains(Path.GetFullPath(subtitlePath), error.ToString());
        Assert.DoesNotContain("controlled open failure", error.ToString());
    }

    private sealed class FakePlaybackWorkflow(VideoPlaybackService service, string videoPath, string subtitlePath)
        : ICliWorkflow, ICliPlaybackWorkflow
    {
        public int PlayCount { get; private set; }
        public string? TaskId { get; init; }

        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TranslationResult(subtitlePath, TaskId: TaskId));

        public Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DownloadResult(videoPath));

        public Task<VideoPlaybackResult> PlayTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            PlayCount++;
            return Task.FromResult(service.Play(videoPath, subtitlePath));
        }
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
