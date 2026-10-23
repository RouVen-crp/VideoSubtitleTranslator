namespace VideoSubtitleTranslator.Cli;

public static class CliApplication
{
    private const int Success = 0;
    private const int UsageError = 2;
    private const int TranslationError = 1;

    public static async Task<int> RunAsync(
        string[] args,
        ICliWorkflow workflow,
        TextWriter standardOutput,
        TextWriter standardError,
        bool isInteractive,
        string currentDirectory,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            if (isInteractive)
            {
                standardOutput.WriteLine("zimu 交互界面即将开放。现在可以运行：");
                WriteHelp(standardOutput);
                return Success;
            }

            standardError.WriteLine("未提供命令。请先运行以下命令：");
            WriteHelp(standardError);
            return UsageError;
        }

        if (args.Length == 1 && IsHelp(args[0]))
        {
            WriteHelp(standardOutput);
            return Success;
        }

        if (string.Equals(args[0], "history", StringComparison.OrdinalIgnoreCase))
        {
            if (workflow is not ICliTaskWorkflow taskWorkflow || args.Length != 1)
            {
                standardError.WriteLine("用法：zimu history");
                return UsageError;
            }
            var tasks = await taskWorkflow.GetTaskHistoryAsync(cancellationToken);
            if (tasks.Count == 0) standardOutput.WriteLine("暂无翻译任务。");
            else
            {
                standardOutput.WriteLine("ID       状态       阶段         输入");
                foreach (var task in tasks)
                    standardOutput.WriteLine($"{task.Id}  {StatusLabel(task.Status),-8} {task.Stage,-12} {CliInputSanitizer.Sanitize(task.Input)}");
            }
            standardOutput.WriteLine("提示：zimu resume <ID> 恢复失败或中断的任务；--help 查看命令。");
            return Success;
        }

        if (string.Equals(args[0], "resume", StringComparison.OrdinalIgnoreCase))
        {
            if (workflow is not ICliTaskWorkflow taskWorkflow || args.Length != 2 || !CliTaskStore.IsValidId(args[1]))
            {
                standardError.WriteLine("用法：zimu resume <任务ID>");
                return UsageError;
            }
            standardError.WriteLine("恢复任务时，尚未完成的模型请求可能会重新执行并产生费用。");
            try
            {
                var resumed = await taskWorkflow.ResumeTaskAsync(args[1], cancellationToken);
                if (string.IsNullOrWhiteSpace(resumed.SubtitlePath) || !File.Exists(resumed.SubtitlePath))
                {
                    standardError.WriteLine("恢复后未找到中文字幕文件。");
                    return TranslationError;
                }
                standardOutput.WriteLine($"任务 {args[1]} 已完成：{Path.GetFullPath(resumed.SubtitlePath)}");
                return Success;
            }
            catch (Exception)
            {
                standardError.WriteLine("任务恢复失败。请运行 zimu history 查看任务状态和阶段。");
                return TranslationError;
            }
        }

        if (string.Equals(args[0], "download", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length == 2 && IsHelp(args[1]))
            {
                WriteDownloadHelp(standardOutput);
                return Success;
            }

            if (!TryParseDownload(args[1..], currentDirectory, out var downloadRequest, out var downloadError))
            {
                standardError.WriteLine(downloadError);
                WriteDownloadHelp(standardError);
                return UsageError;
            }

            try
            {
                var result = await workflow.DownloadAsync(downloadRequest!, cancellationToken);
                if (string.IsNullOrWhiteSpace(result.VideoPath) || !File.Exists(result.VideoPath))
                {
                    standardError.WriteLine("下载未生成可用的视频文件。请检查链接和 yt-dlp 后重试。");
                    return TranslationError;
                }

                standardOutput.WriteLine($"视频已下载：{Path.GetFullPath(result.VideoPath)}");
                return Success;
            }
            catch (OperationCanceledException)
            {
                standardError.WriteLine("下载已取消。");
                return TranslationError;
            }
            catch (VideoDownloadException exception)
            {
                standardError.WriteLine(exception.Message);
                return TranslationError;
            }
            catch (Exception)
            {
                standardError.WriteLine("视频下载失败。请检查链接、yt-dlp 和网络后重试。");
                return TranslationError;
            }
        }

