namespace VideoSubtitleTranslator.Tests;

public class GitignoreTests
{
    [Fact]
    public void Gitignore_contains_dotenv_entry()
    {
        var gitignorePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".gitignore"));

        var lines = File.ReadAllLines(gitignorePath);

        Assert.Contains(lines, l => l.TrimStart() == ".env");
    }
}
