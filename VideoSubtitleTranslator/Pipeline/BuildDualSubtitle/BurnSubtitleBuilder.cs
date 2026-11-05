using System.Text;
using System.Text.Json;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 给烧录用的双语 ASS：英文原文 + 中文译文（meme 模式再加文化注释）。
/// 烧录默认走这里，而不是直接烧中文 SRT，这样两种模式都能得到中英对照。
/// </summary>
public static class BurnSubtitleBuilder
{
    /// <summary>
    /// 从任务目录里的字幕文件合成双语 ASS 内容；中文译文缺失时返回 null（由调用方回退）。
    /// </summary>
    public static string? TryBuild(WorkDirs dirs)
    {
        var chinesePath = File.Exists(dirs.ProofreadSubtitlePath) ? dirs.ProofreadSubtitlePath
            : File.Exists(dirs.TranslatedSubtitlePath) ? dirs.TranslatedSubtitlePath
            : null;
        if (chinesePath is null)
        {
            return null;
        }

        var chinese = DefaultBuildDualSubtitleStep.ParseSrt(chinesePath);
        if (chinese.Count == 0)
        {
            return null;
        }

        var english = DefaultBuildDualSubtitleStep.ReadEnglishLines(dirs);
        var annotations = ReadAnnotations(dirs);
        return DefaultBuildDualSubtitleStep.BuildAss(chinese, annotations, english);
    }

    private static List<DefaultBuildDualSubtitleStep.CulturalAnnotation> ReadAnnotations(WorkDirs dirs)
    {
        if (!File.Exists(dirs.CulturalAnnotationsPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(dirs.CulturalAnnotationsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<DefaultBuildDualSubtitleStep.CulturalAnnotation>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (JsonException)
        {
            // 注释文件损坏不应该让烧录失败，退化为纯双语字幕。
            return [];
        }
    }
}
