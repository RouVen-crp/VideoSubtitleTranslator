using System.Text;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

/// <summary>
/// 烧录默认出中英双语：英文原文一行、中文译文一行，meme 再加一行文化注释。
/// </summary>
public sealed class BurnSubtitleBuilderTests
{
    [Fact]
    public void Bilingual_ass_contains_english_chinese_and_annotation_with_smaller_fonts()
    {
        using var temp = new TemporaryDirectory();
        var dirs = CreateWorkspace(temp.Path, withAnnotations: true);

        var ass = BurnSubtitleBuilder.TryBuild(dirs);

        Assert.NotNull(ass);
        // 三行同属一个事件：英文（小）在上、中文（主）居中、注释（最小）在下。
        Assert.Contains($"\\fs{DefaultBuildDualSubtitleStep.EnglishFontSize}", ass);
        Assert.Contains($"\\fs{DefaultBuildDualSubtitleStep.ChineseFontSize}", ass);
        Assert.Contains($"\\fs{DefaultBuildDualSubtitleStep.AnnotationFontSize}", ass);
        Assert.Contains("So this is the girl.", ass);
        Assert.Contains("所以这就是那个女孩。", ass);
        Assert.Contains("指抗癌化疗导致脱发", ass);
        Assert.True(ass.IndexOf("So this is the girl.", StringComparison.Ordinal) <
                    ass.IndexOf("所以这就是那个女孩。", StringComparison.Ordinal));
        Assert.True(ass.IndexOf("所以这就是那个女孩。", StringComparison.Ordinal) <
                    ass.IndexOf("指抗癌化疗导致脱发", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_english_subtitle_the_burn_falls_back_to_chinese_only()
    {
        using var temp = new TemporaryDirectory();
        var dirs = CreateWorkspace(temp.Path, withAnnotations: false);
        File.Delete(dirs.NormalizedSubtitlePath);

        var ass = BurnSubtitleBuilder.TryBuild(dirs);

        Assert.NotNull(ass);
        // 没有英文就只烧中文，不应出现英文原文。
        Assert.DoesNotContain("So this is the girl.", ass);
        Assert.Contains("所以这就是那个女孩。", ass);
    }

    [Fact]
    public void Missing_chinese_subtitle_returns_null_so_the_caller_can_fall_back()
    {
        using var temp = new TemporaryDirectory();
        var dirs = CreateWorkspace(temp.Path, withAnnotations: false);
        File.Delete(dirs.TranslatedSubtitlePath);

        Assert.Null(BurnSubtitleBuilder.TryBuild(dirs));
    }

    private static WorkDirs CreateWorkspace(string root, bool withAnnotations)
    {
        var workspace = Path.Combine(root, "task");
        Directory.CreateDirectory(workspace);
        var dirs = new WorkDirs(workspace, ".mp4");
        File.WriteAllText(dirs.NormalizedSubtitlePath, "1\n00:00:00,000 --> 00:00:07,880\nSo this is the girl.\n\n", Encoding.UTF8);
        File.WriteAllText(dirs.TranslatedSubtitlePath, "1\n00:00:00,000 --> 00:00:07,880\n所以这就是那个女孩。\n\n", Encoding.UTF8);
        if (withAnnotations)
        {
            File.WriteAllText(dirs.CulturalAnnotationsPath,
                "[{\"index\":1,\"needsAnnotation\":true,\"annotation\":\"指抗癌化疗导致脱发\"}]", Encoding.UTF8);
        }
        return dirs;
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
