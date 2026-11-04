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
    private static ProcessStartInfo CreateYtDlpStartInfo(IEnumerable<string> arguments, bool includeCookies)
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

        if (includeCookies)
        {
            foreach (var argument in YtDlpCookieArguments.Build().Arguments)
                startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static int RunYtDlp(IEnumerable<string> arguments, bool includeCookies)
    {
        using var process = Process.Start(CreateYtDlpStartInfo(arguments, includeCookies));
        if (process is null) throw new PipelineStageFailureException("无法启动 yt-dlp 进程。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);
        return process.ExitCode;
    }

    private static void DownloadThumbnail(string videoUrl, WorkDirs dirs)
    {
        if (File.Exists($"{dirs.ThumbnailPath}.png"))
        {
            Console.WriteLine("Thumbnail exists, skip download");
            return;
        }

        var exitCode = RunYtDlp(
            ["--no-playlist", "--write-thumbnail", "--convert-thumbnails", "png", "--skip-download",
             "-o", dirs.ThumbnailPath, videoUrl],
            includeCookies: true);
        if (exitCode != 0)
            Console.Error.WriteLine($"缩略图下载失败（yt-dlp exit code {exitCode}），继续下载视频。");
    }

    private static void DownloadVideo(string videoUrl, WorkDirs dirs)
    {
        if (HasUsableVideo(dirs))
        {
            Console.WriteLine("Video exists, skip download");
            return;
        }
        if (File.Exists(dirs.VideoPath)) File.Delete(dirs.VideoPath);

        string[] videoArguments =
            ["--no-playlist", "--js-runtimes", "node", "--restrict-filenames", "-o", dirs.VideoPath, videoUrl];

        // 浏览器 cookies 是可选鉴权：浏览器运行时 yt-dlp 复制不了 cookie 数据库，
        // 会直接失败；这种情况下去掉 cookies 再试一次。
        var usedCookies = YtDlpCookieArguments.Build().UsedCookies;
        var exitCode = RunYtDlp(videoArguments, includeCookies: usedCookies);

        if ((exitCode != 0 || !HasUsableVideo(dirs)) && usedCookies)
        {
            Console.Error.WriteLine("yt-dlp 带 cookies 下载失败，去掉 cookies 重试一次。");
            exitCode = RunYtDlp(videoArguments, includeCookies: false);
        }

        if (exitCode != 0)
            throw new PipelineStageFailureException($"yt-dlp 视频下载失败（exit code {exitCode}）。请检查链接、网络和 yt-dlp 配置。");
        if (!HasUsableVideo(dirs))
            throw new PipelineStageFailureException("yt-dlp 未生成预期的视频文件；请检查链接和下载器输出。");
    }

    private static bool HasUsableVideo(WorkDirs dirs) =>
        File.Exists(dirs.VideoPath) && new FileInfo(dirs.VideoPath).Length > 0;

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
