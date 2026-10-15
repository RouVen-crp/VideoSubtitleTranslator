namespace VideoSubtitleTranslator.Tests;

public class WorkDirsTests
{
    [Fact]
    public void CulturalAnnotationsPath_is_under_workspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "testvid");
        var dirs = new WorkDirs(workspace, ".mp4");

        Assert.Equal(Path.Combine(workspace, "cultural_annotations.json"), dirs.CulturalAnnotationsPath);
    }

    [Fact]
    public void CulturalTermTablePath_is_under_workspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "testvid");
        var dirs = new WorkDirs(workspace, ".mp4");

        Assert.Equal(Path.Combine(workspace, "cultural_term_table.txt"), dirs.CulturalTermTablePath);
    }

    [Fact]
    public void CulturalContextPath_is_under_workspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "testvid");
        var dirs = new WorkDirs(workspace, ".mp4");

        Assert.Equal(Path.Combine(workspace, "cultural_context.txt"), dirs.CulturalContextPath);
    }

    [Fact]
    public void DualSubtitlePath_is_under_workspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "testvid");
        var dirs = new WorkDirs(workspace, ".mp4");

        Assert.Equal(Path.Combine(workspace, "dual_subtitle.ass"), dirs.DualSubtitlePath);
    }

    [Fact]
    public void Standard_paths_unchanged()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "testvid");
        var dirs = new WorkDirs(workspace, ".mkv");

        Assert.Equal(Path.Combine(workspace, "video.mkv"), dirs.VideoPath);
        Assert.Equal(Path.Combine(workspace, "audio.wav"), dirs.AudioPath);
        Assert.Equal(Path.Combine(workspace, "subtitle.srt"), dirs.SubtitlePath);
    }
}
