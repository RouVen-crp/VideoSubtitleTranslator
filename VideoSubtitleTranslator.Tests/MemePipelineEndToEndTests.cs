using System.Collections.Concurrent;
using System.Text.Json;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

/// <summary>
/// #15 AC4：meme 模式端到端（从 NormalizeSubtitle 之后开始）。
/// 载入真实 <c>pipeline_meme.config.json</c> 的模型段，用本地 TCP 模型替身响应
/// Translate(Meme) 与 CulturalAnnotate 请求，证明：
/// 1. meme 配置下不会发起任何校对（Proofread）请求；
/// 2. 产物落在任务目录内；
/// 3. 全流程不依赖 shell 或 ffmpeg，可在任意平台运行。
/// </summary>
[Collection("Shared runtime configuration")]
public sealed class MemePipelineEndToEndTests
{
    private const int SentenceCount = 3;

    private static readonly IReadOnlyDictionary<string, string> KnownTranslations = new Dictionary<string, string>
    {
        ["Nobody messes with the DMV."] = "第1句译文",
        ["That's just how it is."] = "第2句译文",
        ["No cap, that's wild."] = "第3句译文"
    };

    [Fact]
    public void Meme_pipeline_after_normalize_completes_without_any_proofread_request()
    {
        using var temp = new TemporaryDirectory();
        var taskDirectory = Path.Combine(temp.Path, "meme-task");
        Directory.CreateDirectory(taskDirectory);
        var dirs = new WorkDirs(taskDirectory, ".mp4");
        WriteNormalizedSubtitle(dirs);

        var systemPrompts = new ConcurrentQueue<string>();
        using var server = new LoopbackHttpServer(requestBody =>
            RespondToModelRequest(requestBody, systemPrompts));

        var memePipeline = LoadMemePipelineSteps();
        var runtime = LoadMemeRuntime(server.BaseUrl);
        var previousConfig = GlobalRuntimeConfig.Current;
        try
        {
            new PipelineRunner(new PipelineRegistry()).Run(
                new PipelineConfig { Steps = memePipeline },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["WorkspacePath"] = taskDirectory,
                    ["VideoExt"] = ".mp4",
                    ["VideoTitle"] = "meme-lesson",
                    ["Mode"] = "meme"
                },
                runtime);
        }
        finally
        {
            GlobalRuntimeConfig.Current = previousConfig;
        }

        // 真实 meme 配置的模型段被使用：标题、Meme 模块翻译、文化注释各至少一次请求。
        var expectedModuleRequests =
            (int)Math.Ceiling(SentenceCount / (double)Math.Max(1, runtime.Translation.ModuleSentenceCount));
        var expectedAnnotationRequests = (int)Math.Ceiling(SentenceCount / 10.0);
        Assert.Equal(1 + expectedModuleRequests + expectedAnnotationRequests, server.RequestCount);
        Assert.Equal(server.RequestCount, systemPrompts.Count);
        AssertContainsPrompt(systemPrompts, "meme标题翻译助手");
        AssertContainsPrompt(systemPrompts, "短视频meme字幕翻译助手");
        AssertContainsPrompt(systemPrompts, "文化注释助手");
        // ProofreadEnabled=false：即使流水线里配置了 ProofreadSubtitle 步骤，也不能出现校对提示词请求。
        Assert.DoesNotContain(systemPrompts,
            prompt => prompt.Contains("字幕后处理校对助手", StringComparison.Ordinal));
        Assert.DoesNotContain(systemPrompts,
            prompt => prompt.Contains("你是视频字幕翻译助手", StringComparison.Ordinal));
        Assert.False(runtime.Translation.ProofreadEnabled);
        Assert.True(runtime.Translation.CulturalAnnotateEnabled);

        // 校对被配置关闭，因此不应产生校对阶段产物。
        Assert.False(File.Exists(dirs.ProofreadSubtitlePath));

        AssertArtifactInTaskDirectory(taskDirectory, dirs.TranslatedSubtitlePath);
        AssertArtifactInTaskDirectory(taskDirectory, dirs.CulturalAnnotationsPath);
        AssertArtifactInTaskDirectory(taskDirectory, dirs.DualSubtitlePath);
        AssertArtifactInTaskDirectory(taskDirectory, dirs.TranslatedTitlePath);
        Assert.Contains("街头生存指南", File.ReadAllText(dirs.TranslatedTitlePath));

        var translated = File.ReadAllText(dirs.TranslatedSubtitlePath);
        Assert.Contains("第1句译文", translated);
        Assert.Contains("第3句译文", translated);

        var annotations = File.ReadAllText(dirs.CulturalAnnotationsPath);
        using (var annotationsDocument = JsonDocument.Parse(annotations))
        {
            var annotationTexts = annotationsDocument.RootElement.EnumerateArray()
                .Select(item => item.TryGetProperty("annotation", out var text) ? text.GetString() : null)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
            Assert.Contains("DMV 指美国车辆管理局。", annotationTexts);
        }

