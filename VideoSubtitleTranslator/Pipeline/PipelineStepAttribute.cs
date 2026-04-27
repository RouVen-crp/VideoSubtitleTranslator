using System;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 标记流程“步骤”及其具体实现的统一注解。
/// <para>
/// 使用方式约定：
/// <list type="bullet">
/// <item>
/// <description>
/// 步骤层（抽象基类）：<c>[PipelineStep("Translate", RequiredKeys = new[] { "SubtitlePath" })]</c>，<c>Implementation</c> 为空或不填写；
/// </description>
/// </item>
/// <item>
/// <description>
/// 实现层（具体类）：<c>[PipelineStep("Translate", Implementation = "DeepSeek")]</c>，可选填 <c>RequiredKeys</c> 覆盖步骤层约束。
/// </description>
/// </item>
/// </list>
/// </para>
/// <para>
/// 入参与约束规则：
/// <list type="number">
/// <item>
/// <description>若实现层注解声明了 <see cref="RequiredKeys"/>，则仅按实现层约束校验；</description>
/// </item>
/// <item>
/// <description>否则若步骤层注解声明了 <see cref="RequiredKeys"/>，则按步骤层约束校验；</description>
/// </item>
/// <item>
/// <description>若两层都未声明，则不做 Key 校验。</description>
/// </item>
/// </list>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PipelineStepAttribute : Attribute
{
    /// <summary>
    /// 步骤名称（逻辑名），例如 "Download"、"ExtractSubtitle"、"Translate"。
    /// </summary>
    public string Step { get; }

    /// <summary>
    /// 实现名称（实现层专用），例如 "DeepSeek"、"PythonWhisper"、"ManualReview" 等。
    /// 对于步骤层（抽象基类），请保持为空（默认）。
    /// </summary>
    public string? Implementation { get; init; }

    /// <summary>
    /// 入参字典中<strong>必须包含</strong>的 Key 集合。
    /// <para>实现层未设置时，使用步骤层上的约束。</para>
    /// </summary>
    public string[]? RequiredKeys { get; init; }

    public PipelineStepAttribute(string step)
    {
        Step = step ?? throw new ArgumentNullException(nameof(step));
    }
}

