using VideoSubtitleTranslator.Pipeline;
using System.Diagnostics;
using System.Text.Json;
using System.ComponentModel;
using System.Text;

namespace VideoSubtitleTranslator.Cli;

public sealed class PipelineTranslationWorkflow : ICliWorkflow
{
    private readonly Func<string, CancellationToken, Task<string?>> _mediaValidator;
    private readonly Func<PipelineConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>> _pipelineRunner;

    public PipelineTranslationWorkflow(
        Func<string, CancellationToken, Task<string?>>? mediaValidator = null,
        Func<PipelineConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>>? pipelineRunner = null)
    {
        _mediaValidator = mediaValidator ?? ValidateMediaAsync;
        _pipelineRunner = pipelineRunner ?? ((config, context, runtime) => new PipelineRunner(new PipelineRegistry()).Run(config, context, runtime));
    }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Program.LoadEnv();

        if (request.IsLocalFile)
            return TranslateLocalAsync(request, cancellationToken);

        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths(request.Mode, AppContext.BaseDirectory);
        var translatorConfig = LoadCliTranslatorConfig(translatorPath, request.Mode);

        var initialContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Url"] = request.Input,
            ["WorkspaceRoot"] = request.WorkspaceRoot,
            ["ConfigPath"] = pipelinePath,
            ["TranslatorConfigPath"] = translatorPath,
            ["Mode"] = request.Mode
        };

        var pipelineOutput = Console.Out;
        try
        {
            Console.SetOut(Console.Error);
            var pipelineConfig = PipelineRunner.LoadConfig(pipelinePath);
            var runner = new PipelineRunner(new PipelineRegistry());
            var context = runner.Run(pipelineConfig, initialContext, translatorConfig);
            var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
            var subtitlePath = File.Exists(dirs.ProofreadSubtitlePath)
                ? dirs.ProofreadSubtitlePath
                : dirs.TranslatedSubtitlePath;

            if (!File.Exists(subtitlePath))
                throw new InvalidOperationException("The translation pipeline produced no subtitle artifact.");

            return Task.FromResult(new TranslationResult(subtitlePath));
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
            config.Steps = config.Steps.Where(step => step.Step is not ("InitMetadata" or "Download" or "BurnSubtitle")).ToList();
            var context = _pipelineRunner(config, initialContext, translatorConfig);
            dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
            var subtitlePath = File.Exists(dirs.ProofreadSubtitlePath) ? dirs.ProofreadSubtitlePath : dirs.TranslatedSubtitlePath;
            if (!File.Exists(subtitlePath))
                throw new InvalidOperationException("The translation pipeline produced no subtitle artifact.");

            var sidecar = GetAvailableSidecarPath(request.Input);
            File.Copy(subtitlePath, sidecar, overwrite: false);
            return new TranslationResult(subtitlePath, sidecar);
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
        // Keep the CLI's fast SRT path independent of the legacy Web workbench defaults.
        config.Burn.Enabled = false;
        return config;
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
