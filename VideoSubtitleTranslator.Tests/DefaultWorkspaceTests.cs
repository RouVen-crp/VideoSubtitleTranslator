namespace VideoSubtitleTranslator.Tests;

public class DefaultWorkspaceTests
{
    [Fact]
    public void Repository_root_defaults_to_project_output_directory()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = Program.ResolveDefaultWorkspace(repositoryRoot);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(repositoryRoot, "VideoSubtitleTranslator", "output")),
            result);
    }

    [Fact]
    public void Project_directory_defaults_to_its_output_directory()
    {
        var projectDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "VideoSubtitleTranslator.csproj");
        File.WriteAllText(projectFile, "<Project />");

        try
        {
            var result = Program.ResolveDefaultWorkspace(projectDirectory);

            Assert.Equal(Path.GetFullPath(Path.Combine(projectDirectory, "output")), result);
        }
        finally
        {
            Directory.Delete(projectDirectory, recursive: true);
        }
    }
}
