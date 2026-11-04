using VideoSubtitleTranslator.Cli;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

/// <summary>
/// 本地视频输入的上下文必须穿过 SecurityFilter/InitWorkspace 的真实实现。
/// 这些测试不使用步骤替身，因为替身正是此前漏掉该回归的原因。
/// </summary>
[Collection("Shared runtime configuration")]
public sealed class LocalWorkflowContextTests
{
    [Fact]
    public void Security_filter_keeps_local_video_inputs_and_drops_unlisted_keys()
    {
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Url"] = "C:/videos/lesson.mp4",
            ["WorkspaceRoot"] = "C:/out",
            ["ConfigPath"] = "C:/app/pipeline.config.json",
            ["VideoTitle"] = "lesson",
            ["WorkspacePath"] = "C:/out/lesson_2",
            ["VideoExt"] = ".mp4",
            ["VideoAuthor"] = string.Empty,
            ["VideoUploadDate"] = string.Empty,
            ["AvoidWorkspaceOverwrite"] = "true",
            ["UntrustedKey"] = "dropped"
        };

        var cleaned = new DefaultSecurityFilterStep().Execute(context);

        Assert.Equal("lesson", cleaned["VideoTitle"]);
        Assert.Equal("C:/out/lesson_2", cleaned["WorkspacePath"]);
        Assert.Equal(".mp4", cleaned["VideoExt"]);
        Assert.Equal("true", cleaned["AvoidWorkspaceOverwrite"]);
        Assert.Equal("C:/out", cleaned["WorkspaceRoot"]);
        Assert.False(cleaned.ContainsKey("UntrustedKey"));
    }

    [Fact]
    public void Real_security_filter_then_init_workspace_keeps_the_decided_local_workspace()
    {
        var runner = new PipelineRunner(new PipelineRegistry());
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Url"] = "C:/videos/lesson.mp4",
            ["WorkspaceRoot"] = "C:/out",
            ["ConfigPath"] = "C:/app/pipeline.config.json",
            ["VideoTitle"] = "lesson",
            ["WorkspacePath"] = "C:/out/lesson_2",
            ["VideoExt"] = ".mp4",
            ["VideoAuthor"] = string.Empty,
            ["VideoUploadDate"] = string.Empty
        };

        context = runner.RunStep(new PipelineStepConfig { Step = "SecurityFilter", Implementation = "Default" }, context);
        context = runner.RunStep(new PipelineStepConfig { Step = "InitWorkspace", Implementation = "Default" }, context);

        Assert.Equal("C:/out/lesson_2", context["WorkspacePath"]);
        Assert.Equal(".mp4", context["VideoExt"]);
        Assert.Equal("lesson", context["VideoTitle"]);
    }

    [Fact]
    public void Init_workspace_keeps_a_workspace_path_already_decided_by_the_caller()
    {
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["VideoTitle"] = "lesson",
            ["WorkspaceRoot"] = "C:/out",
            ["WorkspacePath"] = "C:/out/lesson_2"
        };

        var result = new DefaultInitWorkspaceStep().Execute(context);

        Assert.Equal("C:/out/lesson_2", result["WorkspacePath"]);
    }

    [Fact]
    public void Init_workspace_computes_a_fresh_path_when_the_caller_did_not_decide_one()
    {
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["VideoTitle"] = "my lesson",
            ["WorkspaceRoot"] = "C:/out"
        };

        var result = new DefaultInitWorkspaceStep().Execute(context);

        Assert.Equal(Path.Combine("C:/out", "my_lesson"), result["WorkspacePath"]);
    }

    [Fact]
    public async Task Local_translation_completes_through_the_real_context_steps()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "lesson.mp4");
        await File.WriteAllTextAsync(videoPath, "video");
        var realRunner = new PipelineRunner(new PipelineRegistry());
        var workflow = new PipelineTranslationWorkflow(
            mediaValidator: (_, _) => Task.FromResult<string?>(null),
            pipelineStepRunner: (step, context, _) =>
            {
                // 只让上下文相关的步骤走真实实现与真实必填校验；媒体/模型步骤保持替身。
                if (step.Step is "SecurityFilter" or "InitWorkspace")
                    return realRunner.RunStep(step, context);

                Directory.CreateDirectory(context["WorkspacePath"]);
                var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
                File.WriteAllText(dirs.NormalizedSubtitlePath, "1\n00:00:00,000 --> 00:00:01,000\nHello\n\n");
                File.WriteAllText(dirs.ProofreadSubtitlePath, "1\n00:00:00,000 --> 00:00:01,000\n你好\n\n");
                return context;
            },
            taskStore: new CliTaskStore(Path.Combine(temp.Path, "tasks")));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await CliApplication.RunAsync(
            ["translate", videoPath, "--workspace", Path.Combine(temp.Path, "workspace")],
            workflow, output, error, isInteractive: false, currentDirectory: temp.Path);

        Assert.Equal(0, exit);
        Assert.Contains("translated_subtitle.proofread.srt", output.ToString());
        Assert.Empty(error.ToString());
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
