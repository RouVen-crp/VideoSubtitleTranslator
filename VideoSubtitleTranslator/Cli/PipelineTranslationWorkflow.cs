using VideoSubtitleTranslator.Pipeline;
using System.Diagnostics;
using System.Text.Json;
using System.ComponentModel;
using System.Text;
using System.Runtime.InteropServices;

namespace VideoSubtitleTranslator.Cli;

public sealed class PipelineTranslationWorkflow : ICliWorkflow
{
    private readonly Func<string, CancellationToken, Task<string?>> _mediaValidator;
    private readonly Func<PipelineConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>> _pipelineRunner;
    private readonly Func<string, IReadOnlyList<string>, string, CancellationToken, Task<int>> _mediaProcessRunner;

    public PipelineTranslationWorkflow(
        Func<string, CancellationToken, Task<string?>>? mediaValidator = null,
        Func<PipelineConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>>? pipelineRunner = null,
        Func<string, IReadOnlyList<string>, string, CancellationToken, Task<int>>? mediaProcessRunner = null)
    {
        _mediaValidator = mediaValidator ?? ValidateMediaAsync;
        _pipelineRunner = pipelineRunner ?? ((config, context, runtime) => new PipelineRunner(new PipelineRegistry()).Run(config, context, runtime));
        _mediaProcessRunner = mediaProcessRunner ?? RunMediaProcessAsync;
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Program.LoadEnv();

        if (request.IsLocalFile)
            return await TranslateLocalAsync(request, cancellationToken);

        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths(request.Mode, AppContext.BaseDirectory);
        var translatorConfig = LoadCliTranslatorConfig(translatorPath, request.Mode);

        var initialContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Url"] = request.Input,
            ["WorkspaceRoot"] = request.WorkspaceRoot,
            ["ConfigPath"] = pipelinePath,
            ["TranslatorConfigPath"] = translatorPath,
            ["Mode"] = request.Mode,
            ["AvoidWorkspaceOverwrite"] = "true"
        };

