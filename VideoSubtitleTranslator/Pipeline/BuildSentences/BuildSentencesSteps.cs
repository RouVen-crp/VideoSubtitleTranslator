using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 将逐词字幕切分为按句字幕（步骤层）。
/// </summary>
[PipelineStep("BuildSentences", RequiredKeys = new[] { "WorkspacePath", "VideoExt" })]
public abstract class BuildSentencesStepBase : IPipelineStep
{
    public string Step => "BuildSentences";

    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

/// <summary>
/// 使用现有句子构建逻辑生成 raw_subtitle.txt 和 subtitle.srt（暂时仍委托 SentenceBuilder，后续会内联）。
/// </summary>
[PipelineStep("BuildSentences", Implementation = "Default")]
public sealed class DefaultBuildSentencesStep : BuildSentencesStepBase
{
    private static void Build(WorkDirs dirs)
    {
        if (File.Exists(dirs.RawSubtitlePath))
        {
            Console.WriteLine("Raw subtitle exists, skip build");
            return;
        }

        if (!File.Exists(dirs.SubtitlePath))
            throw new FileNotFoundException($"缺少句级字幕：{dirs.SubtitlePath}");

        var subtitleLines = File.ReadAllLines(dirs.SubtitlePath);
        var sentences = new List<string>();
        for (var i = 0; i + 2 < subtitleLines.Length; i++)
        {
            if (!int.TryParse(subtitleLines[i].Trim(), out _)) continue;
            var text = subtitleLines[i + 2].Trim();
            if (text.Length == 0) continue;
            sentences.Add(text);
            i += 3;
        }

        using var rawWriter = new StreamWriter(dirs.RawSubtitlePath, false, Encoding.UTF8);
        foreach (var sentence in sentences)
        {
            rawWriter.WriteLine(sentence);
        }
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var workspacePath = context["WorkspacePath"];
        var ext = context["VideoExt"];
        var dirs = new WorkDirs(workspacePath, ext);

        Build(dirs);
        return context;
    }
}

