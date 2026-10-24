using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

public sealed class InitWorkspaceTests
{
    [Fact]
    public void Cli_request_uses_a_new_workspace_when_a_video_title_already_exists()
    {
        using var temp = new TemporaryDirectory();
        var existing = Path.Combine(temp.Path, "Lesson_One");
        Directory.CreateDirectory(existing);

        var context = new Dictionary<string, string>
        {
            ["WorkspaceRoot"] = temp.Path,
            ["VideoTitle"] = "Lesson One",
            ["AvoidWorkspaceOverwrite"] = "true"
        };

        new DefaultInitWorkspaceStep().Execute(context);

        Assert.Equal(Path.Combine(temp.Path, "Lesson_One_2"), context["WorkspacePath"]);
        Assert.True(Directory.Exists(existing));
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
