using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

public class BurnSubtitleDualTrackTests
{
    [Fact]
    public void BuildFfmpegArgs_uses_dual_subtitle_ass_when_provided()
    {
        var dirs = new WorkDirs("/tmp/vid", ".mp4");
        var args = DefaultBurnSubtitleStep.BuildFfmpegArgs(
            dirs, "/tmp/vid/out.mp4", "h264_nvenc", "high", 5000, "/tmp/vid/dual_subtitle.ass");

        Assert.Contains("\"ass='/tmp/vid/dual_subtitle.ass'\"", args);
        Assert.DoesNotContain("subtitles=", args);
    }

    [Theory]
    [InlineData("dual_subtitle.ass", "ass")]
    [InlineData("dual_subtitle.ssa", "ass")]
    [InlineData("translated_subtitle.srt", "subtitles")]
    public void BuildFfmpegArgs_selects_filter_by_subtitle_extension(string fileName, string expectedFilter)
    {
        var dirs = new WorkDirs("/tmp/vid", ".mp4");
        var args = DefaultBurnSubtitleStep.BuildFfmpegArgs(
            dirs, "/tmp/vid/out.mp4", "h264_nvenc", "high", 5000, $"/tmp/vid/{fileName}");

        Assert.Contains($"-vf \"{expectedFilter}='/tmp/vid/{fileName}'\"", args);
        var otherFilter = expectedFilter == "ass" ? "subtitles" : "ass";
        Assert.DoesNotContain($"{otherFilter}=", args);
    }

    [Theory]
    [InlineData("ass")]
    [InlineData("ssa")]
    [InlineData("srt")]
    public void BuildFfmpegArgs_uses_ass_filter_for_ass_like_extensions_in_low_quality_branch(string extension)
    {
        var dirs = new WorkDirs("/tmp/vid", ".mp4");
        var args = DefaultBurnSubtitleStep.BuildFfmpegArgs(
            dirs, "/tmp/vid/out.mp4", "libx264", "auto", null, $"/tmp/vid/subtitle.{extension}");

        var expectedFilter = extension is "ass" or "ssa" ? "ass" : "subtitles";
        Assert.Contains($"-vf \"{expectedFilter}='", args);
        Assert.Contains("-c:a copy", args);
        Assert.DoesNotContain("-b:v", args);
    }

    [Fact]
    public void EscapeSubtitleFilterPath_escapes_single_quotes()
    {
        var escaped = DefaultBurnSubtitleStep.EscapeSubtitleFilterPath("/tmp/it's a subtitle.srt");

        Assert.Equal("/tmp/it'\\''s a subtitle.srt", escaped);
    }

    [Fact]
    public void BuildFfmpegArgs_uses_srt_when_provided()
    {
        var dirs = new WorkDirs("/tmp/vid", ".mp4");
        var args = DefaultBurnSubtitleStep.BuildFfmpegArgs(
            dirs, "/tmp/vid/out.mp4", "h264_nvenc", "auto", null, "/tmp/vid/translated_subtitle.srt");

        Assert.Contains("\"subtitles='/tmp/vid/translated_subtitle.srt'\"", args);
        Assert.Contains("-c:a copy", args);
    }

    [Fact]
    public void BuildFfmpegArgs_escapes_windows_subtitle_filter_path()
    {
        var dirs = new WorkDirs("C:\\videos", ".mp4");
        var args = DefaultBurnSubtitleStep.BuildFfmpegArgs(
            dirs,
            "C:\\videos\\out.mp4",
            "libx264",
            "auto",
            null,
            "D:\\CODE\\subtitles\\translated.srt");

        Assert.Contains("\"subtitles='D\\:/CODE/subtitles/translated.srt'\"", args);
    }

    [Fact]
    public void EnsureFfmpegSucceeded_throws_for_nonzero_exit_code()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => DefaultBurnSubtitleStep.EnsureFfmpegSucceeded(-22));

        Assert.Contains("-22", exception.Message);
    }

    [Fact]
    public void ResolveSubtitlePath_returns_dual_ass_when_exists()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var dirs = new WorkDirs(tmpDir, ".mp4");
            File.WriteAllText(dirs.DualSubtitlePath, "test");

            var result = DefaultBurnSubtitleStep.ResolveSubtitlePath(dirs);

            Assert.Equal(dirs.DualSubtitlePath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolveSubtitlePath_falls_back_to_proofread_then_translated()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var dirs = new WorkDirs(tmpDir, ".mp4");
            File.WriteAllText(dirs.TranslatedSubtitlePath, "test");

            var result = DefaultBurnSubtitleStep.ResolveSubtitlePath(dirs);

            Assert.Equal(dirs.TranslatedSubtitlePath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
