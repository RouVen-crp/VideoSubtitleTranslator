using System.Diagnostics;
using System.Text.Json;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Cli;

internal sealed record CliDependencyCheck(bool IsAvailable, string Message);

internal sealed class CliDependencyDiagnostics
{
    private readonly string _baseDirectory;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _commandExists;
    private readonly Func<string, bool> _pythonPackagesAvailable;

    internal CliDependencyDiagnostics(
        string baseDirectory,
        Func<string, string?>? getEnvironmentVariable = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? commandExists = null,
        Func<string, bool>? pythonPackagesAvailable = null)
    {
        _baseDirectory = baseDirectory;
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _fileExists = fileExists ?? File.Exists;
        _commandExists = commandExists ?? CommandExists;
        _pythonPackagesAvailable = pythonPackagesAvailable ?? PythonPackagesAvailable;
    }

    internal IReadOnlyList<CliDependencyCheck> Check(string? target)
    {
        var checks = new List<CliDependencyCheck>();
        var configPath = Path.Combine(_baseDirectory, "Config", "translator.config.json");
        TranslatorRuntimeConfig? config = null;

        if (!_fileExists(configPath))
        {
            checks.Add(new(false, $"未找到配置文件 {DisplayPath(configPath)}。请从项目发布包恢复 Config/translator.config.json。"));
        }
        else
        {
            try
            {
                config = JsonSerializer.Deserialize<TranslatorRuntimeConfig>(File.ReadAllText(configPath), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                config ??= new TranslatorRuntimeConfig();
                checks.Add(new(true, $"翻译配置可读取：{DisplayPath(configPath)}"));
            }
            catch (Exception)
            {
                checks.Add(new(false, $"无法读取翻译配置 {DisplayPath(configPath)}。请恢复有效的 Config/translator.config.json。"));
            }
        }

        var includeDownload = target is null or "download";
        var includeTranslate = target is null or "translate";
        if (includeDownload)
        {
            var ytDlp = ResolveEnvironmentOverride("VST_YTDLP_PATH", config?.Tools.YtDlpPath ?? "yt-dlp");
            AddCommandCheck(checks, ytDlp, "yt-dlp", "用于下载在线视频。请安装 yt-dlp，或通过 VST_YTDLP_PATH 指定已有程序。");
        }

        if (!includeTranslate) return checks;

        if (!includeDownload)
        {
            var ytDlp = ResolveEnvironmentOverride("VST_YTDLP_PATH", config?.Tools.YtDlpPath ?? "yt-dlp");
            AddCommandCheck(checks, ytDlp, "yt-dlp", "仅在线翻译需要下载器。请安装 yt-dlp，或通过 VST_YTDLP_PATH 指定已有程序；本地视频可忽略此项。");
        }

        var pipelinePath = Path.Combine(_baseDirectory, "pipeline.config.json");
        checks.Add(new(_fileExists(pipelinePath), _fileExists(pipelinePath)
            ? $"翻译流水线配置可读取：{DisplayPath(pipelinePath)}"
            : $"未找到翻译流水线配置 {DisplayPath(pipelinePath)}。请恢复 pipeline.config.json。"));

        var ffmpeg = ResolveEnvironmentOverride("VST_FFMPEG_PATH", config?.Tools.FfmpegPath ?? "ffmpeg");
        var ffprobe = ResolveEnvironmentOverride("VST_FFPROBE_PATH", config?.Tools.FfprobePath ?? "ffprobe");
        AddCommandCheck(checks, ffmpeg, "ffmpeg", "用于音频提取及媒体处理。请安装 FFmpeg，或通过 VST_FFMPEG_PATH 指定已有程序。");
        AddCommandCheck(checks, ffprobe, "ffprobe", "用于检查视频轨道。请安装 FFmpeg，或通过 VST_FFPROBE_PATH 指定已有程序。");

        var python = ResolvePython(config?.Whisper.PythonExecutable ?? "python3");
        var pythonExists = _commandExists(python);
        checks.Add(new(pythonExists, pythonExists
            ? $"Python 可用：{Path.GetFileName(python)}"
            : $"找不到 Python（{Path.GetFileName(python)}）。请安装 Python 3，或通过 VST_PYTHON_PATH 指定已有程序。"));
        if (pythonExists)
        {
            var packagesAvailable = _pythonPackagesAvailable(python);
            checks.Add(new(packagesAvailable, packagesAvailable
                ? "Whisper 和 srt Python 依赖可导入。"
                : $"Python 缺少 Whisper 或 srt。请运行 VideoSubtitleTranslator/scripts/init_python_venv.sh，或在已有 Python 环境中安装 PythonScripts/requirements.txt 中的依赖。"));
        }

        var apiKeyEnv = string.IsNullOrWhiteSpace(config?.Llm.ApiKeyEnv) ? "DEEPSEEK_API_KEY" : config.Llm.ApiKeyEnv;
        var keyConfigured = !string.IsNullOrWhiteSpace(_getEnvironmentVariable(apiKeyEnv));
        checks.Add(new(keyConfigured, keyConfigured
            ? $"LLM 密钥环境变量已设置：{apiKeyEnv}（值不会显示）。"
            : $"未设置 LLM 密钥环境变量 {apiKeyEnv}。请在 shell 或本地 .env 中设置该变量；doctor 不会显示其值。"));
        return checks;
    }

    private string ResolvePython(string configuredPython)
    {
        var overridePath = _getEnvironmentVariable("VST_PYTHON_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath.Trim().Trim('"');
        if (!configuredPython.Equals("python3", StringComparison.OrdinalIgnoreCase) &&
            !configuredPython.Equals("python", StringComparison.OrdinalIgnoreCase)) return configuredPython;

        var scriptsDirectory = DefaultExtractSubtitleStep.ResolvePythonScriptsDirectory(_baseDirectory);
        var candidates = new[]
        {
            Path.Combine(scriptsDirectory, "venv", "bin", "python3"),
            Path.Combine(scriptsDirectory, "venv", "Scripts", "python.exe"),
            configuredPython
        };
        return candidates.FirstOrDefault(_commandExists) ?? configuredPython;
    }

    private string ResolveEnvironmentOverride(string variableName, string fallback)
    {
        var value = _getEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().Trim('"');
    }

    private void AddCommandCheck(List<CliDependencyCheck> checks, string executable, string name, string action)
    {
        var exists = _commandExists(executable);
        checks.Add(new(exists, exists
            ? $"{name} 可用：{Path.GetFileName(executable)}"
            : $"找不到 {name}。{action}"));
    }

    private static bool CommandExists(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return false;
        if (Path.IsPathRooted(executable) || executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(executable);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')
            : [string.Empty];
        return path.Split(Path.PathSeparator).Any(directory => extensions.Any(extension =>
            File.Exists(Path.Combine(directory, executable + extension))));
    }

    private static bool PythonPackagesAvailable(string python)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = python,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("import whisper, srt");
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string DisplayPath(string path) => Path.GetRelativePath(_baseDirectory, path);
}
