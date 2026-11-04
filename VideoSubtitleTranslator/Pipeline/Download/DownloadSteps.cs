using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 下载视频及相关资源（步骤层）。
/// </summary>
[PipelineStep("Download", RequiredKeys = new[] { "Url", "WorkspacePath", "VideoExt", "VideoTitle", "VideoAuthor", "VideoUploadDate" })]
public abstract class DownloadStepBase : IPipelineStep
{
    public string Step => "Download";

    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

/// <summary>
/// 下载缩略图和视频，并写入 info.txt 的默认实现（暂时仍委托 MetaGetter/YoutubeDownloader，后续会内联）。
/// </summary>
[PipelineStep("Download", Implementation = "Default")]
public sealed class DefaultDownloadStep : DownloadStepBase
{
    private static ProcessStartInfo CreateYtDlpStartInfo(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GlobalRuntimeConfig.Current.Tools.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        var cookiesFile = Environment.GetEnvironmentVariable("COOKIES_FILE");
        if (!string.IsNullOrWhiteSpace(cookiesFile) && File.Exists(cookiesFile))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookiesFile);
        }
        else if (!string.IsNullOrWhiteSpace(GlobalRuntimeConfig.Current.CookiesFromBrowser))
        {
            startInfo.ArgumentList.Add("--cookies-from-browser");
            startInfo.ArgumentList.Add(GlobalRuntimeConfig.Current.CookiesFromBrowser);
        }

        return startInfo;
    }

    private static void DownloadThumbnail(string videoUrl, WorkDirs dirs)
    {
        if (File.Exists($"{dirs.ThumbnailPath}.png"))
        {
            Console.WriteLine("Thumbnail exists, skip download");
            return;
        }

        using var process = Process.Start(CreateYtDlpStartInfo([
            "--no-playlist", "--write-thumbnail", "--convert-thumbnails", "png", "--skip-download",
            "-o", dirs.ThumbnailPath, videoUrl]));
        if (process is null) return;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);
        if (process.ExitCode != 0)
            Console.Error.WriteLine($"缩略图下载失败（yt-dlp exit code {process.ExitCode}），继续下载视频。");
    }

    private static void DownloadVideo(string videoUrl, WorkDirs dirs)
    {
        if (File.Exists(dirs.VideoPath) && new FileInfo(dirs.VideoPath).Length > 0)
        {
            Console.WriteLine("Video exists, skip download");
            return;
        }
        if (File.Exists(dirs.VideoPath)) File.Delete(dirs.VideoPath);

        using var process = Process.Start(CreateYtDlpStartInfo([
            "--no-playlist", "--js-runtimes", "node", "--restrict-filenames", "-o", dirs.VideoPath, videoUrl]));
        if (process is null) throw new PipelineStageFailureException("无法启动 yt-dlp 视频下载进程。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);
        if (process.ExitCode != 0)
            throw new PipelineStageFailureException($"yt-dlp 视频下载失败（exit code {process.ExitCode}）。请检查链接、网络和 yt-dlp 配置。");
        if (!File.Exists(dirs.VideoPath) || new FileInfo(dirs.VideoPath).Length == 0)
            throw new PipelineStageFailureException("yt-dlp 未生成预期的视频文件；请检查链接和下载器输出。");
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var url = context["Url"];
        var workspacePath = context["WorkspacePath"];
        var ext = context["VideoExt"];
        var title = context["VideoTitle"];
        var author = context["VideoAuthor"];
        var uploadDate = context["VideoUploadDate"];

        var dirs = new WorkDirs(workspacePath, ext);

        DownloadThumbnail(url, dirs);
        DownloadVideo(url, dirs);
        File.WriteAllText(dirs.InfoPath, $"原标题: {title}\n作者: {author}\n原始URL: {url}\n上传日期: {uploadDate}");

        return context;
    }
}

