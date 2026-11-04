namespace VideoSubtitleTranslator.Tests;

public sealed class WorkspaceNamingTests
{
    [Theory]
    [InlineData("Huge Streamer Just Nuked Her Career..", "Huge_Streamer_Just_Nuked_Her_Career")]
    [InlineData("  padded title  ", "padded_title")]
    [InlineData("quote\"comma,apostrophe' name", "quotecommaapostrophe_name")]
    [InlineData("CON", "_CON")]
    [InlineData("con", "_con")]
    [InlineData("", "video")]
    [InlineData("   ", "video")]
    [InlineData("..", "video")]
    [InlineData("a/../b", "a..b")]
    public void Titles_become_directory_names_that_Windows_keeps_verbatim(string title, string expected)
    {
        Assert.Equal(expected, WorkspaceNaming.ToSafeDirectoryName(title));
    }

    [Fact]
    public void Long_titles_are_truncated_without_leaving_a_trailing_dot()
    {
        var title = new string('x', 200) + "...";

        var name = WorkspaceNaming.ToSafeDirectoryName(title);

        Assert.True(name.Length <= 80, $"name length was {name.Length}");
        Assert.False(name.EndsWith('.'), "truncation must not leave a trailing dot");
    }

    [Fact]
    public void Generated_names_never_end_with_a_dot_or_space()
    {
        foreach (var title in new[] { "video.", "video..", "video . ", "video...   " })
        {
            var name = WorkspaceNaming.ToSafeDirectoryName(title);

            Assert.False(name.EndsWith('.') || name.EndsWith(' '), $"'{title}' produced '{name}'");
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }
}
