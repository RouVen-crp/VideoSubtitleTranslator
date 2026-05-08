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

        Assert.Contains("subtitles='/tmp/vid/dual_subtitle.ass'", args);
    }

    [Fact]
    public void BuildFfmpegArgs_uses_srt_when_provided()
    {
        var dirs = new WorkDirs("/tmp/vid", ".mp4");
        var args = DefaultBurnSubtitleStep.BuildFfmpegArgs(
            dirs, "/tmp/vid/out.mp4", "h264_nvenc", "auto", null, "/tmp/vid/translated_subtitle.srt");

        Assert.Contains("subtitles='/tmp/vid/translated_subtitle.srt'", args);
        Assert.Contains("-c:a copy", args);
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
