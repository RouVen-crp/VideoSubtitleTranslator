using VideoSubtitleTranslator.Cli;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

[Collection("Shared runtime configuration")]
public sealed class CliProgressTests
{
    [Fact]
    public async Task Translate_reports_stage_order_elapsed_time_and_truthful_heartbeat_to_progress_output()
    {
        using var temp = new TemporaryDirectory();
        var inputVideo = Path.Combine(temp.Path, "lesson.mp4");
        await File.WriteAllTextAsync(inputVideo, "video");
        using var progress = new StringWriter();
        var workflow = new PipelineTranslationWorkflow(
            mediaValidator: (_, _) => Task.FromResult<string?>(null),
            taskStore: new CliTaskStore(Path.Combine(temp.Path, "tasks")),
            progressOutput: progress,
            progressHeartbeatInterval: TimeSpan.FromMilliseconds(15),
            pipelineStepRunner: (step, context, _) =>
            {
                var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
                switch (step.Step)
                {
                    case "SplitAudio": File.WriteAllText(dirs.AudioPath, "audio"); break;
                    case "ExtractSubtitle":
                        Thread.Sleep(60);
                        File.WriteAllText(dirs.SubtitlePath, "raw subtitles");
                        break;
                    case "BuildSentences": File.WriteAllText(dirs.RawSubtitlePath, "sentences"); break;
                    case "NormalizeSubtitle": File.WriteAllText(dirs.NormalizedSubtitlePath, "normalized"); break;
                    case "Translate": File.WriteAllText(dirs.TranslatedSubtitlePath, "translated"); break;
                    case "ProofreadSubtitle": File.WriteAllText(dirs.ProofreadSubtitlePath, "proofread"); break;
                }
                return context;
            });
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var consoleOutput = new StringWriter();
        using var consoleError = new StringWriter();
        var originalConsoleOutput = Console.Out;
        var originalConsoleError = Console.Error;

        int exitCode;
        Console.SetOut(consoleOutput);
        Console.SetError(consoleError);
        try
        {
            exitCode = await CliApplication.RunAsync(
                ["translate", inputVideo, "--workspace", Path.Combine(temp.Path, "workspace")],
                workflow, output, error, false, temp.Path);
        }
        finally
        {
            Console.SetOut(originalConsoleOutput);
            Console.SetError(originalConsoleError);
        }

        Assert.Equal(0, exitCode);
        var messages = progress.ToString();
        AssertOrdered(messages, "提取音频", "转录字幕", "规范化字幕", "翻译字幕", "自动校对");
        Assert.Contains("仍在运行，已用时", messages);
        Assert.Contains("已用时", messages);
        Assert.DoesNotContain("%", messages);
        Assert.DoesNotContain("ETA", messages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("进度：", output.ToString());
        Assert.DoesNotContain("[progress]", consoleOutput.ToString());
    }

    private static void AssertOrdered(string text, params string[] fragments)
    {
        var lastIndex = -1;
        foreach (var fragment in fragments)
        {
            var index = text.IndexOf(fragment, lastIndex + 1, StringComparison.Ordinal);
            Assert.True(index > lastIndex, $"Expected progress to contain '{fragment}' after index {lastIndex}. Actual: {text}");
            lastIndex = index;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
