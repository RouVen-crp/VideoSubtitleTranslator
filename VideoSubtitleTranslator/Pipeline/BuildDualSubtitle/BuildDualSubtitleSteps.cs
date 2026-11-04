using System.Text;
using System.Text.Json;

namespace VideoSubtitleTranslator.Pipeline;

[PipelineStep("BuildDualSubtitle", RequiredKeys = new[] { "WorkspacePath", "VideoExt" })]
public abstract class BuildDualSubtitleStepBase : IPipelineStep
{
    public string Step => "BuildDualSubtitle";
    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

[PipelineStep("BuildDualSubtitle", Implementation = "Default")]
public sealed class DefaultBuildDualSubtitleStep : BuildDualSubtitleStepBase
{
    public sealed class CulturalAnnotation
    {
        public int Index { get; set; }
        public bool NeedsAnnotation { get; set; }
        public string Annotation { get; set; } = string.Empty;
    }

    public sealed class SrtEntry
    {
        public int Index { get; init; }
        public required string Start { get; init; }
        public required string End { get; init; }
        public required string Text { get; init; }
    }

    internal static List<SrtEntry> ParseSrt(string path)
    {
        var lines = File.ReadAllLines(path);
        var list = new List<SrtEntry>();
        var idx = 0;
        for (var i = 0; i + 2 < lines.Length; i++)
        {
            if (!int.TryParse(lines[i].Trim(), out _)) continue;
            idx++;
            var time = lines[i + 1];
            var parts = time.Split("-->");
            if (parts.Length != 2) continue;
            var text = lines[i + 2].Trim();
            if (text.Length == 0) continue;
            list.Add(new SrtEntry
            {
                Index = idx,
                Start = parts[0].Trim().Replace(',', '.'),
                End = parts[1].Trim().Replace(',', '.'),
                Text = text
            });
            i += 3;
        }
        return list;
    }

    /// <summary>中文译文（主字幕）。</summary>
    internal const int ChineseFontSize = 32;

    /// <summary>英文原文（副字幕，放在中文上方）。</summary>
    internal const int EnglishFontSize = 24;

    /// <summary>文化注释（最小，放在最下方）。</summary>
    internal const int AnnotationFontSize = 20;

    internal static string BuildAss(
        IReadOnlyList<SrtEntry> srtEntries,
        IReadOnlyList<CulturalAnnotation> annotations,
        IReadOnlyList<string>? englishLines = null)
    {
        var annotationByIndex = new Dictionary<int, CulturalAnnotation>();
        foreach (var annotation in annotations)
        {
            if (annotation is { NeedsAnnotation: true, Annotation.Length: > 0 })
            {
                annotationByIndex.TryAdd(annotation.Index, annotation);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("Title: dual_subtitle");
        sb.AppendLine("ScriptType: v4.00+");
        // 没有 PlayRes 时 libass 会按 384x288 推算，字号被放大到画面装不下、长句冲出屏幕。
        // 固定 1920x1080 画布后由 libass 等比缩放到实际分辨率。
        sb.AppendLine("PlayResX: 1920");
        sb.AppendLine("PlayResY: 1080");
        sb.AppendLine("WrapStyle: 0");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine($"Style: Translation,Arial,{ChineseFontSize},&H00FFFFFF,&H000000FF,&H00000000,&H80000000,1,0,0,0,100,100,0,0,1,2,0,2,60,60,60,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        for (var i = 0; i < srtEntries.Count; i++)
        {
            var entry = srtEntries[i];
            var timestamp = $"{ToAssTimestamp(entry.Start)},{ToAssTimestamp(entry.End)}";
            var lines = new List<string>();

            // 一个事件里按"英文 → 中文 → 注释"的顺序排三行，行距和顺序完全由这里决定；
            // 拆成多个事件会触发 libass 的避让逻辑，导致行序不可控。
            if (englishLines is not null && entry.Index - 1 < englishLines.Count)
            {
                var english = englishLines[entry.Index - 1].Trim();
                if (english.Length > 0)
                {
                    lines.AddRange(WrapText(english, 72)
                        .Select(line => $"{{\\fs{EnglishFontSize}\\c&H00D0D0D0&}}{EscapeAssText(line)}"));
                }
            }

            lines.Add($"{{\\fs{ChineseFontSize}\\c&H00FFFFFF&}}{EscapeAssText(entry.Text)}");

            if (annotationByIndex.TryGetValue(entry.Index, out var annotation) &&
                annotation is { NeedsAnnotation: true, Annotation.Length: > 0 })
            {
                lines.Add($"{{\\fs{AnnotationFontSize}\\c&H00AAAAAA&}}{EscapeAssText(annotation.Annotation)}");
            }

            sb.AppendLine($"Dialogue: 0,{timestamp},Translation,,0,0,0,,{string.Join("\\N", lines)}");
        }

        return sb.ToString();
    }

    /// <summary>按词边界把过长的一行拆开，避免英文句子冲出画面右侧。</summary>
    internal static IReadOnlyList<string> WrapText(string text, int maxCharacters)
    {
        if (text.Length <= maxCharacters)
        {
            return [text];
        }

        var result = new List<string>();
        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxCharacters)
            {
                result.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    /// <summary>
    /// ASS 的时间戳是 <c>h:mm:ss.cc</c>（两位厘秒），SRT 是 <c>hh:mm:ss,mmm</c>（三位毫秒）。
    /// 直接沿用毫秒写法会让 libass 按错误精度解析，字幕整体偏移。
    /// </summary>
    internal static string ToAssTimestamp(string srtTimestamp)
    {
        var normalized = srtTimestamp.Replace(',', '.');
        if (!TimeSpan.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture, out var time))
        {
            return normalized;
        }

        var centiseconds = time.Milliseconds / 10;
        return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}.{centiseconds:D2}";
    }

    internal static string EscapeAssText(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\n", "\\N").Replace("\r", "");
    }

    private void RunBuild(WorkDirs dirs)
    {
        var translatedSrtPath = File.Exists(dirs.ProofreadSubtitlePath) ? dirs.ProofreadSubtitlePath : dirs.TranslatedSubtitlePath;
        if (!File.Exists(translatedSrtPath))
            throw new FileNotFoundException($"Missing translated subtitle: {translatedSrtPath}");

        var annotationsPath = dirs.CulturalAnnotationsPath;
        if (!File.Exists(annotationsPath))
            throw new FileNotFoundException($"Missing cultural annotations: {annotationsPath}");

        var srtEntries = ParseSrt(translatedSrtPath);
        var annotationsJson = File.ReadAllText(annotationsPath);
        var annotations = JsonSerializer.Deserialize<List<CulturalAnnotation>>(annotationsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<CulturalAnnotation>();

        var ass = BuildAss(srtEntries, annotations, ReadEnglishLines(dirs));
        File.WriteAllText(dirs.DualSubtitlePath, ass, Encoding.UTF8);
    }

    /// <summary>
    /// 英文原文行（用于把中英双语一起烧进视频）。缺文件时返回 null，退化为只有中文。
    /// </summary>
    internal static IReadOnlyList<string>? ReadEnglishLines(WorkDirs dirs)
    {
        if (!File.Exists(dirs.NormalizedSubtitlePath))
        {
            return null;
        }

        return ParseSrt(dirs.NormalizedSubtitlePath).Select(entry => entry.Text).ToList();
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
        RunBuild(dirs);
        return context;
    }
}
