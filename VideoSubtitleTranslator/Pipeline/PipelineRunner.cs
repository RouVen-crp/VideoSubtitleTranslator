using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>根据 JSON 配置驱动整个业务流程执行的运行器。</summary>
public sealed class PipelineRunner
{
    private readonly PipelineRegistry _registry;

    public PipelineRunner(PipelineRegistry registry) => _registry = registry;

    public static PipelineConfig LoadConfig(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("未找到流程配置文件", path);

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PipelineConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new PipelineConfig();
    }

    public Dictionary<string, string> Run(PipelineConfig config, Dictionary<string, string>? initialContext = null,
        TranslatorRuntimeConfig? translatorConfig = null)
    {
        GlobalRuntimeConfig.Current = translatorConfig ?? new TranslatorRuntimeConfig();
        var context = initialContext is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(initialContext, StringComparer.OrdinalIgnoreCase);

        foreach (var stepConfig in config.Steps)
            context = RunStep(stepConfig, context);

        return context;
    }

    /// <summary>执行单个已配置步骤，供 Web Worker 在步骤边界提交持久化阶段。</summary>
    public Dictionary<string, string> RunStep(PipelineStepConfig stepConfig, Dictionary<string, string> context)
    {
        var requiredKeys = _registry.GetRequiredKeys(stepConfig.Step, stepConfig.Implementation);
        if (requiredKeys is { Length: > 0 })
        {
            foreach (var key in requiredKeys)
            {
                if (!context.ContainsKey(key))
                {
                    throw new InvalidOperationException(
                        $"执行步骤 \"{stepConfig.Step}\" 的实现 \"{stepConfig.Implementation}\" 之前，缺少必需的上下文字段：\"{key}\"。");
                }
            }
        }

        return _registry.Resolve(stepConfig.Step, stepConfig.Implementation).Execute(context);
    }
}
