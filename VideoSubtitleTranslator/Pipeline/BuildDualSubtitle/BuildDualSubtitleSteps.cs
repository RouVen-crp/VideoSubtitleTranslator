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
            list.Add(new SrtEntry { Index = idx, Start = parts[0].Trim().Replace(',', '.'), End = parts[1].Trim().Replace(',', '.'), Text = text });
            i += 3;
        }
        return list;
    }

    internal static string BuildAss(IReadOnlyList<SrtEntry> srtEntries, IReadOnlyList<CulturalAnnotation> annotations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("Title: dual_subtitle");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine("Style: Translation,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,1,0,0,0,100,100,0,0,1,2,0,2,10,10,40,1");
        sb.AppendLine("Style: Annotation,Arial,14,&H00AAAAAA,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,1,0,2,10,10,10,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        for (var i = 0; i < srtEntries.Count; i++)
        {
            var entry = srtEntries[i];
            var text = EscapeAssText(entry.Text);
            var annotation = annotations.FirstOrDefault(a => a.Index == entry.Index && a.NeedsAnnotation);

            var dialogueText = annotation is { NeedsAnnotation: true, Annotation.Length: > 0 }
                ? $"{text}\\N{{\\fs14\\c&HAAAAAA&}}{EscapeAssText(annotation.Annotation)}"
                : text;

            sb.AppendLine($"Dialogue: 0,{entry.Start},{entry.End},Translation,,0,0,0,,{dialogueText}");
        }

        return sb.ToString();
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
