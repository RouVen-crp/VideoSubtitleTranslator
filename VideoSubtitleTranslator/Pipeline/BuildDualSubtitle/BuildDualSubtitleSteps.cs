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

    /// <summary>中文译文（主字幕）：1080 画布高度的 1/20，约 54px。</summary>
    internal const int ChineseFontSize = 54;

    /// <summary>英文原文（副字幕，放在中文上方）。</summary>
    internal const int EnglishFontSize = 38;

    /// <summary>文化注释（最小，放在最下方）。</summary>
    internal const int AnnotationFontSize = 28;

    internal static string BuildAss(
        IReadOnlyList<SrtEntry> srtEntries,
        IReadOnlyList<CulturalAnnotation> annotations,
        IReadOnlyList<SubtitleLine>? sourceLines = null)
    {
        var annotationByIndex = new Dictionary<int, CulturalAnnotation>();
        foreach (var annotation in annotations)
        {
            if (annotation is { NeedsAnnotation: true, Annotation.Length: > 0 })
            {
                annotationByIndex.TryAdd(annotation.Index, annotation);
            }
        }

        // 校对会合并相邻句，中文块与英文源行的条数不再相等；按时间重叠配对，不能按序号。
        var pairedSourceLines = SubtitlePairing.PairTexts(
            srtEntries.Select(entry => new SubtitleLine(
                ParseMilliseconds(entry.Start), ParseMilliseconds(entry.End), entry.Text)).ToList(),
            sourceLines);

        // 先按排版规则断句、分配时长，再渲染。
        var source = srtEntries.Select((entry, position) =>
        {
            var english = pairedSourceLines[position]?.Trim();
            var note = annotationByIndex.TryGetValue(entry.Index, out var found) ? found.Annotation?.Trim() : null;
            return new SubtitleLayout.Cue(
                ParseSeconds(entry.Start),
                ParseSeconds(entry.End),
                entry.Text,
                string.IsNullOrWhiteSpace(english) ? null : english,
                string.IsNullOrWhiteSpace(note) ? null : note);
        }).ToList();
        var cues = SubtitleLayout.Arrange(source);

        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("Title: dual_subtitle");
        sb.AppendLine("ScriptType: v4.00+");
        // 没有 PlayRes 时 libass 会按 384x288 推算，字号被放大到画面装不下、长句冲出屏幕。
        // PlayRes 固定为 1080p，字号按高度比例给出，libass 会等比缩放到实际分辨率。
        sb.AppendLine("PlayResX: 1920");
        sb.AppendLine("PlayResY: 1080");
        sb.AppendLine("WrapStyle: 0");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine($"Style: Translation,Arial,{ChineseFontSize},&H00FFFFFF,&H000000FF,&H00000000,&H80000000,1,0,0,0,100,100,0,0,1,3,0,2,60,60,70,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var cue in cues)
        {
            var lines = new List<string>();

            // 一个事件里按"英文 → 中文 → 注释"排三行；拆成多个事件会触发 libass 的避让重排。
            if (cue.English is not null)
            {
                lines.AddRange(WrapText(cue.English, SubtitleLayout.MaxEnglishCharactersPerLine)
                    .Take(2)
                    .Select(line => $"{{\\fs{EnglishFontSize}\\c&H00E0FFFF&}}{EscapeAssText(line)}"));
            }

            lines.Add($"{{\\fs{ChineseFontSize}\\c&H00FFFFFF&}}{EscapeAssText(cue.Chinese)}");

            if (cue.Annotation is not null)
            {
                lines.Add($"{{\\fs{AnnotationFontSize}\\c&H00AAAAAA&}}{EscapeAssText(cue.Annotation)}");
            }

            sb.AppendLine(
                $"Dialogue: 0,{FormatAssTime(cue.Start)},{FormatAssTime(cue.End)},Translation,,0,0,0,,{string.Join("\\N", lines)}");
        }

        return sb.ToString();
    }

    private static double ParseSeconds(string timestamp)
    {
        return TimeSpan.TryParse(timestamp.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value.TotalSeconds
            : 0;
    }

    internal static int ParseMilliseconds(string timestamp)
    {
        return TimeSpan.TryParse(timestamp.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? (int)Math.Max(0, value.TotalMilliseconds)
            : 0;
    }

    private static string FormatAssTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}.{value.Milliseconds / 10:D2}";
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
    /// 保留时间范围，供 <see cref="SubtitlePairing"/> 与校对合并后的中文块配对。
    /// </summary>
    internal static IReadOnlyList<SubtitleLine>? ReadEnglishLines(WorkDirs dirs)
    {
        if (!File.Exists(dirs.NormalizedSubtitlePath))
        {
            return null;
        }

        return ParseSrt(dirs.NormalizedSubtitlePath)
            .Select(entry => new SubtitleLine(
                ParseMilliseconds(entry.Start), ParseMilliseconds(entry.End), entry.Text))
            .ToList();
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
        RunBuild(dirs);
        return context;
    }
}
