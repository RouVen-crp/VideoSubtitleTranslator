using System.Collections.Generic;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 单个步骤在配置文件中的描述：由“步骤名 + 实现名”唯一确定一个具体实现类。
/// </summary>
public sealed class PipelineStepConfig
{
    /// <summary>
    /// 步骤名称，对应 <see cref="PipelineStepAttribute.Step"/>。
    /// </summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>
    /// 实现名称，对应 <see cref="PipelineStepAttribute.Implementation"/>。
    /// </summary>
    public string Implementation { get; set; } = string.Empty;
}

/// <summary>
/// 整个流程配置：按顺序列出要执行的步骤。
/// JSON 形态示例：
/// <code>
/// {
///   "steps": [
///     { "step": "Download", "implementation": "Default" },
///     { "step": "Transcribe", "implementation": "PythonWhisper" },
///     { "step": "Translate", "implementation": "DeepSeek" },
///     { "step": "BurnSubtitle", "implementation": "Default" }
///   ]
/// }
/// </code>
/// </summary>
public sealed class PipelineConfig
{
    public List<PipelineStepConfig> Steps { get; set; } = new();
}