        if (!string.Equals(args[0], "translate", StringComparison.OrdinalIgnoreCase))
        {
            standardError.WriteLine("未知命令。");
            WriteHelp(standardError);
            return UsageError;
        }

        if (args.Length == 2 && IsHelp(args[1]))
        {
            WriteTranslateHelp(standardOutput);
            return Success;
        }

        if (!TryParseTranslate(args[1..], currentDirectory, out var request, out var error))
        {
            standardError.WriteLine(error);
            WriteTranslateHelp(standardError);
            return UsageError;
        }

        try
        {
            var result = await workflow.TranslateAsync(request!, cancellationToken);
            if (string.IsNullOrWhiteSpace(result.SubtitlePath) || !File.Exists(result.SubtitlePath))
            {
                standardError.WriteLine("翻译未生成可用的中文字幕文件。请检查输入视频和翻译依赖后重试。");
                return TranslationError;
            }

            if (request!.CreateBilingualSubtitle &&
                (string.IsNullOrWhiteSpace(result.BilingualSubtitlePath) || !File.Exists(result.BilingualSubtitlePath)))
            {
                standardError.WriteLine("未能生成可用的双语字幕文件。");
                return TranslationError;
            }

            if (request.BurnSubtitles &&
                (string.IsNullOrWhiteSpace(result.BurnedVideoPath) || !File.Exists(result.BurnedVideoPath)))
            {
                standardError.WriteLine("未能生成可用的字幕烧录视频。");
                return TranslationError;
            }

            standardOutput.WriteLine($"中文字幕已生成：{Path.GetFullPath(result.SubtitlePath)}");
            if (!string.IsNullOrWhiteSpace(result.TaskId)) standardOutput.WriteLine($"任务 ID：{result.TaskId}");
            if (!string.IsNullOrWhiteSpace(result.SidecarSubtitlePath))
                standardOutput.WriteLine($"源视频旁的字幕：{Path.GetFullPath(result.SidecarSubtitlePath)}");
            if (!string.IsNullOrWhiteSpace(result.BilingualSubtitlePath))
                standardOutput.WriteLine($"双语字幕已生成：{Path.GetFullPath(result.BilingualSubtitlePath)}");
            if (!string.IsNullOrWhiteSpace(result.BurnedVideoPath))
                standardOutput.WriteLine($"字幕烧录视频已生成：{Path.GetFullPath(result.BurnedVideoPath)}");
            return Success;
        }
        catch (OperationCanceledException)
        {
            standardError.WriteLine("翻译已取消。");
            return TranslationError;
        }
        catch (LocalVideoValidationException exception)
        {
            standardError.WriteLine(exception.Message);
            return TranslationError;
        }
        catch (Exception)
        {
            // External tool and model exceptions can contain request details. Keep CLI diagnostics
            // actionable without echoing arbitrary exception text that could include secret values.
            standardError.WriteLine("翻译失败。请检查视频链接、yt-dlp、ffmpeg、Python/Whisper、网络和配置后重试。");
            return TranslationError;
        }
    }

    private static bool TryParseDownload(
        string[] args,
        string currentDirectory,
        out DownloadRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;
        if (args.Length != 1)
        {
            error = "download 需要且只接受一个 HTTP 或 HTTPS 视频 URL。";
            return false;
        }

        if (!Uri.TryCreate(args[0], UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            error = "请输入有效的 HTTP 或 HTTPS 视频 URL。";
            return false;
        }

        request = new DownloadRequest(
            uri.AbsoluteUri,
            Program.ResolveDefaultWorkspace(currentDirectory));
        return true;
    }

    private static bool TryParseTranslate(
        string[] args,
        string currentDirectory,
        out TranslationRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;
        var positional = new List<string>();
        var mode = "standard";
        string? workspaceRoot = null;
        var bilingual = false;
        var burn = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode":
                    if (i + 1 >= args.Length)
                    {
                        error = "--mode 需要 standard 或 meme。";
                        return false;
                    }

                    mode = args[++i].ToLowerInvariant();
                    if (mode is not ("standard" or "meme"))
                    {
                        error = "不支持的翻译模式。请选择 standard 或 meme。";
                        return false;
                    }
                    break;

                case "--workspace":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        error = "--workspace 需要一个目录路径。";
                        return false;
                    }

                    workspaceRoot = Path.GetFullPath(args[++i], currentDirectory);
                    break;

                case "--bilingual":
                    if (bilingual)
                    {
                        error = "--bilingual 只能指定一次。";
                        return false;
                    }
                    bilingual = true;
                    break;

                case "--burn":
                    if (burn)
                    {
                        error = "--burn 只能指定一次。";
                        return false;
                    }
                    burn = true;
                    break;

                default:
                    if (args[i].StartsWith("-", StringComparison.Ordinal))
                    {
                        error = "未知选项。";
                        return false;
                    }
                    positional.Add(args[i]);
                    break;
            }
        }

        if (positional.Count != 1)
        {
            error = "translate 需要且只接受一个视频 URL 或本地视频路径。";
            return false;
        }

        if (Uri.TryCreate(positional[0], UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            workspaceRoot ??= Program.ResolveDefaultWorkspace(currentDirectory);
            request = new TranslationRequest(uri.AbsoluteUri, mode, workspaceRoot, CreateBilingualSubtitle: bilingual, BurnSubtitles: burn);
            return true;
        }

        var localPath = Path.GetFullPath(positional[0], currentDirectory);
        if (!File.Exists(localPath))
        {
            error = "本地视频文件不存在。请检查路径后重试。";
            return false;
        }

        if (!new[] { ".mp4", ".mkv", ".mov", ".webm", ".avi", ".m4v" }
                .Contains(Path.GetExtension(localPath), StringComparer.OrdinalIgnoreCase))
        {
            error = "不支持此视频格式。请提供 MP4、MKV、MOV、WebM、AVI 或 M4V 文件。";
            return false;
        }

        workspaceRoot ??= Program.ResolveDefaultWorkspace(currentDirectory);
        request = new TranslationRequest(
            localPath, mode, workspaceRoot, IsLocalFile: true,
            CreateBilingualSubtitle: bilingual, BurnSubtitles: burn);
        return true;
    }

    private static bool IsHelp(string value) => value is "--help" or "-h";

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("zimu - 轻量视频字幕翻译工具");
        writer.WriteLine();
        writer.WriteLine("用法：");
        writer.WriteLine("  zimu translate <URL|视频路径> [--mode standard|meme] [--bilingual] [--burn] [--workspace <目录>]");
        writer.WriteLine("  zimu download <URL>");
        writer.WriteLine("  zimu history");
        writer.WriteLine("  zimu resume <任务ID>");
        writer.WriteLine("  zimu --help");
        writer.WriteLine();
        writer.WriteLine("示例：");
        writer.WriteLine("  zimu translate https://example.com/video");
        writer.WriteLine("  zm translate https://example.com/video");
        writer.WriteLine("  zimu translate https://example.com/video --mode meme");
        writer.WriteLine("  zimu download https://example.com/video");
    }

    private static string StatusLabel(CliTaskStatus status) => status switch
    {
        CliTaskStatus.Queued => "排队中", CliTaskStatus.Running => "运行中", CliTaskStatus.Completed => "已完成",
        CliTaskStatus.Failed => "失败", CliTaskStatus.Interrupted => "已中断", _ => "未知"
    };

    private static void WriteDownloadHelp(TextWriter writer)
    {
        writer.WriteLine("用法：zimu download <URL>");
        writer.WriteLine("示例：zimu download https://example.com/video");
        writer.WriteLine("下载在线视频，不执行转录或翻译。视频保存在默认工作区目录。");
    }

    private static void WriteTranslateHelp(TextWriter writer)
    {
        writer.WriteLine("用法：zimu translate <URL|视频路径> [--mode standard|meme] [--bilingual] [--burn] [--workspace <目录>]");
        writer.WriteLine("示例：zimu translate https://example.com/video");
        writer.WriteLine("示例：zimu translate ./lesson.mp4");
        writer.WriteLine("默认英文翻译为中文，字幕以 SRT 文件交付，不烧录到视频。");
        writer.WriteLine("可选：--bilingual 生成英中双语 SRT；--burn 生成烧录字幕的视频。");
    }
}
