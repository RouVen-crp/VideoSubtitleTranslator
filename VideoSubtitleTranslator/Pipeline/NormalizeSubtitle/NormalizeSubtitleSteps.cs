using System.Text;
using System.Text.Json;

namespace VideoSubtitleTranslator.Pipeline;

[PipelineStep("NormalizeSubtitle", RequiredKeys = new[] { "WorkspacePath", "VideoExt", "VideoTitle" })]
public abstract class NormalizeSubtitleStepBase : IPipelineStep
{
    public string Step => "NormalizeSubtitle";
    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

[PipelineStep("NormalizeSubtitle", Implementation = "DeepSeek")]
public sealed class DeepSeekNormalizeSubtitleStep : NormalizeSubtitleStepBase
{
    private sealed record SubtitleBlock
    {
        public required string Start { get; init; }
        public required string End { get; init; }
        public required string Text { get; init; }
    }

    private sealed class NormalizeDecision
    {
        public bool Merge { get; set; }
    }

    private static List<SubtitleBlock> ParseSrt(string path)
    {
        var lines = File.ReadAllLines(path);
        var list = new List<SubtitleBlock>();
        for (var i = 0; i + 2 < lines.Length; i++)
        {
            if (!int.TryParse(lines[i].Trim(), out _)) continue;
            var time = lines[i + 1];
            var parts = time.Split("-->");
            if (parts.Length != 2) continue;
            var text = lines[i + 2].Trim();
            if (text.Length == 0) continue;
            list.Add(new SubtitleBlock
            {
                Start = parts[0].Trim(),
                End = parts[1].Trim(),
                Text = text
            });
            i += 3;
        }

        return list;
    }

    private static string BuildPrompt(SubtitleBlock a, SubtitleBlock b)
    {
        return PipelineTextUtils.ApplyTemplate(
            PromptProvider.Get("Normalize/user_prompt_template.md"),
            new Dictionary<string, string>
            {
                ["SENTENCE_1"] = a.Text,
                ["SENTENCE_2"] = b.Text
            });
    }

    private static bool IsLikelyGrammarFracture(SubtitleBlock a, SubtitleBlock b)
    {
        var left = a.Text.Trim();
        var right = b.Text.Trim();
        if (left.Length == 0 || right.Length == 0) return false;

        // 右句若以小写/连接词/标点续接开头，说明左句可能被截断。
        var startsWithLower = char.IsLetter(right[0]) && char.IsLower(right[0]);
        var linkerStarts = new[]
        {
            "and ", "or ", "but ", "so ", "because ", "that ", "which ", "who ", "if ", "when ", "then ", "than ", "to "
        };
        var lowerRight = right.ToLowerInvariant();
        var startsWithLinker = linkerStarts.Any(lowerRight.StartsWith);
        var startsWithPunct = ",.;:!?)]}\"'".Contains(right[0]);

        // 左句若以明显未收束结尾（逗号、连词、系动词/介词等），更可能需要合并。
        var lowerLeft = left.ToLowerInvariant();
        var badTail = new[]
        {
            "and", "or", "but", "so", "because", "that", "which", "who", "if", "when", "then", "than", "to", "of", "in", "on", "at", "for", "with", "is", "are", "was", "were", "be"
        };
        var leftEndsWithCommaLike = ",;:(".Contains(left[^1]);
        var leftEndsWithBadTail = badTail.Any(t => lowerLeft.EndsWith(" " + t) || lowerLeft == t);

        return startsWithLower || startsWithLinker || startsWithPunct || leftEndsWithCommaLike || leftEndsWithBadTail;
    }

    private static string MergeTexts(SubtitleBlock a, SubtitleBlock b)
    {
        var left = a.Text.Trim();
        var right = b.Text.Trim();
        if (left.Length == 0) return right;
        if (right.Length == 0) return left;
        if (".,;:!?)]}\"'".Contains(left[^1])) return $"{left} {right}";
        return $"{left} {right}";
    }

