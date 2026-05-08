using System.Text;
using System.Text.Json;

namespace VideoSubtitleTranslator.Pipeline;

[PipelineStep("CulturalAnnotate", RequiredKeys = new[] { "WorkspacePath", "VideoExt" })]
public abstract class CulturalAnnotateStepBase : IPipelineStep
{
    public string Step => "CulturalAnnotate";
    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

[PipelineStep("CulturalAnnotate", Implementation = "DeepSeek")]
public sealed class DeepSeekCulturalAnnotateStep : CulturalAnnotateStepBase
{
    private sealed class AnnotationItem
    {
        public required int Index { get; init; }
        public required bool NeedsAnnotation { get; init; }
        public string Annotation { get; set; } = string.Empty;
    }

    private sealed class AnnotationResponse
    {
        public List<AnnotationItem> Annotations { get; set; } = new();
        public List<EditOperation> TermEdits { get; set; } = new();
        public string ContextUpdate { get; set; } = string.Empty;
    }

    private sealed class EditOperation
    {
        public string Action { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private static List<SrtEntry> ParseSrt(string path)
    {
        var lines = File.ReadAllLines(path);
        var list = new List<SrtEntry>();
        for (var i = 0; i + 2 < lines.Length; i++)
        {
            if (!int.TryParse(lines[i].Trim(), out var idx)) continue;
            var time = lines[i + 1];
            var parts = time.Split("-->");
            if (parts.Length != 2) continue;
            var text = lines[i + 2].Trim();
            if (text.Length == 0) continue;
            list.Add(new SrtEntry { Index = idx, Start = parts[0].Trim(), End = parts[1].Trim(), Text = text });
            i += 3;
        }
        return list;
    }

    private sealed class SrtEntry
    {
        public int Index { get; init; }
        public required string Start { get; init; }
        public required string End { get; init; }
        public required string Text { get; init; }
    }

    private static Dictionary<string, string> LoadTable(string path)
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

    private static void SaveTable(string path, Dictionary<string, string> table)
    {
        File.WriteAllLines(path, table.Select(kv => $"{kv.Key}\t{kv.Value}"));
    }

    private static string BuildSystemPrompt()
    {
        return PromptProvider.Get("CulturalAnnotate/system_prompt.md").Trim();
    }

    private static string BuildUserPrompt(
        List<string> originals,
        List<string> translations,
        IReadOnlyDictionary<string, string> termTable,
        string contextText)
    {
        var originalsBlock = new StringBuilder();
        for (var i = 0; i < originals.Count; i++)
            originalsBlock.AppendLine($"{i + 1}. {originals[i]}");

        var translationsBlock = new StringBuilder();
        for (var i = 0; i < translations.Count; i++)
            translationsBlock.AppendLine($"{i + 1}. {translations[i]}");

        var termBlock = termTable.Count == 0
            ? "（尚无）"
            : string.Join('\n', termTable.Select(kv => $"{kv.Key} => {kv.Value}"));

        return PipelineTextUtils.ApplyTemplate(
            PromptProvider.Get("CulturalAnnotate/user_prompt_template.md"),
            new Dictionary<string, string>
            {
                ["ORIGINAL_SENTENCES"] = originalsBlock.ToString().TrimEnd(),
                ["TRANSLATED_SENTENCES"] = translationsBlock.ToString().TrimEnd(),
                ["CULTURAL_TERM_TABLE"] = termBlock,
                ["CULTURAL_CONTEXT"] = string.IsNullOrWhiteSpace(contextText) ? "（尚无）" : contextText.Trim()
            });
    }

    private static AnnotationResponse? TryParseResponse(string response)
    {
        var json = PipelineTextUtils.ExtractJsonObject(response);
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize<AnnotationResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyTermEdits(List<EditOperation> edits, Dictionary<string, string> table)
    {
        foreach (var edit in edits)
        {
            var action = edit.Action?.Trim().ToLowerInvariant() ?? string.Empty;
            if (action is not ("add" or "update" or "delete")) continue;
            var key = edit.Key?.Trim() ?? string.Empty;
            var value = edit.Value?.Trim() ?? string.Empty;
            if (key.Length == 0) continue;
            switch (action)
            {
                case "add":
                case "update":
                    if (value.Length > 0) table[key] = value;
                    break;
                case "delete":
                    table.Remove(key);
                    break;
            }
        }
    }

    private void RunAnnotation(WorkDirs dirs)
    {
        if (!GlobalRuntimeConfig.Current.Translation.CulturalAnnotateEnabled)
        {
            Console.WriteLine("CulturalAnnotate disabled, skip");
            return;
        }

        if (File.Exists(dirs.CulturalAnnotationsPath))
        {
            Console.WriteLine("Cultural annotations exist, skip");
            return;
        }

        var rawPath = File.Exists(dirs.NormalizedRawSubtitlePath) ? dirs.NormalizedRawSubtitlePath : File.Exists(dirs.RawSubtitlePath) ? dirs.RawSubtitlePath : throw new FileNotFoundException("Missing raw subtitle file");
        var translatedSrtPath = File.Exists(dirs.ProofreadSubtitlePath) ? dirs.ProofreadSubtitlePath : dirs.TranslatedSubtitlePath;
        if (!File.Exists(translatedSrtPath))
            throw new FileNotFoundException($"Missing translated subtitle: {translatedSrtPath}");

        var originalLines = File.ReadAllLines(rawPath).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var translatedEntries = ParseSrt(translatedSrtPath);
        var translatedLines = translatedEntries.Select(e => e.Text).ToList();

        if (originalLines.Count != translatedLines.Count)
        {
            Console.WriteLine($"Warning: original({originalLines.Count}) vs translated({translatedLines.Count}) sentence count mismatch, using min");
            var min = Math.Min(originalLines.Count, translatedLines.Count);
            originalLines = originalLines.Take(min).ToList();
            translatedLines = translatedLines.Take(min).ToList();
        }

        var termTable = LoadTable(dirs.CulturalTermTablePath);
        if (!File.Exists(dirs.CulturalTermTablePath))
            File.WriteAllText(dirs.CulturalTermTablePath, string.Empty, Encoding.UTF8);

        var contextText = File.Exists(dirs.CulturalContextPath) ? File.ReadAllText(dirs.CulturalContextPath).Trim() : string.Empty;
        if (!File.Exists(dirs.CulturalContextPath))
            File.WriteAllText(dirs.CulturalContextPath, string.Empty, Encoding.UTF8);

        var maxAttempts = Math.Max(1, GlobalRuntimeConfig.Current.Translation.ModuleJsonMaxAttempts);
        var annotations = new List<AnnotationItem>();
        var batchSize = 10;
        var config = GlobalRuntimeConfig.Current;

        for (var batchStart = 0; batchStart < originalLines.Count; batchStart += batchSize)
        {
            var batchOriginals = originalLines.Skip(batchStart).Take(batchSize).ToList();
            var batchTranslations = translatedLines.Skip(batchStart).Take(batchSize).ToList();
            var prompt = BuildUserPrompt(batchOriginals, batchTranslations, termTable, contextText);
            AnnotationResponse? parsed = null;
            var lastResponse = string.Empty;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                lastResponse = ApiCaller.CallApi(config.Llm.Model, BuildSystemPrompt(), prompt).Result;
                parsed = TryParseResponse(lastResponse);
                if (parsed is not null) break;
                Thread.Sleep(Math.Max(50, config.Translation.RequestDelayMs));
            }

            if (parsed is null)
                throw new InvalidOperationException(
                    $"CulturalAnnotate batch {batchStart / batchSize + 1} failed after {maxAttempts} attempts.");

            foreach (var item in parsed.Annotations)
            {
                annotations.Add(new AnnotationItem
                {
                    Index = item.Index > 0 ? item.Index : batchStart + annotations.Count % batchSize + 1,
                    NeedsAnnotation = item.NeedsAnnotation,
                    Annotation = item.Annotation?.Trim() ?? string.Empty
                });
            }

            ApplyTermEdits(parsed.TermEdits, termTable);
            if (!string.IsNullOrWhiteSpace(parsed.ContextUpdate))
                contextText = parsed.ContextUpdate.Trim();

            Thread.Sleep(Math.Max(0, config.Translation.RequestDelayMs));
        }

        SaveTable(dirs.CulturalTermTablePath, termTable);
        File.WriteAllText(dirs.CulturalContextPath, contextText, Encoding.UTF8);

        var json = JsonSerializer.Serialize(annotations,
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(dirs.CulturalAnnotationsPath, json, Encoding.UTF8);
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
        RunAnnotation(dirs);
        return context;
    }
}
