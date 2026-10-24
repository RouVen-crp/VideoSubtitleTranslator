using VideoSubtitleTranslator.Cli;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

[Collection("Shared runtime configuration")]
public sealed class CliOptionalArtifactsTests
{
    [Fact]
    public async Task Default_translation_skips_optional_export_steps_and_keeps_the_chinese_srt()
    {
        using var temp = new TemporaryDirectory();
        var fixture = new PipelineFixture();
        var mediaInvocations = 0;
        var workflow = fixture.CreateWorkflow((_, _, _, _) =>
        {
            mediaInvocations++;
            return Task.FromResult(0);
        });
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await RunLocalTranslation(temp, workflow, output, error);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(fixture.Config!.Steps, step => step.Step == "BurnSubtitle");
        Assert.Equal(0, mediaInvocations);
        Assert.Contains(Path.GetFullPath(fixture.LastSubtitlePath!), output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Bilingual_option_generates_aligned_english_and_chinese_srt_without_encoding_video()
    {
        using var temp = new TemporaryDirectory();
        var fixture = new PipelineFixture();
        var mediaInvocations = 0;
        var workflow = fixture.CreateWorkflow((_, _, _, _) =>
        {
            mediaInvocations++;
            return Task.FromResult(0);
        });
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await RunLocalTranslation(temp, workflow, output, error, "--bilingual");

        Assert.Equal(0, exit);
        Assert.Equal(0, mediaInvocations);
        Assert.Contains("Hello there\n你好", await File.ReadAllTextAsync(fixture.LastBilingualPath!));
        Assert.Contains(Path.GetFullPath(fixture.LastBilingualPath!), output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Burn_option_calls_ffmpeg_safely_and_reports_the_burned_video_path()
    {
        using var temp = new TemporaryDirectory();
        var fixture = new PipelineFixture();
        string? executable = null;
        IReadOnlyList<string>? arguments = null;
        string? workingDirectory = null;
        var subtitleCopiedForFfmpeg = false;
        var workflow = fixture.CreateWorkflow((path, args, directory, _) =>
        {
            executable = path;
            arguments = args;
            workingDirectory = directory;
            subtitleCopiedForFfmpeg = File.Exists(Path.Combine(directory, "subtitle.srt"));
            File.WriteAllText(args[^1], "burned video");
            return Task.FromResult(0);
        });
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await RunLocalTranslation(temp, workflow, output, error, "--burn");

        Assert.Equal(0, exit);
        Assert.Equal("ffmpeg", executable);
        Assert.Contains("-n", arguments!);
        Assert.Contains("-i", arguments!);
        Assert.Equal(fixture.LastBurnedPath, arguments![^1]);
        Assert.Contains("subtitle.srt", arguments[Array.IndexOf(arguments.ToArray(), "-vf") + 1]);
        Assert.True(subtitleCopiedForFfmpeg);
        Assert.False(string.IsNullOrWhiteSpace(workingDirectory));
        Assert.Contains(Path.GetFullPath(fixture.LastBurnedPath!), output.ToString());
        Assert.Equal("burned video", await File.ReadAllTextAsync(fixture.LastBurnedPath!));
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Meme_mode_keeps_cultural_dual_track_for_optional_video_burn()
    {
        using var temp = new TemporaryDirectory();
        var fixture = new PipelineFixture();
        IReadOnlyList<string>? arguments = null;
        string? workingDirectory = null;
        string? subtitleContentsSeenByFfmpeg = null;
        var workflow = fixture.CreateWorkflow((_, args, directory, _) =>
        {
            arguments = args;
            workingDirectory = directory;
            subtitleContentsSeenByFfmpeg = File.ReadAllText(Path.Combine(directory, "subtitle.ass"));
            File.WriteAllText(args[^1], "burned video");
            return Task.FromResult(0);
        });
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await RunLocalTranslation(temp, workflow, output, error, "--mode", "meme", "--burn");

        Assert.Equal(0, exit);
        Assert.Contains(fixture.Config!.Steps, step => step.Step == "BuildDualSubtitle");
        Assert.Contains("subtitle.ass", arguments![Array.IndexOf(arguments.ToArray(), "-vf") + 1]);
        Assert.Contains("cultural dual track", subtitleContentsSeenByFfmpeg);
        Assert.False(string.IsNullOrWhiteSpace(workingDirectory));
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Repeated_translation_uses_a_new_task_directory_and_preserves_previous_bilingual_artifacts()
    {
        using var temp = new TemporaryDirectory();
        var fixture = new PipelineFixture();
        var workflow = fixture.CreateWorkflow((_, args, _, _) =>
        {
            File.WriteAllText(args[^1], "burned video");
            return Task.FromResult(0);
        });
        var firstOutput = new StringWriter();
        var firstError = new StringWriter();
        var secondOutput = new StringWriter();
        var secondError = new StringWriter();

        var firstExit = await RunLocalTranslation(temp, workflow, firstOutput, firstError, "--bilingual", "--burn");
        var firstBilingual = fixture.LastBilingualPath!;
        var firstBurned = fixture.LastBurnedPath!;
        var secondExit = await RunLocalTranslation(temp, workflow, secondOutput, secondError, "--bilingual", "--burn");

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        Assert.NotEqual(firstBilingual, fixture.LastBilingualPath);
        Assert.NotEqual(firstBurned, fixture.LastBurnedPath);
        Assert.Contains("Hello there\n你好", await File.ReadAllTextAsync(firstBilingual));
        Assert.Equal("burned video", await File.ReadAllTextAsync(firstBurned));
        Assert.Empty(firstError.ToString());
        Assert.Empty(secondError.ToString());
    }

    private static Task<int> RunLocalTranslation(
        TemporaryDirectory temp,
        ICliWorkflow workflow,
        TextWriter output,
        TextWriter error,
        params string[] optionalArguments)
    {
        var videoPath = Path.Combine(temp.Path, "lesson's,one.mp4");
        if (!File.Exists(videoPath)) File.WriteAllText(videoPath, "input video");
        return CliApplication.RunAsync(
            ["translate", videoPath, "--workspace", Path.Combine(temp.Path, "workspace"), .. optionalArguments],
            workflow, output, error, false, temp.Path);
    }

    private sealed class PipelineFixture
    {
        public PipelineConfig? Config { get; private set; }
        public string? LastSubtitlePath { get; private set; }
        public string? LastBilingualPath { get; private set; }
        public string? LastBurnedPath { get; private set; }

        public PipelineTranslationWorkflow CreateWorkflow(
            Func<string, IReadOnlyList<string>, string, CancellationToken, Task<int>> mediaProcessRunner)
        {
            return new PipelineTranslationWorkflow(
                (_, _) => Task.FromResult<string?>(null),
                (config, context, _) =>
                {
                    Config = config;
                    var dirs = new WorkDirs(context["WorkspacePath"], ".mp4");
                    Directory.CreateDirectory(context["WorkspacePath"]);
                    File.WriteAllText(dirs.NormalizedSubtitlePath,
                        "1\n00:00:00,000 --> 00:00:01,000\nHello there\n\n");
                    File.WriteAllText(dirs.ProofreadSubtitlePath,
                        "1\n00:00:00,000 --> 00:00:01,000\n你好\n\n");
                    if (context["Mode"] == "meme")
                        File.WriteAllText(dirs.DualSubtitlePath, "cultural dual track");
                    LastSubtitlePath = dirs.ProofreadSubtitlePath;
                    LastBilingualPath = Path.Combine(context["WorkspacePath"], "translated_bilingual.srt");
                    LastBurnedPath = Path.Combine(context["WorkspacePath"], "burned_video.mp4");
                    return context;
                },
                mediaProcessRunner);
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
