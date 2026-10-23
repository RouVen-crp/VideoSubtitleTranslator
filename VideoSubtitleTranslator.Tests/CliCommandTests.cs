using System.Text;
using VideoSubtitleTranslator.Cli;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

public sealed class CliCommandTests
{
    [Fact]
    public async Task Translate_defaults_to_english_to_chinese_standard_and_reports_the_subtitle()
    {
        using var temp = new TemporaryDirectory();
        var subtitlePath = Path.Combine(temp.Path, "translated_subtitle.srt");
        var workflow = new RecordingTranslationWorkflow(subtitlePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", "https://example.com/video"], workflow, output, error,
            isInteractive: false, currentDirectory: temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Equal("standard", workflow.Request?.Mode);
        Assert.Equal("https://example.com/video", workflow.Request?.Input);
        Assert.False(workflow.Request?.IsLocalFile);
        Assert.False(workflow.Request?.CreateBilingualSubtitle);
        Assert.False(workflow.Request?.BurnSubtitles);
        Assert.Equal(Path.Combine(temp.Path, "VideoSubtitleTranslator", "output"), workflow.Request?.WorkspaceRoot);
        Assert.Contains(subtitlePath, output.ToString());
        Assert.Empty(error.ToString());
        Assert.Equal("1\n00:00:00,000 --> 00:00:01,000\n你好\n", await File.ReadAllTextAsync(subtitlePath));
    }

    [Fact]
    public async Task Translate_accepts_explicit_bilingual_and_burn_options_and_reports_both_artifacts()
    {
        using var temp = new TemporaryDirectory();
        var subtitlePath = Path.Combine(temp.Path, "translated_subtitle.srt");
        var workflow = new RecordingTranslationWorkflow(subtitlePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", "https://example.com/video", "--bilingual", "--burn"], workflow, output, error,
            isInteractive: false, currentDirectory: temp.Path);

        Assert.Equal(0, exitCode);
        Assert.True(workflow.Request?.CreateBilingualSubtitle);
        Assert.True(workflow.Request?.BurnSubtitles);
        Assert.Contains(Path.GetFullPath(workflow.Result!.BilingualSubtitlePath!), output.ToString());
        Assert.Contains(Path.GetFullPath(workflow.Result.BurnedVideoPath!), output.ToString());
        Assert.Empty(error.ToString());
    }

    [Theory]
    [InlineData("--bilingual")]
    [InlineData("--burn")]
    public async Task Translate_rejects_duplicate_output_options(string option)
    {
        using var temp = new TemporaryDirectory();
        var workflow = new RecordingTranslationWorkflow(Path.Combine(temp.Path, "unused.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", "https://example.com/video", option, option], workflow, output, error, false, temp.Path);

        Assert.Equal(2, exitCode);
        Assert.Contains("只能指定一次", error.ToString());
        Assert.Null(workflow.Request);
    }

    [Fact]
    public async Task Download_saves_video_and_reports_actual_path_without_translation()
    {
        using var temp = new TemporaryDirectory();
        var workflow = new RecordingTranslationWorkflow(Path.Combine(temp.Path, "unused.srt"))
        {
            DownloadedPath = Path.Combine(temp.Path, "output", "actual-title.mp4")
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["download", "https://example.com/video"], workflow, output, error, false, temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Equal("https://example.com/video", workflow.DownloadRequest?.Url);
        Assert.Null(workflow.Request);
        Assert.Equal(Path.Combine(temp.Path, "VideoSubtitleTranslator", "output"), workflow.DownloadRequest?.OutputDirectory);
        Assert.Contains(Path.GetFullPath(workflow.DownloadedPath), output.ToString());
        Assert.Empty(error.ToString());
    }

    [Theory]
    [InlineData("ftp://example.com/video")]
    [InlineData("not-a-url")]
    public async Task Download_rejects_invalid_url_with_nonzero_exit(string url)
    {
        using var temp = new TemporaryDirectory();
        var workflow = new RecordingTranslationWorkflow(Path.Combine(temp.Path, "unused.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["download", url], workflow, output, error, false, temp.Path);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("HTTP 或 HTTPS", error.ToString());
        Assert.Null(workflow.DownloadRequest);
    }

    [Fact]
    public async Task Download_help_is_available_without_running_workflow()
    {
        using var temp = new TemporaryDirectory();
        var workflow = new RecordingTranslationWorkflow(Path.Combine(temp.Path, "unused.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["download", "--help"], workflow, output, error, false, temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("zimu download <URL>", output.ToString());
        Assert.Null(workflow.DownloadRequest);
    }

    [Fact]
    public async Task Download_failure_returns_actionable_nonzero_result_without_stack_trace()
    {
        using var temp = new TemporaryDirectory();
        var workflow = new RecordingTranslationWorkflow(Path.Combine(temp.Path, "unused.srt"))
        {
            DownloadFailure = new VideoDownloadException("视频下载失败。请确认链接可访问，并检查 yt-dlp 和网络后重试。")
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["download", "https://example.com/video"], workflow, output, error, false, temp.Path);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("视频下载失败", error.ToString());
        Assert.DoesNotContain("at VideoSubtitleTranslator", error.ToString());
        Assert.Null(workflow.Request);
    }

    [Fact]
    public async Task Translate_accepts_a_local_video_and_reports_task_and_sidecar_subtitles()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "lesson.mp4");
        Directory.CreateDirectory(temp.Path);
        await File.WriteAllTextAsync(videoPath, "video");
        var subtitlePath = Path.Combine(temp.Path, "task", "translated_subtitle.srt");
        var workflow = new RecordingTranslationWorkflow(subtitlePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", videoPath], workflow, output, error, false, temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Equal(videoPath, workflow.Request?.Input);
        Assert.Equal(Path.Combine(temp.Path, "VideoSubtitleTranslator", "output"), workflow.Request?.WorkspaceRoot);
        Assert.True(workflow.Request?.IsLocalFile);
        Assert.Contains(subtitlePath, output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Translate_does_not_overwrite_existing_subtitle_sidecar()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "lesson.mp4");
        Directory.CreateDirectory(temp.Path);
        await File.WriteAllTextAsync(videoPath, "video");
        var existingPath = Path.Combine(temp.Path, "lesson.zh.srt");
        await File.WriteAllTextAsync(existingPath, "keep this");
        var workflow = new RecordingTranslationWorkflow(Path.Combine(temp.Path, "task", "subtitle.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", videoPath], workflow, output, error, false, temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Equal("keep this", await File.ReadAllTextAsync(existingPath));
    }

    [Fact]
    public async Task Translate_reports_the_source_sidecar_location_when_workflow_copies_it()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(temp.Path);
        var subtitlePath = Path.Combine(temp.Path, "task", "translated.srt");
        var sidecarPath = Path.Combine(temp.Path, "lesson.zh.srt");
        var workflow = new RecordingTranslationWorkflow(subtitlePath) { SidecarSubtitlePath = sidecarPath };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", "https://example.com/video"], workflow, output, error, false, temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains(Path.GetFullPath(sidecarPath), output.ToString());
    }

    [Fact]
    public async Task Translate_rejects_missing_local_video_with_actionable_error()
    {
        using var temp = new TemporaryDirectory();
        var workflow = new RecordingTranslationWorkflow(Path.Combine(temp.Path, "unused.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", Path.Combine(temp.Path, "missing.mp4")], workflow, output, error, false, temp.Path);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("文件不存在", error.ToString());
        Assert.Null(workflow.Request);
    }

    [Fact]
    public async Task Local_workflow_skips_online_steps_and_copies_subtitle_without_overwriting()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "lesson.mp4");
        Directory.CreateDirectory(temp.Path);
        await File.WriteAllTextAsync(videoPath, "video");
        var workspace = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(Path.Combine(workspace, "lesson"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "lesson", "video.mp4"), "preserve");
        var existingSidecar = Path.Combine(temp.Path, "lesson.zh.srt");
        await File.WriteAllTextAsync(existingSidecar, "keep");
        PipelineConfig? observedConfig = null;
        var workflow = new PipelineTranslationWorkflow(
            (_, _) => Task.FromResult<string?>(null),
            (config, context, _) =>
            {
                observedConfig = config;
                Assert.Equal(videoPath, context["Url"]);
                Assert.EndsWith("lesson_2", context["WorkspacePath"]);
                Assert.True(File.Exists(Path.Combine(context["WorkspacePath"], "video.mp4")));
                var dirs = new WorkDirs(context["WorkspacePath"], ".mp4");
                File.WriteAllText(dirs.NormalizedSubtitlePath, "1\n00:00:00,000 --> 00:00:01,000\nHello\n\n");
                File.WriteAllText(dirs.TranslatedSubtitlePath, "translated");
                return context;
            });

        var result = await workflow.TranslateAsync(new TranslationRequest(videoPath, "standard", workspace, true));

        Assert.NotNull(observedConfig);
        Assert.DoesNotContain(observedConfig!.Steps, step => step.Step is "InitMetadata" or "Download" or "BurnSubtitle");
        Assert.Equal("keep", await File.ReadAllTextAsync(existingSidecar));
        Assert.Equal("preserve", await File.ReadAllTextAsync(Path.Combine(workspace, "lesson", "video.mp4")));
        Assert.Equal("translated", await File.ReadAllTextAsync(result.SidecarSubtitlePath!));
        Assert.EndsWith("lesson.zh.2.srt", result.SidecarSubtitlePath);
    }

    [Fact]
    public async Task Local_workflow_reports_unusable_media_without_running_pipeline()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "lesson.mp4");
        Directory.CreateDirectory(temp.Path);
        await File.WriteAllTextAsync(videoPath, "video");
        var pipelineRan = false;
        var workflow = new PipelineTranslationWorkflow(
            (_, _) => Task.FromResult<string?>("媒体无法读取"),
            (_, _, _) =>
            {
                pipelineRan = true;
                return new Dictionary<string, string>();
            });

        var exception = await Assert.ThrowsAsync<LocalVideoValidationException>(() =>
            workflow.TranslateAsync(new TranslationRequest(videoPath, "standard", temp.Path, true)));

        Assert.Contains("媒体无法读取", exception.Message);
        Assert.False(pipelineRan);
    }

    [Fact]
    public async Task Cli_shows_safe_local_media_diagnostic_and_returns_nonzero()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(temp.Path);
        var videoPath = Path.Combine(temp.Path, "lesson.mp4");
        await File.WriteAllTextAsync(videoPath, "video");
        var workflow = new RecordingTranslationWorkflow(Path.Combine(temp.Path, "unused.srt"))
        {
            Failure = new LocalVideoValidationException("找不到 ffprobe。请安装 FFmpeg 后重试。")
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", videoPath], workflow, output, error, false, temp.Path);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("找不到 ffprobe", error.ToString());
        Assert.Equal(videoPath, workflow.Request?.Input);
    }

    [Fact]
    public async Task Help_is_available_without_running_the_translation_workflow()
    {
        var workflow = new RecordingTranslationWorkflow(Path.Combine(Path.GetTempPath(), "unused.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["translate", "--help"], workflow, output, error, false, "/tmp");

        Assert.Equal(0, exitCode);
        Assert.Contains("translate <URL|视频路径>", output.ToString());
        Assert.Null(workflow.Request);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Translate_can_select_the_existing_meme_mode()
    {
        using var temp = new TemporaryDirectory();
        var workflow = new RecordingTranslationWorkflow(Path.Combine(temp.Path, "translated_subtitle.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", "https://example.com/video", "--mode", "meme"], workflow, output, error,
            isInteractive: false, currentDirectory: temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Equal("meme", workflow.Request?.Mode);
        Assert.Empty(error.ToString());
    }

    [Theory]
    [InlineData("translate", "")]
    [InlineData("unknown", "")]
    [InlineData("translate", "ftp://example.com/video")]
    public async Task Invalid_commands_return_an_actionable_error_and_nonzero_exit(string command, string argument)
    {
        var workflow = new RecordingTranslationWorkflow(Path.Combine(Path.GetTempPath(), "unused.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var args = string.IsNullOrEmpty(argument) ? [command] : new[] { command, argument };
        var exitCode = await CliApplication.RunAsync(args, workflow, output, error, false, Path.GetTempPath());

        Assert.NotEqual(0, exitCode);
        Assert.NotEmpty(error.ToString());
        Assert.Null(workflow.Request);
    }

    [Fact]
    public async Task No_arguments_in_a_noninteractive_terminal_prints_help_without_hanging()
    {
        var workflow = new RecordingTranslationWorkflow(Path.Combine(Path.GetTempPath(), "unused.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync([], workflow, output, error, false, "/tmp");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("zimu translate <URL|视频路径>", error.ToString());
        Assert.Null(workflow.Request);
    }

    [Fact]
    public async Task Untrusted_command_and_option_text_is_not_echoed_in_diagnostics()
    {
        var workflow = new RecordingTranslationWorkflow(Path.Combine(Path.GetTempPath(), "unused.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["unknown-command-marker"], workflow, output, error, false, "/tmp");

        Assert.NotEqual(0, exitCode);
        Assert.DoesNotContain("unknown-command-marker", error.ToString());

        error.GetStringBuilder().Clear();
        exitCode = await CliApplication.RunAsync(
            ["translate", "https://example.com/video", "--unknown-option-marker=value"],
            workflow, output, error, false, "/tmp");

        Assert.NotEqual(0, exitCode);
        Assert.DoesNotContain("unknown-option-marker=value", error.ToString());
        Assert.Null(workflow.Request);
    }

    [Fact]
    public async Task Translation_failures_return_nonzero_without_a_stack_trace()
    {
        var workflow = new RecordingTranslationWorkflow(Path.Combine(Path.GetTempPath(), "unused.srt"))
        {
            Failure = new InvalidOperationException("diagnostic-payload")
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["translate", "https://example.com/video"], workflow, output, error, false, "/tmp");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("翻译失败", error.ToString());
        Assert.DoesNotContain("diagnostic-payload", error.ToString());
        Assert.DoesNotContain("at VideoSubtitleTranslator", error.ToString());
        Assert.Empty(output.ToString());
    }

    private sealed class RecordingTranslationWorkflow(string subtitlePath) : ICliWorkflow
    {
        public TranslationRequest? Request { get; private set; }
        public Exception? Failure { get; init; }
        public string? SidecarSubtitlePath { get; init; }
        public TranslationResult? Result { get; private set; }
        public string? DownloadedPath { get; init; }
        public Exception? DownloadFailure { get; init; }
        public DownloadRequest? DownloadRequest { get; private set; }

        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            if (Failure is not null) throw Failure;
            Directory.CreateDirectory(Path.GetDirectoryName(subtitlePath)!);
            File.WriteAllText(subtitlePath, "1\n00:00:00,000 --> 00:00:01,000\n你好\n", Encoding.UTF8);
            var bilingualPath = request.CreateBilingualSubtitle ? Path.Combine(Path.GetDirectoryName(subtitlePath)!, "dual.srt") : null;
            var burnedVideoPath = request.BurnSubtitles ? Path.Combine(Path.GetDirectoryName(subtitlePath)!, "burned.mp4") : null;
            if (bilingualPath is not null) File.WriteAllText(bilingualPath, "bilingual");
            if (burnedVideoPath is not null) File.WriteAllText(burnedVideoPath, "video");
            Result = new TranslationResult(subtitlePath, SidecarSubtitlePath, bilingualPath, burnedVideoPath);
            return Task.FromResult(Result);
        }

        public Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
        {
            DownloadRequest = request;
            if (DownloadFailure is not null) throw DownloadFailure;
            Directory.CreateDirectory(Path.GetDirectoryName(DownloadedPath!)!);
            File.WriteAllText(DownloadedPath!, "video");
            return Task.FromResult(new DownloadResult(DownloadedPath!));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
