using System.Collections.Generic;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 初始化工作目录（步骤层）。
/// </summary>
[PipelineStep("InitWorkspace", RequiredKeys = new[] { "VideoTitle", "WorkspaceRoot" })]
public abstract class InitWorkspaceStepBase : IPipelineStep
{
    public string Step => "InitWorkspace";

    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

/// <summary>
/// 根据视频标题与工作区根目录计算子工作目录路径。
/// 依赖输入：VideoTitle, WorkspaceRoot
/// 输出：WorkspacePath
/// </summary>
[PipelineStep("InitWorkspace", Implementation = "Default")]
public sealed class DefaultInitWorkspaceStep : InitWorkspaceStepBase
{
    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var title = context["VideoTitle"];
        var root = context["WorkspaceRoot"];

        // 调用方（本地视频输入、或恢复既有任务）已经确定工作目录时保持不变，
        // 否则重新计算会把已复制到该目录的视频和已校验的阶段产物指向别处。
        if (context.TryGetValue("WorkspacePath", out var decidedPath) && !string.IsNullOrWhiteSpace(decidedPath))
        {
            return context;
        }

        var safeTitle = WorkspaceNaming.ToSafeDirectoryName(title);
        var workspacePath = Path.Combine(root, safeTitle);
        if (context.TryGetValue("AvoidWorkspaceOverwrite", out var avoidOverwrite) &&
            string.Equals(avoidOverwrite, "true", StringComparison.OrdinalIgnoreCase))
        {
            var basePath = workspacePath;
            for (var suffix = 2; Directory.Exists(workspacePath); suffix++)
                workspacePath = $"{basePath}_{suffix}";
        }

        context["WorkspacePath"] = workspacePath;
        return context;
    }
}
