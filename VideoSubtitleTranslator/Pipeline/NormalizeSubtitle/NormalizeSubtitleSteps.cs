using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    private sealed class SubtitleBlock
    {
        public required string Start { get; init; }
        public required string End { get; init; }
        public required string Text { get; init; }
    }

    private sealed class NormalizeDecision
    {
        public bool Merge { get; set; }
        public string MergedText { get; set; } = string.Empty;
    }

    private sealed class NormalizeProgressState
    {
        public int SourceBlocks;
        public int CurrentIndex;
        public int CurrentAttempt;
        public int MaxAttempts;
        public int OutputBlocks;
        public string Stage = "init";
    }

    private static string? ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = s.IndexOf('\n');
            if (firstNl > 0)
            {
                s = s[(firstNl + 1)..].TrimStart();
                var fence = s.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0) s = s[..fence].TrimEnd();
            }
        }

        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return s.Substring(start, end - start + 1);
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
        var template = PromptProvider.Get("Normalize/user_prompt_template.txt");
        return template
            .Replace("{{SENTENCE_1}}", a.Text, StringComparison.Ordinal)
            .Replace("{{SENTENCE_2}}", b.Text, StringComparison.Ordinal);
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

    private static bool IsOverMerged(string mergedText, SubtitleBlock a, SubtitleBlock b)
    {
        // 合并后长度超过双句总长度太多（通常是模型改写扩写），直接拒绝。
        var rawJoinedLength = a.Text.Length + 1 + b.Text.Length;
        if (mergedText.Length > rawJoinedLength + 20) return true;
        // 绝对长度上限，避免大段合并。
        if (mergedText.Length > 260) return true;
        return false;
    }

    private static NormalizeDecision DecideMerge(SubtitleBlock a, SubtitleBlock b, int maxAttempts, NormalizeProgressState? progress = null)
    {
        var prompt = BuildPrompt(a, b);
        var lastResponse = string.Empty;
        for (var i = 0; i < maxAttempts; i++)
        {
            if (progress is not null)
            {
                progress.CurrentAttempt = i + 1;
                progress.Stage = "requesting-model";
            }
            lastResponse = ApiCaller.CallApi(
                GlobalRuntimeConfig.Current.Llm.Model,
                PromptProvider.Get("Normalize/system_prompt.txt").Trim(),
                prompt).Result;
            if (progress is not null) progress.Stage = "parsing-response";
            var json = ExtractJsonObject(lastResponse);
            if (json is null) continue;
            try
            {
                var decision = JsonSerializer.Deserialize<NormalizeDecision>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (decision is null) continue;
                if (!decision.Merge)
                {
                    decision.MergedText = string.Empty;
                    return decision;
                }

                decision.MergedText = decision.MergedText.Trim();
                if (decision.MergedText.Length == 0) continue;
                // 仅在语法断裂迹象明显时才允许合并；否则强制不合并。
                if (!IsLikelyGrammarFracture(a, b))
                {
                    return new NormalizeDecision
                    {
                        Merge = false,
                        MergedText = string.Empty
                    };
                }

                // 防止大段过度合并。
                if (IsOverMerged(decision.MergedText, a, b))
                {
                    return new NormalizeDecision
                    {
                        Merge = false,
                        MergedText = string.Empty
                    };
                }
                return decision;
            }
            catch
            {
                // 宽松兜底：部分模型会在 reason 中输出未转义引号，导致 JSON 无法严格解析。
                // 对 merge=false 的场景，优先按关键词判定，避免不必要中断。
                var lower = json.ToLowerInvariant();
                if (lower.Contains("\"merge\":false"))
                {
                    return new NormalizeDecision
                    {
                        Merge = false,
                        MergedText = string.Empty
                    };
                }
                if (lower.Contains("\"merge\":true"))
                {
                    // 尝试从 mergedText 提取（非常宽松）；提取失败则继续重试。
                    var key = "\"mergedText\"";
                    var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var colon = json.IndexOf(':', idx);
                        if (colon > 0)
                        {
                            var tail = json[(colon + 1)..].TrimStart();
                            if (tail.StartsWith("\"", StringComparison.Ordinal))
                            {
                                var end = tail.IndexOf("\",", StringComparison.Ordinal);
                                if (end < 0) end = tail.LastIndexOf('"');
                                if (end > 1)
                                {
                                    var merged = tail[1..end].Trim();
                                    if (merged.Length > 0)
                                    {
                                        var candidate = new NormalizeDecision
                                        {
                                            Merge = true,
                                            MergedText = merged
                                        };
                                        if (IsLikelyGrammarFracture(a, b) && !IsOverMerged(candidate.MergedText, a, b))
                                            return candidate;
                                        return new NormalizeDecision
                                        {
                                            Merge = false,
                                            MergedText = string.Empty
                                        };
                                    }
                                }
                            }
                        }
                    }
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
        var progress = new NormalizeProgressState
        {
            SourceBlocks = source.Count,
            MaxAttempts = maxAttempts
        };
        using var heartbeatCts = new CancellationTokenSource();
        var startedAt = Stopwatch.StartNew();
        var heartbeatTask = Task.Run(async () =>
        {
            while (!heartbeatCts.IsCancellationRequested)
            {
                Console.WriteLine(
                    $"[normalize-heartbeat] elapsed={startedAt.Elapsed:hh\\:mm\\:ss} idx={progress.CurrentIndex + 1}/{progress.SourceBlocks} out={progress.OutputBlocks} attempt={progress.CurrentAttempt}/{progress.MaxAttempts} stage={progress.Stage}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), heartbeatCts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, heartbeatCts.Token);
        using var logWriter = new StreamWriter(dirs.SubtitleNormalizeLogPath, false, Encoding.UTF8);
        logWriter.WriteLine($"[start] sourceBlocks={source.Count}, attempts={maxAttempts}, ts={DateTime.UtcNow:O}");

        try
        {
            var left = source[0];
            var nextIndex = 1;
            while (nextIndex < source.Count)
            {
                progress.CurrentIndex = nextIndex - 1;
                var right = source[nextIndex];
                var decision = DecideMerge(left, right, maxAttempts, progress);
                if (decision.Merge)
                {
                    var merged = new SubtitleBlock
                    {
                        Start = left.Start,
                        End = right.End,
                        Text = decision.MergedText
                    };
                    // 合并后继续与下一句做滚动校验：12,3。
                    left = merged;
                    progress.Stage = "merged";
                    logWriter.WriteLine(
                        $"[merge] left={nextIndex - 1} right={nextIndex}\n  left={source[nextIndex - 1].Text}\n  right={right.Text}\n  merged={decision.MergedText}");
                    nextIndex += 1;
                }
                else
                {
                    normalized.Add(left);
                    left = right;
                    progress.OutputBlocks = normalized.Count;
                    progress.Stage = "kept";
                    logWriter.WriteLine($"[keep] idx={nextIndex - 1}\n  text={normalized[^1].Text}");
                    nextIndex += 1;
                }
            }
            normalized.Add(left);
            progress.OutputBlocks = normalized.Count;
            progress.Stage = "flush-tail";
            logWriter.WriteLine($"[tail] text={left.Text}");
        }
        finally
        {
            heartbeatCts.Cancel();
            try
            {
                heartbeatTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // ignore
            }
        }

        logWriter.WriteLine($"[end] normalizedBlocks={normalized.Count}, ts={DateTime.UtcNow:O}");
        WriteNormalizedFiles(dirs, normalized);
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
        Normalize(dirs);
        return context;
    }
}
