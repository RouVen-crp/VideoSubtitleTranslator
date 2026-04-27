using System;
using System.Collections.Generic;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 安全过滤步骤（步骤层）。
/// </summary>
[PipelineStep("SecurityFilter")]
public abstract class SecurityFilterStepBase : IPipelineStep
{
    public string Step => "SecurityFilter";

    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

/// <summary>
/// 默认安全过滤实现：仅保留白名单中的键，构造一个“干净”的上下文字典。
/// </summary>
[PipelineStep("SecurityFilter", Implementation = "Default")]
public sealed class DefaultSecurityFilterStep : SecurityFilterStepBase
{
    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var cleaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void CopyIfExists(string key)
        {
            if (context.TryGetValue(key, out var value))
            {
                cleaned[key] = value;
            }
        }

        // 白名单：初始入参
        CopyIfExists("Url");
        CopyIfExists("WorkspaceRoot");
        CopyIfExists("ConfigPath");

        return cleaned;
    }
}

