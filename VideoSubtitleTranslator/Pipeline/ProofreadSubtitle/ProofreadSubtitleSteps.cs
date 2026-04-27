using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace VideoSubtitleTranslator.Pipeline;

[PipelineStep("ProofreadSubtitle", RequiredKeys = new[] { "WorkspacePath", "VideoExt", "VideoTitle" })]
public abstract class ProofreadSubtitleStepBase : IPipelineStep
{
    public string Step => "ProofreadSubtitle";
    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

[PipelineStep("ProofreadSubtitle", Implementation = "DeepSeek")]
public sealed class DeepSeekProofreadSubtitleStep : ProofreadSubtitleStepBase
{
    private static string ProofreadSystemPrompt => PromptProvider.Get("Proofread/system_prompt.md").Trim();

    private sealed class SubtitleBlock
    {
        public int Index { get; init; }
        public required string Start { get; init; }
        public required string End { get; init; }
        public required string Text { get; init; }
    }

    private sealed class ProofreadResponse
    {
        [JsonPropertyName("items")]
        public List<ProofreadItem> Items { get; set; } = new();
    }

    private sealed class ProofreadItem
    {
        [JsonPropertyName("start")]
        public string Start { get; set; } = string.Empty;

        [JsonPropertyName("end")]
        public string End { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private static List<SubtitleBlock> ParseSrt(string path)
    {
        var lines = File.ReadAllLines(path);
        var list = new List<SubtitleBlock>();
        var i = 0;
        while (i < lines.Length)
        {
            if (!int.TryParse(lines[i].Trim(), out var idx))
            {
                i++;
                continue;
            }

            if (i + 1 >= lines.Length) break;
            var time = lines[i + 1].Trim();
            var parts = time.Split("-->");
            if (parts.Length != 2)
            {
                i++;
                continue;
            }

            var j = i + 2;
            var textLines = new List<string>();
            while (j < lines.Length && !string.IsNullOrWhiteSpace(lines[j]))
            {
                textLines.Add(lines[j].TrimEnd());
                j++;
            }
            var text = string.Join('\n', textLines).Trim();
            list.Add(new SubtitleBlock
            {
                Index = idx,
                Start = parts[0].Trim(),
                End = parts[1].Trim(),
                Text = text
            });
            i = j + 1;
        }

        return list;
    }

    private static string BuildUserPrompt(SubtitleBlock a, SubtitleBlock b)
    {
        return PipelineTextUtils.ApplyTemplate(
            PromptProvider.Get("Proofread/user_prompt_template.md"),
            new Dictionary<string, string>
            {
                ["A_INDEX"] = a.Index.ToString(),
                ["A_START"] = a.Start,
                ["A_END"] = a.End,
                ["A_TEXT"] = a.Text,
                ["B_INDEX"] = b.Index.ToString(),
                ["B_START"] = b.Start,
                ["B_END"] = b.End,
                ["B_TEXT"] = b.Text
            });
    }

    private static string NormalizeForCompare(string text)
    {
        var chars = text.Where(ch => !char.IsWhiteSpace(ch) && !char.IsPunctuation(ch)).ToArray();
        return new string(chars).ToLowerInvariant();
    }

    private static bool IsLikelyDedupPair(SubtitleBlock a, SubtitleBlock b)
    {
        var na = NormalizeForCompare(a.Text);
        var nb = NormalizeForCompare(b.Text);
        if (na.Length == 0 || nb.Length == 0) return false;
        if (na == nb) return true;
        if (na.Contains(nb) || nb.Contains(na))
        {
            var shorter = Math.Min(na.Length, nb.Length);
            var longer = Math.Max(na.Length, nb.Length);
            return shorter >= Math.Max(6, longer * 6 / 10);
        }

        return false;
    }

    private static bool HasEmptyLineIssue(SubtitleBlock a, SubtitleBlock b) =>
        string.IsNullOrWhiteSpace(a.Text) || string.IsNullOrWhiteSpace(b.Text);

    private static bool ShouldInvokeModel(SubtitleBlock a, SubtitleBlock b) =>
        HasEmptyLineIssue(a, b) || IsLikelyDedupPair(a, b);

    private static string GetMergeReason(SubtitleBlock a, SubtitleBlock b, List<SubtitleBlock> candidate)
    {
        if (candidate.Count != 1) return "not-merged";
        if (HasEmptyLineIssue(a, b))
            return "empty-line-merge";
        if (IsLikelyDedupPair(a, b))
            return "dedup-merge";
        return "reject-merge";
    }

    private static bool IsSuspiciousRewrite(SubtitleBlock a, SubtitleBlock b, List<SubtitleBlock> candidate)
    {
        var originalTotal = a.Text.Trim().Length + b.Text.Trim().Length;
        var outputTotal = candidate.Sum(x => x.Text.Trim().Length);
        if (outputTotal > originalTotal + 24) return true;
        if (candidate.Count == 1 && outputTotal > originalTotal + 16) return true;
        return false;
    }

    private static bool TryParseAndValidate(
        string response,
        SubtitleBlock a,
        SubtitleBlock b,
        out List<SubtitleBlock> normalized)
    {
        normalized = new List<SubtitleBlock>();
        var json = PipelineTextUtils.ExtractJsonObject(response);
        if (json is null) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<ProofreadResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed?.Items is null) return false;
            if (parsed.Items.Count is < 1 or > 2) return false;

            var allowedStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { a.Start, b.Start };
            var allowedEnds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { a.End, b.End };

            if (parsed.Items.Count == 1)
            {
                var only = parsed.Items[0];
                if (only.Start.Trim() != a.Start || only.End.Trim() != b.End) return false;
                var text = only.Text.Trim();
                if (text.Length == 0) return false;
                normalized.Add(new SubtitleBlock { Index = a.Index, Start = a.Start, End = b.End, Text = text });
                return true;
            }

            var i0 = parsed.Items[0];
            var i1 = parsed.Items[1];
            if (i0.Start.Trim() != a.Start || i0.End.Trim() != a.End) return false;
            if (i1.Start.Trim() != b.Start || i1.End.Trim() != b.End) return false;
            if (!allowedStarts.Contains(i0.Start.Trim()) || !allowedStarts.Contains(i1.Start.Trim())) return false;
            if (!allowedEnds.Contains(i0.End.Trim()) || !allowedEnds.Contains(i1.End.Trim())) return false;
            var t0 = i0.Text.Trim();
            var t1 = i1.Text.Trim();
            if (t0.Length == 0 || t1.Length == 0) return false;
            normalized.Add(new SubtitleBlock { Index = a.Index, Start = a.Start, End = a.End, Text = t0 });
            normalized.Add(new SubtitleBlock { Index = b.Index, Start = b.Start, End = b.End, Text = t1 });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteSrt(string path, List<SubtitleBlock> blocks)
    {
        using var sw = new StreamWriter(path, false, Encoding.UTF8);
        for (var i = 0; i < blocks.Count; i++)
        {
            sw.WriteLine(i + 1);
            sw.WriteLine($"{blocks[i].Start} --> {blocks[i].End}");
            foreach (var line in blocks[i].Text.Split('\n'))
                sw.WriteLine(line);
            sw.WriteLine();
        }
    }

    private static void RunProofread(WorkDirs dirs)
    {
        var cfg = GlobalRuntimeConfig.Current.Translation;
        if (!cfg.ProofreadEnabled)
        {
            Console.WriteLine("Proofread disabled by config, skip subtitle proofread");
            return;
        }

        if (cfg.ProofreadWindowSize != 2)
            Console.WriteLine($"ProofreadWindowSize={cfg.ProofreadWindowSize}，当前实现仅支持2，已按2处理。");

        if (!File.Exists(dirs.TranslatedSubtitlePath))
            throw new FileNotFoundException($"缺少翻译字幕：{dirs.TranslatedSubtitlePath}");

        var source = ParseSrt(dirs.TranslatedSubtitlePath);
        if (source.Count == 0)
            throw new InvalidOperationException("翻译字幕为空，无法进行后处理校对。");

        var maxAttempts = Math.Max(1, cfg.ProofreadJsonMaxAttempts);
        var delayMs = Math.Max(0, cfg.ProofreadRequestDelayMs > 0 ? cfg.ProofreadRequestDelayMs : cfg.RequestDelayMs);
        var output = new List<SubtitleBlock>();

        using var logWriter = new StreamWriter(dirs.SubtitleProofreadLogPath, false, Encoding.UTF8);
        logWriter.WriteLine($"[start] blocks={source.Count}, attempts={maxAttempts}, ts={DateTime.UtcNow:O}");
        var mergeCount = 0;
        var rejectCount = 0;
        var invokedCount = 0;

        var left = source[0];
        var nextIndex = 1;
        while (nextIndex < source.Count)
        {
            var right = source[nextIndex];
            if (!ShouldInvokeModel(left, right))
            {
                output.Add(left);
                left = right;
                nextIndex += 1;
                if (delayMs > 0) Thread.Sleep(delayMs);
                continue;
            }

            invokedCount++;
            var prompt = BuildUserPrompt(left, right);
            var parsed = false;
            var lastResponse = string.Empty;
            List<SubtitleBlock> candidate = new();
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                lastResponse = ApiCaller.CallApi(GlobalRuntimeConfig.Current.Llm.Model, ProofreadSystemPrompt, prompt).Result;
                if (TryParseAndValidate(lastResponse, left, right, out candidate) &&
                    !IsSuspiciousRewrite(left, right, candidate))
                {
                    var reason = GetMergeReason(left, right, candidate);
                    if (reason == "reject-merge")
                    {
                        rejectCount++;
                        candidate = new List<SubtitleBlock>
                        {
                            new() { Index = left.Index, Start = left.Start, End = left.End, Text = left.Text },
                            new() { Index = right.Index, Start = right.Start, End = right.End, Text = right.Text }
                        };
                        logWriter.WriteLine(
                            $"[reject] left={left.Index} right={right.Index} reason={reason} -> fallback=keep-two\n  left={left.Text}\n  right={right.Text}");
                    }
                    parsed = true;
                    break;
                }
                Thread.Sleep(delayMs);
            }

            if (!parsed)
                throw new InvalidOperationException(
                    $"字幕后处理校对失败：窗口 {nextIndex - 1}/{source.Count} 在 {maxAttempts} 次后仍无法通过校验。最后响应片段：\n{lastResponse[..Math.Min(1200, lastResponse.Length)]}");

            if (candidate.Count == 1)
            {
                var merged = new SubtitleBlock
                {
                    Index = left.Index,
                    Start = candidate[0].Start,
                    End = candidate[0].End,
                    Text = candidate[0].Text
                };
                var reason = GetMergeReason(left, right, candidate);
                mergeCount++;
                logWriter.WriteLine(
                    $"[merge] left={left.Index} right={right.Index} reason={reason} -> {left.Index}+{right.Index}\n  left={left.Text}\n  right={right.Text}\n  merged={merged.Text}");
                left = merged;
                nextIndex += 1;
            }
            else
            {
                output.Add(new SubtitleBlock
                {
                    Index = left.Index,
                    Start = candidate[0].Start,
                    End = candidate[0].End,
                    Text = candidate[0].Text
                });
                left = new SubtitleBlock
                {
                    Index = right.Index,
                    Start = candidate[1].Start,
                    End = candidate[1].End,
                    Text = candidate[1].Text
                };
                nextIndex += 1;
            }

            if (delayMs > 0) Thread.Sleep(delayMs);
        }

        output.Add(left);
        logWriter.WriteLine(
            $"[end] inputBlocks={source.Count}, outputBlocks={output.Count}, modelInvocations={invokedCount}, mergedPairs={mergeCount}, rejectedMerges={rejectCount}, ts={DateTime.UtcNow:O}");
        WriteSrt(dirs.ProofreadSubtitlePath, output);
        WriteSrt(dirs.TranslatedSubtitlePath, output);
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
        RunProofread(dirs);
        return context;
    }
}