    private static NormalizeDecision DecideMerge(SubtitleBlock a, SubtitleBlock b, int maxAttempts)
    {
        var prompt = BuildPrompt(a, b);
        var lastResponse = string.Empty;
        for (var i = 0; i < maxAttempts; i++)
        {
            lastResponse = ApiCaller.CallApi(
                GlobalRuntimeConfig.Current.Llm.Model,
                PromptProvider.Get("Normalize/system_prompt.md").Trim(),
                prompt).Result;
            var json = PipelineTextUtils.ExtractJsonObject(lastResponse);
            if (json is null) continue;
            try
            {
                var decision = JsonSerializer.Deserialize<NormalizeDecision>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (decision is null) continue;
                // 双重防守：即使模型判断可合并，也必须满足本地语法断裂判据。
                if (decision.Merge && !IsLikelyGrammarFracture(a, b))
                    return new NormalizeDecision { Merge = false };
                return new NormalizeDecision { Merge = decision.Merge };
            }
            catch
            {
                var lower = json.ToLowerInvariant();
                if (lower.Contains("\"merge\":false"))
                {
                    return new NormalizeDecision { Merge = false };
                }

                if (lower.Contains("\"merge\":true"))
                {
                    return new NormalizeDecision { Merge = IsLikelyGrammarFracture(a, b) };
                }
            }
        }

        throw new InvalidOperationException(
            $"字幕预处理决策失败：重试 {maxAttempts} 次后仍无法解析合法 JSON。最后响应片段：\n{lastResponse[..Math.Min(1000, lastResponse.Length)]}");
    }

    private static void WriteNormalizedFiles(WorkDirs dirs, List<SubtitleBlock> blocks)
    {
        using (var writer = new StreamWriter(dirs.NormalizedSubtitlePath, false, Encoding.UTF8))
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                writer.WriteLine(i + 1);
                writer.WriteLine($"{blocks[i].Start} --> {blocks[i].End}");
                writer.WriteLine(blocks[i].Text);
                writer.WriteLine();
            }
        }

        using (var writer = new StreamWriter(dirs.NormalizedRawSubtitlePath, false, Encoding.UTF8))
        {
            foreach (var b in blocks)
                writer.WriteLine(b.Text);
        }
    }

    private static void Normalize(WorkDirs dirs)
    {
        if (File.Exists(dirs.NormalizedSubtitlePath) && File.Exists(dirs.NormalizedRawSubtitlePath))
        {
            Console.WriteLine("Normalized subtitle exists, skip normalize");
            return;
        }

        if (!File.Exists(dirs.SubtitlePath))
            throw new FileNotFoundException($"缺少原始句级字幕：{dirs.SubtitlePath}");

        var windowSize = Math.Max(2, GlobalRuntimeConfig.Current.Translation.NormalizeWindowSize);
        if (windowSize != 2)
            Console.WriteLine($"NormalizeWindowSize={windowSize}，当前实现仅支持2，已按2处理。");
        var maxAttempts = Math.Max(1, GlobalRuntimeConfig.Current.Translation.NormalizeJsonMaxAttempts);

        var source = ParseSrt(dirs.SubtitlePath);
        if (source.Count == 0)
            throw new InvalidOperationException("原始句级字幕为空，无法执行规范化处理。");
        var normalized = new List<SubtitleBlock>();
        using var logWriter = new StreamWriter(dirs.SubtitleNormalizeLogPath, false, Encoding.UTF8);
        logWriter.WriteLine($"[start] sourceBlocks={source.Count}, attempts={maxAttempts}, ts={DateTime.UtcNow:O}");
        var mergeCount = 0;

        var left = source[0];
        var nextIndex = 1;
        while (nextIndex < source.Count)
        {
            var right = source[nextIndex];
            var decision = DecideMerge(left, right, maxAttempts);
            if (decision.Merge)
            {
                var mergedText = MergeTexts(left, right);
                var merged = new SubtitleBlock
                {
                    Start = left.Start,
                    End = right.End,
                    Text = mergedText
                };
                left = merged;
                mergeCount++;
                logWriter.WriteLine(
                    $"[merge] left={nextIndex - 1} right={nextIndex}\n  left={source[nextIndex - 1].Text}\n  right={right.Text}\n  merged={mergedText}");
                nextIndex += 1;
                continue;
            }

            normalized.Add(left);
            left = right;
            nextIndex += 1;
        }

        normalized.Add(left);
        logWriter.WriteLine(
            $"[end] sourceBlocks={source.Count}, normalizedBlocks={normalized.Count}, mergedPairs={mergeCount}, ts={DateTime.UtcNow:O}");
        WriteNormalizedFiles(dirs, normalized);
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
        Normalize(dirs);
        return context;
    }
}