        var ass = File.ReadAllText(dirs.DualSubtitlePath);
        Assert.Contains("[Script Info]", ass);
        Assert.Contains("Nobody messes with the DMV.", ass);
        Assert.Contains("第1句译文", ass);
        Assert.Contains("DMV 指美国车辆管理局。", ass);
    }

    private static List<PipelineStepConfig> LoadMemePipelineSteps()
    {
        var pipeline = PipelineRunner.LoadConfig(Path.Combine(AppContext.BaseDirectory, "pipeline_meme.config.json"));
        var normalizeIndex = pipeline.Steps.FindIndex(step => step.Step == "NormalizeSubtitle");
        Assert.True(normalizeIndex >= 0, "meme 流水线必须包含 NormalizeSubtitle 步骤。");

        // 只跑 NormalizeSubtitle 之后的模型段；BurnSubtitle 需要 ffmpeg，本测试不覆盖。
        var steps = pipeline.Steps
            .Skip(normalizeIndex + 1)
            .Where(step => !string.Equals(step.Step, "BurnSubtitle", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Contains(steps, step => step.Step == "Translate" && step.Implementation == "Meme");
        Assert.Contains(steps, step => step.Step == "CulturalAnnotate");
        Assert.Contains(steps, step => step.Step == "BuildDualSubtitle");
        return steps;
    }

    private static TranslatorRuntimeConfig LoadMemeRuntime(string baseUrl)
    {
        var runtime = TranslatorRuntimeConfig.Load(
            Path.Combine(AppContext.BaseDirectory, "Config", "translator_meme.config.json"));
        runtime.Mode = "meme";
        runtime.Llm.BaseUrl = baseUrl;
        runtime.Llm.RetryCount = 0;
        runtime.Llm.TimeoutSeconds = 10;
        runtime.Translation.RequestDelayMs = 0;
        return runtime;
    }

    private static void WriteNormalizedSubtitle(WorkDirs dirs)
    {
        File.WriteAllText(dirs.NormalizedSubtitlePath,
            "1\n00:00:00,000 --> 00:00:01,500\nNobody messes with the DMV.\n\n" +
            "2\n00:00:01,500 --> 00:00:03,000\nThat's just how it is.\n\n" +
            "3\n00:00:03,000 --> 00:00:04,500\nNo cap, that's wild.\n");
        // NormalizeSubtitle 会同时产出逐行原文文件，CulturalAnnotate 依赖它。
        File.WriteAllLines(dirs.NormalizedRawSubtitlePath,
        [
            "Nobody messes with the DMV.",
            "That's just how it is.",
            "No cap, that's wild."
        ]);
    }

    private static string RespondToModelRequest(string requestBody, ConcurrentQueue<string> systemPrompts)
    {
        using var document = JsonDocument.Parse(requestBody);
        var messages = document.RootElement.GetProperty("messages");
        var system = messages[0].GetProperty("content").GetString() ?? string.Empty;
        var user = messages[1].GetProperty("content").GetString() ?? string.Empty;
        systemPrompts.Enqueue(system);

        if (system.Contains("meme标题翻译助手", StringComparison.Ordinal))
        {
            return ChatCompletion(JsonSerializer.Serialize(new { title = "街头生存指南" }));
        }

        if (system.Contains("短视频meme字幕翻译助手", StringComparison.Ordinal))
        {
            var moduleLines = ParseCurrentModuleLines(user);
            return ChatCompletion(JsonSerializer.Serialize(new
            {
                translations = moduleLines.Select(line =>
                    KnownTranslations.TryGetValue(line, out var translation) ? translation : $"译：{line}").ToArray(),
                termEdits = Array.Empty<object>(),
                metaRuleEdits = Array.Empty<object>(),
                synopsisFullText = string.Empty,
                adSummaryUpdate = string.Empty
            }));
        }

        if (system.Contains("文化注释助手", StringComparison.Ordinal))
        {
            var count = CountOriginalSentences(user);
            var annotations = Enumerable.Range(1, count).Select(index => new
            {
                index,
                needsAnnotation = index == 1,
                annotation = index == 1 ? "DMV 指美国车辆管理局。" : string.Empty
            }).ToArray();
            return ChatCompletion(JsonSerializer.Serialize(new
            {
                annotations,
                termEdits = Array.Empty<object>(),
                contextUpdate = string.Empty
            }));
        }

        return ChatCompletion("{}");
    }

    private static string ChatCompletion(string content) =>
        JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } }
        });

    private static IReadOnlyList<string> ParseCurrentModuleLines(string prompt)
    {
        var lines = ExtractSection(prompt, "### 当前模块原文", "## 最终校验");
        return lines.Count > 0 ? lines : ["（空模块）"];
    }

    private static IReadOnlyList<string> ExtractSection(string prompt, string startMarker, string endMarker)
    {
        var start = prompt.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return Array.Empty<string>();
        start += startMarker.Length;
        var end = prompt.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0) end = prompt.Length;
        return prompt[start..end]
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static int CountOriginalSentences(string prompt)
    {
        const string startMarker = "### 原文字幕";
        const string endMarker = "### 中文字幕";
        var start = prompt.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return SentenceCount;
        start += startMarker.Length;
        var end = prompt.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0) end = prompt.Length;
        var section = prompt[start..end];
        return section.Split('\n')
            .Select(line => line.Trim())
            .Count(line => line.Length > 1 && char.IsDigit(line[0]) && line[1] == '.');
    }

    private static void AssertContainsPrompt(ConcurrentQueue<string> prompts, string marker) =>
        Assert.Contains(prompts, prompt => prompt.Contains(marker, StringComparison.Ordinal));

    private static void AssertArtifactInTaskDirectory(string taskDirectory, string artifactPath)
    {
        Assert.True(File.Exists(artifactPath), $"缺少产物：{artifactPath}");
        Assert.Equal(
            Path.GetFullPath(taskDirectory),
            Path.GetFullPath(Path.GetDirectoryName(artifactPath)!));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
