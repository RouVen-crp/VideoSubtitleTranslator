using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 翻译步骤（步骤层）。
/// </summary>
[PipelineStep("Translate", RequiredKeys = new[] { "WorkspacePath", "VideoExt", "VideoTitle" })]
public abstract class TranslateStepBase : IPipelineStep
{
    public string Step => "Translate";

    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

/// <summary>
/// 模块化翻译：整段前情提要重写、术语增量、独立 ad_memory 广告概括文件；解析失败抛异常中止。
/// </summary>
[PipelineStep("Translate", Implementation = "DeepSeek")]
public class DeepSeekTranslateStep : TranslateStepBase
{
    private sealed class ProgressState
    {
        public int TotalModules;
        public int CurrentModule;
        public int CurrentAttempt;
        public int MaxAttempts;
        public int CompletedModules;
        public string Stage = "init";
    }

    private string ModuleSysPrompt => PromptProvider.Get($"{PromptFolder}/module_system_prompt.md").Trim();
    private string InternalAdPolicy => PromptProvider.Get($"{PromptFolder}/internal_ad_policy_prompt.md").Trim();

    protected virtual string PromptFolder => "Translate";

    public sealed class SentenceItem
    {
        public required string TimeLine { get; init; }
        public required string OriginalText { get; init; }
    }

    public sealed class TranslationModule
    {
        public required int ModuleIndex { get; init; }
        public required List<SentenceItem> Sentences { get; init; }
        public List<string> TranslatedSentences { get; set; } = new();
    }

    private sealed class ModuleTranslationResponse
    {
        public List<string> Translations { get; set; } = new();
        public List<EditOperation> TermEdits { get; set; } = new();
        public List<EditOperation> MetaRuleEdits { get; set; } = new();

        /// <summary>本轮之后的完整前情提要（单段中文）。空或省略表示沿用上一轮。</summary>
        [JsonPropertyName("synopsisFullText")]
        public string? SynopsisFullText { get; set; }

        /// <summary>可选：本模块广告内容概括（写入 ad_memory.txt）；空或省略表示无广告。</summary>
        [JsonPropertyName("adSummaryUpdate")]
        public string? AdSummaryUpdate { get; set; }

        /// <summary>兼容旧字段名 adMemoryUpdate。</summary>
        [JsonPropertyName("adMemoryUpdate")]
        public string? LegacyAdMemoryUpdate { get; set; }
    }

