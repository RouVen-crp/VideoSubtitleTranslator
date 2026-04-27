using System.Collections.Generic;
using System.Text.RegularExpressions;

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

        var safeTitle = Regex.Replace(title, @"\s", "_");
        safeTitle = Regex.Replace(safeTitle, "\"", "");
        safeTitle = Regex.Replace(safeTitle, ",", "");
        safeTitle = Regex.Replace(safeTitle, "'", "");
        var workspacePath = $"{root}/{safeTitle}";

        context["WorkspacePath"] = workspacePath;
        return context;
    }
}

