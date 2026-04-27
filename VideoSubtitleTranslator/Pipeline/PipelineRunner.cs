using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 根据 JSON 配置驱动整个业务流程执行的运行器。
/// </summary>
public sealed class PipelineRunner
{
    private readonly PipelineRegistry _registry;

    public PipelineRunner(PipelineRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// 从 JSON 文件加载流程配置。
    /// <para>配置文件示例见 <see cref="PipelineConfig"/> 的注释。</para>
    /// </summary>
    public static PipelineConfig LoadConfig(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("未找到流程配置文件", path);

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<PipelineConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return config ?? new PipelineConfig();
    }

    /// <summary>
    /// 按配置顺序执行所有步骤。
    /// <para>
    /// 约定：每一步的 <see cref="IPipelineStep.Execute"/> 只能通过传入的 <paramref name="initialContext"/> /
    /// 中间 <c>context</c> 读写数据，不得依赖实例内部状态。
    /// </para>
    /// </summary>
    /// <param name="config">流程配置。</param>
    /// <param name="initialContext">初始上下文字典，可为 <c>null</c>。</param>
    /// <returns>最终的上下文字典，包含所有步骤写入的键值。</returns>
    public Dictionary<string, string> Run(PipelineConfig config, Dictionary<string, string>? initialContext = null,
        TranslatorRuntimeConfig? translatorConfig = null)
    {
        GlobalRuntimeConfig.Current = translatorConfig ?? new TranslatorRuntimeConfig();
        var context = initialContext is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(initialContext, StringComparer.OrdinalIgnoreCase);

        foreach (var stepConfig in config.Steps)
        {
            var step = stepConfig.Step;
            var impl = stepConfig.Implementation;

            // 入参与约束校验：实现层 > 步骤层 > 无校验
            var requiredKeys = _registry.GetRequiredKeys(step, impl);
            if (requiredKeys is { Length: > 0 })
            {
                foreach (var key in requiredKeys)
                {
                    if (!context.ContainsKey(key))
                    {
                        throw new InvalidOperationException(
                            $"执行步骤 \"{step}\" 的实现 \"{impl}\" 之前，缺少必需的上下文字段：\"{key}\"。");
                    }
                }
            }

            var instance = _registry.Resolve(step, impl);

            // 强约束：实现类必须无状态，只通过 context 读写
            context = instance.Execute(context);
        }

        return context;
    }
}

