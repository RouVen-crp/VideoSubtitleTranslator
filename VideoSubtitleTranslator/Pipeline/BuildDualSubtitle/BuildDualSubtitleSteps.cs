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

    internal static string BuildAss(IReadOnlyList<SrtEntry> srtEntries, IReadOnlyList<CulturalAnnotation> annotations)
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
        sb.AppendLine("Style: Translation,Arial,44,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,1,0,0,0,100,100,0,0,1,2,0,2,60,60,60,1");
        sb.AppendLine("Style: Annotation,Arial,30,&H00AAAAAA,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,1,0,2,60,60,20,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        for (var i = 0; i < srtEntries.Count; i++)
        {
            var entry = srtEntries[i];
            var text = EscapeAssText(entry.Text);
            var annotation = annotationByIndex.TryGetValue(entry.Index, out var found) ? found : null;

            var dialogueText = annotation is { NeedsAnnotation: true, Annotation.Length: > 0 }
                ? $"{text}\\N{{\\fs30\\c&HAAAAAA&}}{EscapeAssText(annotation.Annotation)}"
                : text;

            sb.AppendLine($"Dialogue: 0,{ToAssTimestamp(entry.Start)},{ToAssTimestamp(entry.End)},Translation,,0,0,0,,{dialogueText}");
        }

        return sb.ToString();
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

        var ass = BuildAss(srtEntries, annotations);
        File.WriteAllText(dirs.DualSubtitlePath, ass, Encoding.UTF8);
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
        RunBuild(dirs);
        return context;
    }
}
