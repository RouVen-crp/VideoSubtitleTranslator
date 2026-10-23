using VideoSubtitleTranslator.Pipeline;
using System.Diagnostics;
using System.Text.Json;
using System.ComponentModel;
using System.Text;
using System.Runtime.InteropServices;

namespace VideoSubtitleTranslator.Cli;

public sealed class PipelineTranslationWorkflow : ICliWorkflow, ICliTaskWorkflow, ICliPlaybackWorkflow, ICliQueueWorkflow
{
    private readonly Func<string, CancellationToken, Task<string?>> _mediaValidator;
    private readonly Func<PipelineConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>> _pipelineRunner;
    private readonly bool _hasInjectedPipelineRunner;
    private readonly Func<string, IReadOnlyList<string>, string, CancellationToken, Task<int>> _mediaProcessRunner;
    private readonly CliTaskStore _taskStore;
    private readonly CliPipelineExecutor _taskPipelineExecutor;
    private readonly VideoPlaybackService _videoPlayback;

    public PipelineTranslationWorkflow(
        Func<string, CancellationToken, Task<string?>>? mediaValidator = null,
        Func<PipelineConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>>? pipelineRunner = null,
        Func<string, IReadOnlyList<string>, string, CancellationToken, Task<int>>? mediaProcessRunner = null,
        CliTaskStore? taskStore = null,
        Func<PipelineStepConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>>? pipelineStepRunner = null,
        TextWriter? progressOutput = null,
        TimeSpan? progressHeartbeatInterval = null,
        VideoPlaybackService? videoPlayback = null)
    {
        _mediaValidator = mediaValidator ?? ValidateMediaAsync;
        _hasInjectedPipelineRunner = pipelineRunner is not null;
        _pipelineRunner = pipelineRunner ?? ((config, context, runtime) => new PipelineRunner(new PipelineRegistry()).Run(config, context, runtime));
        _mediaProcessRunner = mediaProcessRunner ?? RunMediaProcessAsync;
        _taskStore = taskStore ?? new CliTaskStore();
        _taskPipelineExecutor = new CliPipelineExecutor(_taskStore, pipelineStepRunner, progressOutput, progressHeartbeatInterval);
        _videoPlayback = videoPlayback ?? new VideoPlaybackService();
    }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) =>
        ExecuteTaskAsync(_taskStore.Create(request), cancellationToken, request.Input);

    public Task<CliTaskRecord> EnqueueTranslationAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = _taskStore.Create(request);
        task.QueuePosition = _taskStore.List().FirstOrDefault(candidate => candidate.Id == task.Id)?.QueuePosition;
        return Task.FromResult(task);
    }

    public Task<TranslationResult> RunQueuedTaskAsync(
        string taskId,
        string executionInput,
        Func<IReadOnlyList<CliTaskRecord>, Task>? onQueueChanged = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = _taskStore.Get(taskId) ?? throw new InvalidOperationException("找不到该任务。");
        if (task.Status != CliTaskStatus.Queued)
            throw new InvalidOperationException("只有排队中的任务可以开始。");

        return ExecuteTaskAsync(task, cancellationToken, executionInput, onStarted: async () =>
        {
            if (onQueueChanged is not null)
                await onQueueChanged(_taskStore.List());
        });
    }

    public Task InterruptQueuedTasksAsync(IEnumerable<string> taskIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var taskId in taskIds)
        {
            var task = _taskStore.Get(taskId);
            if (task is null || task.Status != CliTaskStatus.Queued) continue;
            task.Status = CliTaskStatus.Interrupted;
            task.Stage = "InterruptedBeforeStart";
            _taskStore.Save(task);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CliTaskRecord>> GetTaskHistoryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_taskStore.List());
    }

    public Task<TranslationResult> ResumeTaskAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = _taskStore.Get(id) ?? throw new InvalidOperationException("找不到该任务。");
        if (task.Status is not (CliTaskStatus.Interrupted or CliTaskStatus.Failed))
            throw new InvalidOperationException("只有失败或中断的任务可以恢复。");
        return ExecuteTaskAsync(task, cancellationToken);
    }

    public Task<VideoPlaybackResult> PlayTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = _taskStore.Get(taskId) ?? throw new InvalidOperationException("找不到该任务。请运行 zimu history 查看任务 ID。");
        if (task.Status != CliTaskStatus.Completed)
            throw new InvalidOperationException("该任务尚未完成，暂时无法播放。请运行 zimu history 查看任务状态。");
        if (string.IsNullOrWhiteSpace(task.SubtitlePath) || !File.Exists(task.SubtitlePath))
            throw new InvalidOperationException("任务的中文字幕文件不存在。请先恢复任务，或重新运行 translate。");

        string videoPath;
        if (task.IsLocalFile)
        {
            videoPath = task.Input;
        }
        else if (string.IsNullOrWhiteSpace(task.WorkspacePath) ||
                 !task.Context.TryGetValue("VideoExt", out var extension) || string.IsNullOrWhiteSpace(extension))
        {
            throw new InvalidOperationException("任务的视频文件信息不完整。请检查任务目录后重试。");
        }
        else
        {
            videoPath = new WorkDirs(task.WorkspacePath, extension).VideoPath;
        }

        return Task.FromResult(_videoPlayback.Play(videoPath, task.SubtitlePath));
    }

    private async Task<TranslationResult> ExecuteTaskAsync(
        CliTaskRecord task,
        CancellationToken cancellationToken,
        string? inputOverride = null,
        Func<Task>? onStarted = null)
    {
        var request = new TranslationRequest(inputOverride ?? task.Input, task.Mode, task.WorkspaceRoot, task.IsLocalFile, task.Bilingual, task.Burn);
        task.Status = CliTaskStatus.Running;
        task.Stage = "Translating";
        task.ProcessId = Environment.ProcessId;
        _taskStore.Save(task);

        try
        {
            if (onStarted is not null) await onStarted();
            Program.LoadEnv();
            cancellationToken.ThrowIfCancellationRequested();
            var result = request.IsLocalFile
                ? await TranslateLocalAsync(request, task, cancellationToken)
                : await TranslateRemoteAsync(request, task, cancellationToken);
            task.Status = CliTaskStatus.Completed;
            task.Stage = "Completed";
            task.SubtitlePath = result.SubtitlePath;
            _taskStore.Save(task);
            return result with { TaskId = task.Id };
        }
        catch (OperationCanceledException)
        {
            task.Status = CliTaskStatus.Interrupted;
            task.Stage = "Interrupted";
            _taskStore.Save(task);
            throw;
        }
        catch
        {
            task.Status = CliTaskStatus.Failed;
            task.Stage = "Failed";
            _taskStore.Save(task);
            throw;
        }
    }

    private async Task<TranslationResult> TranslateRemoteAsync(TranslationRequest request, CliTaskRecord task, CancellationToken cancellationToken)
    {

        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths(request.Mode, AppContext.BaseDirectory);
        var translatorConfig = LoadCliTranslatorConfig(translatorPath, request.Mode);

        var initialContext = new Dictionary<string, string>(task.Context, StringComparer.OrdinalIgnoreCase)
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
            var context = _hasInjectedPipelineRunner
                ? _pipelineRunner(pipelineConfig, initialContext, translatorConfig)
                : _taskPipelineExecutor.Execute(pipelineConfig, initialContext, translatorConfig, task);
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

    private async Task<TranslationResult> TranslateLocalAsync(TranslationRequest request, CliTaskRecord task, CancellationToken cancellationToken)
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
        var workspacePath = task.WorkspacePath ?? GetAvailableWorkspacePath(request.WorkspaceRoot, title);
        task.WorkspacePath = workspacePath;
        _taskStore.Save(task);
        var initialContext = new Dictionary<string, string>(task.Context, StringComparer.OrdinalIgnoreCase)
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
            if (!File.Exists(dirs.VideoPath)) File.Copy(request.Input, dirs.VideoPath, overwrite: false);

            var config = PipelineRunner.LoadConfig(pipelinePath);
            config.Steps = config.Steps
                .Where(step => step.Step is not ("InitMetadata" or "Download" or "BurnSubtitle"))
                .ToList();
            var context = _hasInjectedPipelineRunner
                ? _pipelineRunner(config, initialContext, translatorConfig)
                : _taskPipelineExecutor.Execute(config, initialContext, translatorConfig, task);
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
