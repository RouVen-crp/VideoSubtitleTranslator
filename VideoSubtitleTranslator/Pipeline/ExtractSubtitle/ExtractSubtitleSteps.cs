using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 提取原始字幕（步骤层）。
/// </summary>
[PipelineStep("ExtractSubtitle", RequiredKeys = new[] { "WorkspacePath", "VideoExt" })]
public abstract class ExtractSubtitleStepBase : IPipelineStep
{
    public string Step => "ExtractSubtitle";

    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

/// <summary>
/// 调用 Python/Whisper 提取原始字幕的默认实现（暂时仍委托 SubtitleExtractor，后续会内联）。
/// </summary>
[PipelineStep("ExtractSubtitle", Implementation = "Default")]
public sealed class DefaultExtractSubtitleStep : ExtractSubtitleStepBase
{
    private static readonly string PythonScriptsDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "PythonScripts")
    );

    private static string ResolvePythonExecutable(string configuredValue)
    {
        if (!configuredValue.Equals("python3", StringComparison.OrdinalIgnoreCase))
            return configuredValue;

        var outputVenvPython = Path.Combine(PythonScriptsDir, "venv", "bin", "python3");
        if (File.Exists(outputVenvPython))
            return outputVenvPython;

        var projectVenvPython = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "PythonScripts", "venv", "bin", "python3"));
        if (File.Exists(projectVenvPython))
            return projectVenvPython;

        return "python3";
    }

    private static void Extract(WorkDirs dirs)
    {
        if (File.Exists(dirs.SubtitlePath))
        {
            Console.WriteLine("Subtitle exists, skip extraction");
            return;
        }

        var whisperConfig = GlobalRuntimeConfig.Current.Whisper;
        var pythonExecutable = ResolvePythonExecutable(whisperConfig.PythonExecutable);

        var forceArg = whisperConfig.Force ? " --force" : string.Empty;
        using var process = Process.Start(
            pythonExecutable,
            $"{PythonScriptsDir}/subtitler.py \"{dirs.AudioPath}\" --model {whisperConfig.Model} --language {whisperConfig.Language} --output \"{dirs.SubtitlePath}\"{forceArg}"
        );
        process?.WaitForExit();
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var workspacePath = context["WorkspacePath"];
        var ext = context["VideoExt"];
        var dirs = new WorkDirs(workspacePath, ext);

        Extract(dirs);
        return context;
    }
}