        var pipelineOutput = Console.Out;
        try
        {
            Console.SetOut(Console.Error);
            var pipelineConfig = PipelineRunner.LoadConfig(pipelinePath);
            pipelineConfig.Steps = pipelineConfig.Steps
                .Where(step => step.Step != "BurnSubtitle")
                .ToList();
            var context = _pipelineRunner(pipelineConfig, initialContext, translatorConfig);
            var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
            return await CreateTranslationResultAsync(dirs, request, translatorConfig, cancellationToken);
        }
        finally
        {
            Console.SetOut(pipelineOutput);
        }
    }

    public async Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Program.LoadEnv();
        var (_, translatorPath) = Program.ResolveConfigPaths("standard", AppContext.BaseDirectory);
        var config = TranslatorRuntimeConfig.Load(translatorPath);
        Directory.CreateDirectory(request.OutputDirectory);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = config.Tools.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        var arguments = new List<string>
        {
            "--ignore-config", "--no-playlist", "--no-warnings", "--js-runtimes", "node",
            "--print", "after_move:filepath",
            "--output", Path.Combine(request.OutputDirectory, "%(title)s.%(ext)s")
        };
        var cookiesFile = Environment.GetEnvironmentVariable("COOKIES_FILE");
        if (!string.IsNullOrWhiteSpace(cookiesFile) && File.Exists(cookiesFile))
        {
            arguments.Add("--cookies");
            arguments.Add(cookiesFile);
        }
        else if (!string.IsNullOrWhiteSpace(config.CookiesFromBrowser))
        {
            arguments.Add("--cookies-from-browser");
            arguments.Add(config.CookiesFromBrowser);
        }
        arguments.Add(request.Url);

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }

            var downloadedPath = (await outputTask).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(downloadedPath) || !File.Exists(downloadedPath))
                throw new VideoDownloadException("视频下载失败。请确认链接可访问，并检查 yt-dlp 和网络后重试。");

            return new DownloadResult(downloadedPath);
        }
        catch (Win32Exception)
        {
            throw new VideoDownloadException("找不到 yt-dlp。请安装 yt-dlp 后重试。");
        }
    }

    private async Task<TranslationResult> TranslateLocalAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.Input))
            throw new FileNotFoundException("本地视频文件不存在。", request.Input);
        var validationError = await _mediaValidator(request.Input, cancellationToken);
        if (!string.IsNullOrWhiteSpace(validationError))
            throw new LocalVideoValidationException(validationError);

        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths(request.Mode, AppContext.BaseDirectory);
        var translatorConfig = LoadCliTranslatorConfig(translatorPath, request.Mode);

        var extension = Path.GetExtension(request.Input);
        var title = Path.GetFileNameWithoutExtension(request.Input);
        var workspacePath = GetAvailableWorkspacePath(request.WorkspaceRoot, title);
        var initialContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Url"] = request.Input,
            ["WorkspaceRoot"] = request.WorkspaceRoot,
            ["WorkspacePath"] = workspacePath,
            ["VideoExt"] = extension,
            ["VideoTitle"] = title,
            ["VideoAuthor"] = string.Empty,
            ["VideoUploadDate"] = string.Empty,
            ["Mode"] = request.Mode
        };

        var pipelineOutput = Console.Out;
        try
        {
            Console.SetOut(Console.Error);
            Directory.CreateDirectory(initialContext["WorkspacePath"]);
            var dirs = new WorkDirs(initialContext["WorkspacePath"], extension);
            File.Copy(request.Input, dirs.VideoPath, overwrite: false);

            var config = PipelineRunner.LoadConfig(pipelinePath);
            config.Steps = config.Steps
                .Where(step => step.Step is not ("InitMetadata" or "Download" or "BurnSubtitle"))
                .ToList();
            var context = _pipelineRunner(config, initialContext, translatorConfig);
            dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
            var result = await CreateTranslationResultAsync(dirs, request, translatorConfig, cancellationToken);
            var sidecar = GetAvailableSidecarPath(request.Input);
            File.Copy(result.SubtitlePath, sidecar, overwrite: false);
            return result with { SidecarSubtitlePath = sidecar };
        }
        finally
        {
            Console.SetOut(pipelineOutput);
        }
    }

    private static TranslatorRuntimeConfig LoadCliTranslatorConfig(string translatorPath, string mode)
    {
        var config = TranslatorRuntimeConfig.Load(translatorPath);
        config.Mode = mode;
        return config;
    }

    private async Task<TranslationResult> CreateTranslationResultAsync(
        WorkDirs dirs,
        TranslationRequest request,
        TranslatorRuntimeConfig config,
        CancellationToken cancellationToken)
    {
        var subtitlePath = File.Exists(dirs.ProofreadSubtitlePath)
            ? dirs.ProofreadSubtitlePath
            : dirs.TranslatedSubtitlePath;
        if (!File.Exists(subtitlePath))
            throw new InvalidOperationException("The translation pipeline produced no subtitle artifact.");

        var bilingualPath = request.CreateBilingualSubtitle
            ? BilingualSubtitleBuilder.Create(dirs.NormalizedSubtitlePath, subtitlePath, dirs.CustomPath(""))
            : null;
        var burnSubtitlePath = File.Exists(dirs.DualSubtitlePath) ? dirs.DualSubtitlePath : subtitlePath;
        var burnedPath = request.BurnSubtitles
            ? await BurnVideoAsync(dirs.VideoPath, burnSubtitlePath, dirs.CustomPath(""), config, cancellationToken)
            : null;

        return new TranslationResult(subtitlePath, BilingualSubtitlePath: bilingualPath, BurnedVideoPath: burnedPath);
    }

    private async Task<string> BurnVideoAsync(
        string videoPath,
        string subtitlePath,
        string workspacePath,
        TranslatorRuntimeConfig config,
        CancellationToken cancellationToken)
    {
        var outputPath = GetAvailableArtifactPath(workspacePath, "burned_video", ".mp4");
        var safeDirectory = Path.Combine(Path.GetTempPath(), "zimu-burn", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(safeDirectory);
        var safeSubtitlePath = Path.Combine(safeDirectory, "subtitle" + Path.GetExtension(subtitlePath));
        File.Copy(subtitlePath, safeSubtitlePath);
        try
        {
            var arguments = new List<string>
            {
                "-n", "-i", videoPath, "-vf", $"subtitles='{Path.GetFileName(safeSubtitlePath)}'"
            };
            var qualityPreset = string.IsNullOrWhiteSpace(config.Burn.QualityPreset) ? "auto" : config.Burn.QualityPreset.Trim();
            if (string.Equals(qualityPreset, "high", StringComparison.OrdinalIgnoreCase))
            {
                var encoder = string.IsNullOrWhiteSpace(config.Burn.VideoEncoder) ||
                              string.Equals(config.Burn.VideoEncoder, "auto", StringComparison.OrdinalIgnoreCase)
                    ? RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "h264_videotoolbox" : "libx264"
                    : config.Burn.VideoEncoder;
                arguments.AddRange(["-c:v", encoder]);
            }
            arguments.AddRange(["-c:a", "copy", outputPath]);

            var exitCode = await _mediaProcessRunner(config.Tools.FfmpegPath, arguments, safeDirectory, cancellationToken);
            if (exitCode != 0 || !File.Exists(outputPath))
                throw new InvalidOperationException("ffmpeg 未能生成烧录视频。");
            return outputPath;
        }
        finally
        {
            try { Directory.Delete(safeDirectory, recursive: true); } catch { }
        }
    }

    private static string GetAvailableArtifactPath(string directory, string name, string extension)
    {
        Directory.CreateDirectory(directory);
        var candidate = Path.Combine(directory, name + extension);
        for (var suffix = 2; File.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{name}.{suffix}{extension}");
        return candidate;
    }

    private static async Task<int> RunMediaProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutput, standardError);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("找不到 ffmpeg。请安装 FFmpeg 后重试。");
        }
    }

    private static string GetAvailableWorkspacePath(string workspaceRoot, string title)
    {
        var candidate = Path.Combine(workspaceRoot, title);
        for (var suffix = 2; Directory.Exists(candidate); suffix++)
            candidate = Path.Combine(workspaceRoot, $"{title}_{suffix}");
        return candidate;
    }

    private static string GetAvailableSidecarPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath)!;
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var candidate = Path.Combine(directory, $"{name}.zh.srt");
        for (var suffix = 2; File.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{name}.zh.{suffix}.srt");
        return candidate;
    }

    private static async Task<string?> ValidateMediaAsync(string path, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-v", "error", "-show_entries", "stream=codec_type", "-of", "json", path })
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            _ = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                return "无法读取视频媒体信息。请确认文件未损坏，并安装 ffprobe。";

            using var document = JsonDocument.Parse(await outputTask);
            var streamTypes = document.RootElement.GetProperty("streams").EnumerateArray()
                .Select(stream => stream.GetProperty("codec_type").GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return streamTypes.Contains("video") && streamTypes.Contains("audio")
                ? null
                : "视频必须同时包含可读取的视频和音频轨道。";
        }
        catch (Win32Exception)
        {
            return "找不到 ffprobe。请安装 FFmpeg 后重试。";
        }
        catch (JsonException)
        {
            return "无法读取视频媒体信息。请确认文件未损坏且格式受 FFmpeg 支持。";
        }
    }
}
