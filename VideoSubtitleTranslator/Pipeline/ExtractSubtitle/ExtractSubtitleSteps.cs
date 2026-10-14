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
        if (!configuredValue.Equals("python3", StringComparison.OrdinalIgnoreCase) &&
            !configuredValue.Equals("python", StringComparison.OrdinalIgnoreCase))
            return configuredValue;

        var windowsVenvPython = Path.Combine(PythonScriptsDir, "venv", "Scripts", "python.exe");
        if (File.Exists(windowsVenvPython))
            return windowsVenvPython;

        var outputVenvPython = Path.Combine(PythonScriptsDir, "venv", "bin", "python3");
        if (File.Exists(outputVenvPython))
            return outputVenvPython;

        var projectWindowsVenvPython = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "PythonScripts", "venv", "Scripts", "python.exe"));
        if (File.Exists(projectWindowsVenvPython))
            return projectWindowsVenvPython;

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

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add(Path.Combine(PythonScriptsDir, "subtitler.py"));
        process.StartInfo.ArgumentList.Add(dirs.AudioPath);
        process.StartInfo.ArgumentList.Add("--model");
        process.StartInfo.ArgumentList.Add(whisperConfig.Model);
        process.StartInfo.ArgumentList.Add("--language");
        process.StartInfo.ArgumentList.Add(whisperConfig.Language);
        process.StartInfo.ArgumentList.Add("--output");
        process.StartInfo.ArgumentList.Add(dirs.SubtitlePath);
        if (whisperConfig.Force) process.StartInfo.ArgumentList.Add("--force");
        process.StartInfo.ArgumentList.Add("--ffmpeg");
        process.StartInfo.ArgumentList.Add(GlobalRuntimeConfig.Current.Tools.FfmpegPath);
        if (!string.IsNullOrWhiteSpace(whisperConfig.ModelPath))
        {
            process.StartInfo.ArgumentList.Add("--whisper-model-path");
            process.StartInfo.ArgumentList.Add(whisperConfig.ModelPath);
        }
        if (whisperConfig.UseGpu) process.StartInfo.ArgumentList.Add("--whisper-use-gpu");

        if (!process.Start()) throw new InvalidOperationException("无法启动 Python Whisper 转录进程。");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);
        if (process.ExitCode != 0)
        {
            var details = string.Join(Environment.NewLine, new[] { outputTask.Result, errorTask.Result }.Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
            if (details.Length > 2000) details = details[^2000..];
            throw new InvalidOperationException($"Whisper 字幕提取失败（Python exit code {process.ExitCode}）：{details}");
        }
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

