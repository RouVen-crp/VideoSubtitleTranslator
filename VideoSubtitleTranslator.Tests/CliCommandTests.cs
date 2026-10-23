using System.Text;
using VideoSubtitleTranslator.Cli;

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
        Assert.Equal(Path.Combine(temp.Path, "VideoSubtitleTranslator", "output"), workflow.Request?.WorkspaceRoot);
        Assert.Contains(subtitlePath, output.ToString());
        Assert.Empty(error.ToString());
        Assert.Equal("1\n00:00:00,000 --> 00:00:01,000\n你好\n", await File.ReadAllTextAsync(subtitlePath));
    }

    [Fact]
    public async Task Help_is_available_without_running_the_translation_workflow()
    {
        var workflow = new RecordingTranslationWorkflow(Path.Combine(Path.GetTempPath(), "unused.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["translate", "--help"], workflow, output, error, false, "/tmp");

        Assert.Equal(0, exitCode);
        Assert.Contains("translate <URL>", output.ToString());
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
        var exitCode = await CliApplication.RunAsync(args, workflow, output, error, false, "/tmp");

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
        Assert.Contains("zimu translate <URL>", error.ToString());
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

    private sealed class RecordingTranslationWorkflow(string subtitlePath) : ITranslationWorkflow
    {
        public TranslationRequest? Request { get; private set; }
        public Exception? Failure { get; init; }

        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            if (Failure is not null) throw Failure;
            Directory.CreateDirectory(Path.GetDirectoryName(subtitlePath)!);
            File.WriteAllText(subtitlePath, "1\n00:00:00,000 --> 00:00:01,000\n你好\n", Encoding.UTF8);
            return Task.FromResult(new TranslationResult(subtitlePath));
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
