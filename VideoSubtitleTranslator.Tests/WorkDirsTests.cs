namespace VideoSubtitleTranslator.Tests;

public class WorkDirsTests
{
    [Fact]
    public void CulturalAnnotationsPath_is_under_workspace()
    {
        var dirs = new WorkDirs("/tmp/testvid", ".mp4");

        Assert.Equal("/tmp/testvid/cultural_annotations.json", dirs.CulturalAnnotationsPath);
    }

    [Fact]
    public void CulturalTermTablePath_is_under_workspace()
    {
        var dirs = new WorkDirs("/tmp/testvid", ".mp4");

        Assert.Equal("/tmp/testvid/cultural_term_table.txt", dirs.CulturalTermTablePath);
    }

    [Fact]
    public void CulturalContextPath_is_under_workspace()
    {
        var dirs = new WorkDirs("/tmp/testvid", ".mp4");

        Assert.Equal("/tmp/testvid/cultural_context.txt", dirs.CulturalContextPath);
    }

    [Fact]
    public void DualSubtitlePath_is_under_workspace()
    {
        var dirs = new WorkDirs("/tmp/testvid", ".mp4");

        Assert.Equal("/tmp/testvid/dual_subtitle.ass", dirs.DualSubtitlePath);
    }

    [Fact]
    public void Standard_paths_unchanged()
    {
        var dirs = new WorkDirs("/tmp/testvid", ".mkv");

        Assert.Equal("/tmp/testvid/video.mkv", dirs.VideoPath);
        Assert.Equal("/tmp/testvid/audio.wav", dirs.AudioPath);
        Assert.Equal("/tmp/testvid/subtitle.srt", dirs.SubtitlePath);
    }
}
