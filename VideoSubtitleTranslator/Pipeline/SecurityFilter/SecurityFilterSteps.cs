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
/// 白名单只覆盖本应用在进入流水线之前已经确定或校验过的字段；
/// 本地视频输入（无 URL 可清洗）同样依赖这些字段通过后续步骤的必填校验。
/// </summary>
[PipelineStep("SecurityFilter", Implementation = "Default")]
public sealed class DefaultSecurityFilterStep : SecurityFilterStepBase
{
    /// <summary>
    /// 初始入参：流水线开始前由调用方确定，后续步骤直接依赖。
    /// </summary>
    private static readonly string[] PreservedKeys =
    [
        "Url",
        "WorkspaceRoot",
        "ConfigPath",
        // 本地视频输入：由 CLI 校验路径与媒体后预置，InitWorkspace/SplitAudio 依赖。
        "VideoTitle",
        "WorkspacePath",
        "VideoExt",
        "VideoAuthor",
        "VideoUploadDate",
        // 重复执行时避免覆盖既有工作目录。
        "AvoidWorkspaceOverwrite"
    ];

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var cleaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in PreservedKeys)
        {
            if (context.TryGetValue(key, out var value))
            {
                cleaned[key] = value;
            }
        }

        return cleaned;
    }
}