    private sealed class EditOperation
    {
        public string Action { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static List<EditOperation> NormalizeEditOperations(IEnumerable<EditOperation>? edits, bool filterPromotional)
    {
        var list = new List<EditOperation>();
        if (edits is null) return list;
        foreach (var edit in edits)
        {
            var action = edit.Action?.Trim().ToLowerInvariant() ?? string.Empty;
            if (action is not ("add" or "update" or "delete")) continue;
            var key = edit.Key?.Trim() ?? string.Empty;
            var value = edit.Value?.Trim() ?? string.Empty;
            if (key.Length == 0) continue;
            if (action != "delete" && value.Length == 0) continue;
            if (filterPromotional && (LooksLikePromotionalMemoryText(key) || LooksLikePromotionalMemoryText(value)))
                continue;
            list.Add(new EditOperation
            {
                Action = action,
                Key = key,
                Value = value
            });
        }

        return list;
    }

    private static string EnsureAdMemoryFile(WorkDirs dirs)
    {
        if (!File.Exists(dirs.AdMemoryPath))
            File.WriteAllText(dirs.AdMemoryPath, string.Empty, Encoding.UTF8);
        return File.ReadAllText(dirs.AdMemoryPath).Trim();
    }

    private static string? NormalizeAdSummaryUpdate(ModuleTranslationResponse response)
    {
        var raw = !string.IsNullOrWhiteSpace(response.AdSummaryUpdate)
            ? response.AdSummaryUpdate
            : response.LegacyAdMemoryUpdate;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
            t = t.Replace("```", "", StringComparison.Ordinal).Trim();
        if (t.Length > 500) t = t[..500].Trim();
        if (t.Length == 0) return null;
        if (t.Contains('\n')) t = t.Replace('\n', ' ').Trim();
        return t;
    }

    private static bool TryAppendAdSummary(WorkDirs dirs, string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return false;
        var candidate = summary.Trim();
        if (candidate.Length < 6) return false;
        var existing = File.Exists(dirs.AdMemoryPath)
            ? File.ReadAllLines(dirs.AdMemoryPath)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList()
            : new List<string>();
        if (existing.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            return false;
        existing.Add(candidate);
        File.WriteAllLines(dirs.AdMemoryPath, existing, Encoding.UTF8);
        return true;
    }

    private string TranslateTitle(string title, string domainHintPrompt)
    {
        var domainHintSection = string.IsNullOrWhiteSpace(domainHintPrompt)
            ? string.Empty
            : $"【视频主题提示】\n{domainHintPrompt.Trim()}";
        var prompt = PipelineTextUtils.ApplyTemplate(
            PromptProvider.Get($"{PromptFolder}/title_user_prompt_template.md"),
            new Dictionary<string, string>
            {
                ["DOMAIN_HINT_SECTION"] = domainHintSection,
                ["TITLE"] = title
            });
        var response = ApiCaller.CallApi(
            GlobalRuntimeConfig.Current.Llm.Model,
            PromptProvider.Get($"{PromptFolder}/title_system_prompt.md").Trim(),
            prompt).Result;
        try
        {
            var json = PipelineTextUtils.ExtractJsonObject(response) ?? response.Trim();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? title : title;
        }
        catch
        {
            return title;
        }
    }

    private static List<SentenceItem> ParseSubtitle(string subtitlePath)
    {
        var lines = File.ReadAllLines(subtitlePath);
        var sentences = new List<SentenceItem>();
        for (var i = 0; i + 2 < lines.Length; i++)
        {
            if (!int.TryParse(lines[i].Trim(), out _)) continue;
            var timeLine = lines[i + 1].Trim();
            var text = lines[i + 2].Trim();
            if (text.Length == 0) continue;
            sentences.Add(new SentenceItem { TimeLine = timeLine, OriginalText = text });
            i += 3;
        }
        return sentences;
    }

    private static List<TranslationModule> BuildModules(List<SentenceItem> sentences, int moduleSize)
    {
        var modules = new List<TranslationModule>();
        for (var i = 0; i < sentences.Count; i += moduleSize)
        {
            modules.Add(new TranslationModule
            {
                ModuleIndex = modules.Count,
                Sentences = sentences.Skip(i).Take(moduleSize).ToList()
            });
        }
        return modules;
    }

    private string BuildModulePrompt(
        List<TranslationModule> modules,
        int moduleIndex,
        int contextWindow,
        IReadOnlyDictionary<string, string> termTable,
        IReadOnlyDictionary<string, string> metaRules,
        string synopsisParagraph,
        string adSummaries,
        string domainHintPrompt,
        string translatedTitle,
        bool isRepairAttempt)
    {
        var previousModules = new StringBuilder();
        for (var i = Math.Max(0, moduleIndex - contextWindow); i < moduleIndex; i++)
        {
            previousModules.AppendLine($"module#{i}");
            foreach (var pair in modules[i].Sentences.Select((s, idx) => new { s, idx }))
            {
                var translated = modules[i].TranslatedSentences.Count > pair.idx ? modules[i].TranslatedSentences[pair.idx] : string.Empty;
                previousModules.AppendLine($"- en: {pair.s.OriginalText}");
                previousModules.AppendLine($"- zh: {translated}");
            }
        }

        var nextModules = new StringBuilder();
        for (var i = moduleIndex + 1; i <= Math.Min(modules.Count - 1, moduleIndex + contextWindow); i++)
        {
            nextModules.AppendLine($"module#{i}");
            foreach (var sentence in modules[i].Sentences)
            {
                nextModules.AppendLine($"- en: {sentence.OriginalText}");
            }
        }

        var currentModule = new StringBuilder();
        foreach (var sentence in modules[moduleIndex].Sentences)
        {
            currentModule.AppendLine(sentence.OriginalText);
        }

        var termTableText = termTable.Count == 0
            ? "（尚无）"
            : string.Join('\n', termTable.Select(kv => $"{kv.Key} => {kv.Value}"));
        var metaRulesText = metaRules.Count == 0
            ? "（尚无）"
            : string.Join('\n', metaRules.Select(kv => $"{kv.Key} => {kv.Value}"));
        var domainHintSection = string.IsNullOrWhiteSpace(domainHintPrompt)
            ? string.Empty
            : $"【视频主题提示（全流程注入）】\n{domainHintPrompt.Trim()}\n";
        var repairSection = isRepairAttempt
            ? "【重要】你上一次输出不是合法 JSON，或 translations 条数与当前模块句数不一致。请仅输出 JSON 并修正条数。\n【重要】本次先确保 translations 条数正确，再填写其余字段。\n\n"
            : string.Empty;

        return PipelineTextUtils.ApplyTemplate(
            PromptProvider.Get($"{PromptFolder}/module_user_prompt_template.md"),
            new Dictionary<string, string>
            {
                ["DOMAIN_HINT_SECTION"] = domainHintSection,
                ["INTERNAL_AD_POLICY_PROMPT"] = InternalAdPolicy,
                ["AD_SUMMARIES"] = string.IsNullOrWhiteSpace(adSummaries) ? "（尚无）" : adSummaries,
                ["REPAIR_SECTION"] = repairSection,
                ["TRANSLATED_TITLE"] = translatedTitle,
                ["TERM_TABLE"] = termTableText,
                ["META_RULES"] = metaRulesText,
                ["SYNOPSIS_PARAGRAPH"] = string.IsNullOrWhiteSpace(synopsisParagraph) ? "（尚无）" : synopsisParagraph,
                ["PREVIOUS_MODULES"] = previousModules.ToString().TrimEnd(),
                ["NEXT_MODULES"] = nextModules.ToString().TrimEnd(),
                ["CURRENT_MODULE"] = currentModule.ToString().TrimEnd(),
                ["EXPECTED_COUNT"] = modules[moduleIndex].Sentences.Count.ToString()
            });
    }

    /// <summary>泛化推广文本检测（不绑定具体品牌）。用于过滤 termEdits 与 adMemoryUpdate。</summary>
    private static bool LooksLikePromotionalMemoryText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();

        if (lower.Contains("sponsored by") || lower.Contains("today's sponsor") || lower.Contains("todays sponsor") ||
            lower.Contains("our sponsor")) return true;
        if (lower.Contains("paid promotion") || lower.Contains("paid partnership")) return true;
        if (lower.Contains("invite code") || lower.Contains("invitation code") || lower.Contains("promo code") ||
            lower.Contains("discount code")) return true;
        if (lower.Contains("link in the description") || lower.Contains("link below") || lower.Contains("pin comment"))
            return true;
        if (lower.Contains("use my link") || (lower.Contains("sign up") && lower.Contains("reward"))) return true;
        if (lower.Contains("subscribe") && (lower.Contains("bell") || lower.Contains("notification"))) return true;
        if (lower.Contains("special offer") || lower.Contains("limited time")) return true;

        if (text.Contains("赞助") && (text.Contains("推广") || text.Contains("广告") || text.Contains("口播"))) return true;
        if (text.Contains("邀请码") || text.Contains("优惠码")) return true;
        if (text.Contains("置顶") && text.Contains("链接")) return true;

        return false;
    }

    private static void ApplyEdits(List<EditOperation> edits, Dictionary<string, string> table)
    {
        foreach (var edit in edits)
        {
            switch (edit.Action.Trim().ToLowerInvariant())
            {
                case "add":
                case "update":
                    table[edit.Key] = edit.Value;
                    break;
                case "delete":
                    table.Remove(edit.Key);
                    break;
            }
        }
    }

    private static int SynopsisMaxChars =>
        Math.Clamp(GlobalRuntimeConfig.Current.Translation.SynopsisMaxChars, 500, 50_000);

    private static int MetaRulesMaxCount =>
        Math.Clamp(GlobalRuntimeConfig.Current.Translation.MetaRulesMaxCount, 20, 2000);

    private static int MetaRuleMaxChars =>
        Math.Clamp(GlobalRuntimeConfig.Current.Translation.MetaRuleMaxChars, 20, 2000);

    private static void TrimMetaRules(Dictionary<string, string> metaRules)
    {
        if (metaRules.Count <= MetaRulesMaxCount) return;
        var toRemove = metaRules.Keys.Take(metaRules.Count - MetaRulesMaxCount).ToList();
        foreach (var k in toRemove) metaRules.Remove(k);
    }

    private static IEnumerable<string> SplitSynopsisSentences(string synopsis)
    {
        var sb = new StringBuilder();
        foreach (var ch in synopsis)
        {
            sb.Append(ch);
            if (ch is '。' or '！' or '？' or '!' or '?' or '\n')
            {
                var s = sb.ToString().Trim();
                if (s.Length > 0) yield return s;
                sb.Clear();
            }
        }

        var tail = sb.ToString().Trim();
        if (tail.Length > 0) yield return tail;
    }

    private static string CompressSynopsis(string synopsis)
    {
        if (string.IsNullOrWhiteSpace(synopsis)) return synopsis;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();
        foreach (var sentence in SplitSynopsisSentences(synopsis))
        {
            var key = sentence.Replace(" ", "", StringComparison.Ordinal)
                .Replace("　", "", StringComparison.Ordinal)
                .Trim();
            if (key.Length == 0) continue;
            if (seen.Add(key)) kept.Add(sentence);
        }

        return string.Join("", kept).Trim();
    }

    /// <summary>解析 JSON 但不校验 translations 条数（用于缺句补译）。</summary>
    private static bool TryParseModuleJsonLoose(string response, out ModuleTranslationResponse parsed)
    {
        parsed = new ModuleTranslationResponse();
        var json = PipelineTextUtils.ExtractJsonObject(response);
        if (json is null) return false;
        try
        {
            var model = JsonSerializer.Deserialize<ModuleTranslationResponse>(json, JsonOptions) ?? new ModuleTranslationResponse();
            if (model.Translations is null || model.Translations.Count == 0) return false;
            model.TermEdits = NormalizeEditOperations(model.TermEdits, filterPromotional: true);
            model.MetaRuleEdits = NormalizeEditOperations(model.MetaRuleEdits, filterPromotional: false);
            parsed = model;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private List<string> RepairMissingTranslationsLines(IReadOnlyList<string> missingEnglishLines)
    {
        if (missingEnglishLines.Count == 0) return new List<string>();
        var indexedLines = new StringBuilder();
        for (var i = 0; i < missingEnglishLines.Count; i++)
            indexedLines.AppendLine($"{i + 1}. {missingEnglishLines[i]}");
        var prompt = PipelineTextUtils.ApplyTemplate(
            PromptProvider.Get($"{PromptFolder}/repair_user_prompt_template.md"),
            new Dictionary<string, string>
            {
                ["COUNT"] = missingEnglishLines.Count.ToString(),
                ["LINES"] = indexedLines.ToString().TrimEnd()
            });

        const int repairAttempts = 3;
        for (var attempt = 0; attempt < repairAttempts; attempt++)
        {
            var resp = ApiCaller.CallApi(
                GlobalRuntimeConfig.Current.Llm.Model,
                PromptProvider.Get($"{PromptFolder}/repair_system_prompt.md").Trim(),
                prompt).Result;
            var json = PipelineTextUtils.ExtractJsonObject(resp);
            if (json is null) continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("translations", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    continue;
                var list = new List<string>();
                foreach (var el in arr.EnumerateArray())
                {
                    var s = el.GetString();
                    if (s is not null) list.Add(s.Trim());
                }

                if (list.Count == missingEnglishLines.Count) return list;
            }
            catch
            {
                // continue
            }

            Thread.Sleep(150);
        }

        throw new InvalidOperationException(
            $"缺句补译失败：期望 {missingEnglishLines.Count} 条译文，英文行：\n{string.Join("\n", missingEnglishLines)}");
    }

    private static bool TryParseTranslationResponse(string response, int expectedCount, out ModuleTranslationResponse parsed)
    {
        parsed = new ModuleTranslationResponse();
        var json = PipelineTextUtils.ExtractJsonObject(response);
        if (json is null) return false;

        try
        {
            var model = JsonSerializer.Deserialize<ModuleTranslationResponse>(json, JsonOptions) ?? new ModuleTranslationResponse();
            if (model.Translations is null || model.Translations.Count != expectedCount) return false;

            model.TermEdits = NormalizeEditOperations(model.TermEdits, filterPromotional: true);
            model.MetaRuleEdits = NormalizeEditOperations(model.MetaRuleEdits, filterPromotional: false);
            parsed = model;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendHistory(string path, string content)
    {
        File.AppendAllText(path, $"[{DateTime.UtcNow:O}]\n{content}\n\n");
    }

    private static void ApplySynopsisUpdate(ref string synopsisParagraph, string? synopsisFullText)
    {
        if (synopsisFullText is null) return;
        var t = synopsisFullText.Trim();
        if (t.Length == 0) return;
        if (LooksLikePromotionalMemoryText(t)) return;
        if (t.Length > SynopsisMaxChars)
            t = t[..SynopsisMaxChars];
        synopsisParagraph = t;
    }

    private static Dictionary<string, string> LoadTermTableFromFile(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var idx = line.IndexOf('\t');
            if (idx <= 0 || idx >= line.Length - 1) continue;
            var k = line[..idx].Trim();
            var v = line[(idx + 1)..].Trim();
            if (k.Length == 0 || v.Length == 0) continue;
            result[k] = v;
        }

        return result;
    }

    private void WriteFinalPromptSnapshot(
        WorkDirs dirs,
        IReadOnlyDictionary<string, string> termTable,
        IReadOnlyDictionary<string, string> metaRules,
        string synopsisParagraph,
        string domainHintPrompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# VideoSubtitleTranslator — 人工审核快照");
        sb.AppendLine();
        sb.AppendLine("生成时间（UTC）：" + DateTime.UtcNow.ToString("O"));
        sb.AppendLine();
        sb.AppendLine("## 用途");
        sb.AppendLine("用于人工审核：当前视频的术语一致性与前情提要是否存在根本性偏差。");
        sb.AppendLine();
        sb.AppendLine("## 最终术语翻译表（term_consistency_table.txt）");
        if (termTable.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("（空）");
        }
        else
        {
            foreach (var kv in termTable.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine($"- {kv.Key} => {kv.Value}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## 最终元翻译指令（meta_translation_rules.txt）");
        if (metaRules.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("（空）");
        }
        else
        {
            foreach (var kv in metaRules.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine($"- {kv.Key} => {kv.Value}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## 最终前情提要（synopsis_memory.txt）");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(synopsisParagraph) ? "（空）" : synopsisParagraph);
        sb.AppendLine();
        sb.AppendLine("## 当前广告识别记忆（ad_memory.txt）");
        sb.AppendLine();
        sb.AppendLine(File.Exists(dirs.AdMemoryPath) ? File.ReadAllText(dirs.AdMemoryPath).Trim() : "（空）");
        sb.AppendLine();
        sb.AppendLine("## 程序内置广告策略");
        sb.AppendLine();
        sb.AppendLine(InternalAdPolicy);
        sb.AppendLine();
        sb.AppendLine("## 最终提示词（用于追溯策略，不用于审核正文）");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(ModuleSysPrompt);
        sb.AppendLine("```");

        var workspaceSnap = dirs.CustomPath("translator_prompt_snapshot.md");
        File.WriteAllText(workspaceSnap, sb.ToString(), Encoding.UTF8);
    }

    private void RunTranslation(string rawTitle, WorkDirs dirs)
    {
        var domainHintPrompt = GlobalRuntimeConfig.Current.Translation.DomainHintPrompt;
        if (File.Exists(dirs.RawTranslatedSubtitlePath) && File.Exists(dirs.TranslatedSubtitlePath))
        {
            Console.WriteLine("Translated subtitle exists, skip translate");
            var synopsis = File.Exists(dirs.SynopsisPath) ? File.ReadAllText(dirs.SynopsisPath).Trim() : string.Empty;
            var term = LoadTermTableFromFile(dirs.TermTablePath);
            var metaRulesSnapshot = LoadTermTableFromFile(dirs.MetaRulesPath);
            WriteFinalPromptSnapshot(dirs, term, metaRulesSnapshot, synopsis, domainHintPrompt);
            return;
        }

        var translationConfig = GlobalRuntimeConfig.Current.Translation;
        var moduleSize = Math.Max(1, translationConfig.ModuleSentenceCount);
        var contextWindow = Math.Max(0, translationConfig.ContextModuleWindow);
        var maxJsonAttempts = Math.Max(1, translationConfig.ModuleJsonMaxAttempts);

        var translatedTitle = File.Exists(dirs.TranslatedTitlePath)
            ? File.ReadAllText(dirs.TranslatedTitlePath)
            : TranslateTitle(rawTitle, domainHintPrompt);
        File.WriteAllText(dirs.TranslatedTitlePath, translatedTitle);

        var adSummaries = EnsureAdMemoryFile(dirs);
        var synopsisParagraph = File.Exists(dirs.SynopsisPath) ? File.ReadAllText(dirs.SynopsisPath).Trim() : "";
        var metaRules = LoadTermTableFromFile(dirs.MetaRulesPath);

        var sourceSubtitlePath = File.Exists(dirs.NormalizedSubtitlePath) ? dirs.NormalizedSubtitlePath : dirs.SubtitlePath;
        Console.WriteLine($"[translate] subtitle-source={sourceSubtitlePath}");
        var sentences = ParseSubtitle(sourceSubtitlePath);
        var modules = BuildModules(sentences, moduleSize);
        var termTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var progress = new ProgressState
        {
            TotalModules = modules.Count,
            MaxAttempts = maxJsonAttempts
        };
        using var heartbeatCts = new CancellationTokenSource();
        var startedAt = Stopwatch.StartNew();
        var heartbeatTask = Task.Run(async () =>
        {
            while (!heartbeatCts.IsCancellationRequested)
            {
                Console.WriteLine(
                    $"[heartbeat] elapsed={startedAt.Elapsed:hh\\:mm\\:ss} module={progress.CurrentModule + 1}/{progress.TotalModules} completed={progress.CompletedModules} attempt={progress.CurrentAttempt}/{progress.MaxAttempts} stage={progress.Stage}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), heartbeatCts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, heartbeatCts.Token);

        try
        {
            for (var moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
            {
                progress.CurrentModule = moduleIndex;
                progress.CurrentAttempt = 0;
                progress.Stage = "module-start";
                Console.WriteLine($"[progress] start module={moduleIndex + 1}/{modules.Count}");
                adSummaries = File.Exists(dirs.AdMemoryPath) ? File.ReadAllText(dirs.AdMemoryPath).Trim() : adSummaries;
                var expected = modules[moduleIndex].Sentences.Count;
                ModuleTranslationResponse? parsed = null;
                string lastResponse = string.Empty;

                for (var attempt = 0; attempt < maxJsonAttempts; attempt++)
                {
                    progress.CurrentAttempt = attempt + 1;
                    progress.Stage = "requesting-model";
                    var prompt = BuildModulePrompt(modules, moduleIndex, contextWindow, termTable, metaRules, synopsisParagraph,
                        adSummaries, domainHintPrompt, translatedTitle, attempt > 0);
                    AppendHistory(dirs.PromptHistoryPath, $"--- attempt {attempt + 1} ---\n{prompt}");
                    lastResponse = ApiCaller.CallApi(GlobalRuntimeConfig.Current.Llm.Model, ModuleSysPrompt, prompt).Result;
                    progress.Stage = "parsing-response";
                    AppendHistory(dirs.PromptHistoryPath, $"--- raw response attempt {attempt + 1} ---\n{lastResponse}");

                    if (TryParseTranslationResponse(lastResponse, expected, out var candidate))
                    {
                        parsed = candidate;
                        break;
                    }

                    Thread.Sleep(Math.Max(50, translationConfig.RequestDelayMs));
                }

                if (parsed is null && TryParseModuleJsonLoose(lastResponse, out var loose) && loose is not null)
                {
                    progress.Stage = "repairing-missing-lines";
                    if (loose.Translations.Count > expected)
                        loose.Translations = loose.Translations.Take(expected).ToList();
                    var gap = expected - loose.Translations.Count;
                    if (gap is > 0 and <= 2)
                    {
                        var tail = modules[moduleIndex].Sentences.TakeLast(gap).Select(s => s.OriginalText).ToList();
                        var repaired = RepairMissingTranslationsLines(tail);
                        loose.Translations.AddRange(repaired);
                    }

                    if (loose.Translations.Count == expected)
                        parsed = loose;
                }

                if (parsed is null)
                    throw new InvalidOperationException(
                        $"模块 {moduleIndex} 翻译失败：在 {maxJsonAttempts} 次尝试后仍无法解析合法 JSON 或 translations 条数不等于 {expected}。最后一次模型输出片段：\n" +
                        lastResponse[..Math.Min(2000, lastResponse.Length)]);

                progress.Stage = "updating-memory";
                modules[moduleIndex].TranslatedSentences = parsed.Translations;
                var adSummaryUpdate = NormalizeAdSummaryUpdate(parsed);
                var hasAdSummaryOutput = !string.IsNullOrWhiteSpace(adSummaryUpdate);
                var appendedAdSummary = TryAppendAdSummary(dirs, adSummaryUpdate);
                var sourceJoined = string.Join(" ", modules[moduleIndex].Sentences.Select(s => s.OriginalText));
                var isLikelyAdModule = hasAdSummaryOutput || appendedAdSummary || LooksLikePromotionalMemoryText(sourceJoined);
                if (!isLikelyAdModule)
                {
                    ApplyEdits(parsed.TermEdits, termTable);
                    ApplyEdits(parsed.MetaRuleEdits, metaRules);
                    foreach (var key in metaRules.Keys.ToList())
                    {
                        var v = metaRules[key].Trim();
                        if (v.Length > MetaRuleMaxChars)
                            metaRules[key] = v[..MetaRuleMaxChars].Trim();
                    }
                    TrimMetaRules(metaRules);
                    ApplySynopsisUpdate(ref synopsisParagraph, parsed.SynopsisFullText);
                }
                var compressEvery = Math.Max(1, GlobalRuntimeConfig.Current.Translation.SynopsisCompressEveryModules);
                if ((moduleIndex + 1) % compressEvery == 0)
                    synopsisParagraph = CompressSynopsis(synopsisParagraph);
                adSummaries = File.ReadAllText(dirs.AdMemoryPath).Trim();

                File.WriteAllText(dirs.SynopsisPath, synopsisParagraph, Encoding.UTF8);

                AppendHistory(dirs.MemoryHistoryPath, JsonSerializer.Serialize(new
                {
                    ModuleIndex = moduleIndex,
                    parsed.Translations,
                    parsed.TermEdits,
                    parsed.MetaRuleEdits,
                    parsed.SynopsisFullText,
                    adSummaryUpdate,
                    isLikelyAdModule,
                    synopsisParagraph,
                    termTable,
                    metaRules,
                    adSummaries
                }));
                progress.CompletedModules++;
                progress.Stage = "module-complete";
                Console.WriteLine(
                    $"[progress] done module={moduleIndex + 1}/{modules.Count} completed={progress.CompletedModules} elapsed={startedAt.Elapsed:hh\\:mm\\:ss}");
                Thread.Sleep(Math.Max(0, translationConfig.RequestDelayMs));
            }
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

        var translatedLines = new List<string>();
        var rawTranslatedLines = new List<string>();
        var subtitleIndex = 1;
        foreach (var module in modules)
        {
            for (var i = 0; i < module.Sentences.Count; i++)
            {
                var translated = i < module.TranslatedSentences.Count ? module.TranslatedSentences[i] : throw new InvalidOperationException("内部错误：译文条数与模块句数不一致。");
                translatedLines.Add(subtitleIndex.ToString());
                translatedLines.Add(module.Sentences[i].TimeLine);
                translatedLines.Add(translated);
                translatedLines.Add(string.Empty);
                rawTranslatedLines.Add(translated);
                subtitleIndex++;
            }
        }

        File.WriteAllLines(dirs.RawTranslatedSubtitlePath, rawTranslatedLines);
        File.WriteAllLines(dirs.TranslatedSubtitlePath, translatedLines);
        File.WriteAllLines(dirs.TermTablePath, termTable.Select(kv => $"{kv.Key}\t{kv.Value}"));
        File.WriteAllLines(dirs.MetaRulesPath, metaRules.Select(kv => $"{kv.Key}\t{kv.Value}"));
        synopsisParagraph = CompressSynopsis(synopsisParagraph);
        File.WriteAllText(dirs.SynopsisPath, synopsisParagraph, Encoding.UTF8);

        WriteFinalPromptSnapshot(dirs, termTable, metaRules, synopsisParagraph, domainHintPrompt);
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var workspacePath = context["WorkspacePath"];
        var ext = context["VideoExt"];
        var title = context["VideoTitle"];
        var dirs = new WorkDirs(workspacePath, ext);

        RunTranslation(title, dirs);
        return context;
    }
}

[PipelineStep("Translate", Implementation = "Meme")]
public sealed class MemeTranslateStep : DeepSeekTranslateStep
{
    protected override string PromptFolder => "Translate/Meme";
}
