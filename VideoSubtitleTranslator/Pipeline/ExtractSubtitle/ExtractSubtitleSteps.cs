using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

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
    private static readonly string PythonScriptsDir = ResolvePythonScriptsDirectory(
        AppContext.BaseDirectory);

    internal static string ResolvePythonScriptsDirectory(string baseDirectory)
    {
        var outputScriptsDirectory = Path.GetFullPath(
            Path.Combine(baseDirectory, "PythonScripts"));
        var projectScriptsDirectory = Path.GetFullPath(
            Path.Combine(baseDirectory, "..", "..", "..", "PythonScripts"));

        if (File.Exists(Path.Combine(projectScriptsDirectory, "subtitler.py")))
            return projectScriptsDirectory;

        return outputScriptsDirectory;
    }

    internal static string ResolvePythonExecutable(string configuredValue, string baseDirectory)
    {
        if (!configuredValue.Equals("python3", StringComparison.OrdinalIgnoreCase) &&
            !configuredValue.Equals("python", StringComparison.OrdinalIgnoreCase))
            return configuredValue;

        var scriptsDirectory = ResolvePythonScriptsDirectory(baseDirectory);
        var projectScriptsDirectory = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "VideoSubtitleTranslator", "PythonScripts"));
        var candidates = new[]
        {
            Path.Combine(scriptsDirectory, "venv", "Scripts", "python.exe"),
            Path.Combine(scriptsDirectory, "venv", "bin", "python3"),
            Path.Combine(scriptsDirectory, "venv", "bin", "python"),
            Path.Combine(projectScriptsDirectory, "venv", "Scripts", "python.exe"),
            Path.Combine(projectScriptsDirectory, "venv", "bin", "python3"),
            Path.Combine(projectScriptsDirectory, "venv", "bin", "python"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "PythonScripts", "venv", "Scripts", "python.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "PythonScripts", "venv", "bin", "python3"))
        };
        var venvPython = candidates.FirstOrDefault(File.Exists);
        if (venvPython is not null) return venvPython;

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
        var pythonExecutable = ResolvePythonExecutable(whisperConfig.PythonExecutable, AppContext.BaseDirectory);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
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
        var output = new StringBuilder();
        var outputLock = new object();
        DataReceivedEventHandler forwardLine = (_, eventArgs) =>
        {
            if (eventArgs.Data is null) return;
            lock (outputLock) output.AppendLine(eventArgs.Data);
            Console.Error.WriteLine(eventArgs.Data);
        };
        process.OutputDataReceived += forwardLine;
        process.ErrorDataReceived += forwardLine;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        // Wait again so asynchronous output/error data handlers have drained before reading their buffer.
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            string details;
            lock (outputLock) details = output.ToString().Trim();
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
