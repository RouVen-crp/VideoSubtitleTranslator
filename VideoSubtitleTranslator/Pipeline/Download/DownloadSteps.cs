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
    private static void DownloadThumbnail(string videoUrl, WorkDirs dirs)
    {
        if (File.Exists($"{dirs.ThumbnailPath}.png"))
        {
            Console.WriteLine("Thumbnail exists, skip download");
            return;
        }

        using var process = Process.Start("yt-dlp",
            $"--write-thumbnail --convert-thumbnails png --skip-download -o \"{dirs.ThumbnailPath}\" \"{videoUrl}\"");
        process?.WaitForExit();
    }

    private static void DownloadVideo(string videoUrl, WorkDirs dirs)
    {
        if (File.Exists(dirs.VideoPath))
        {
            Console.WriteLine("Video exists, skip download");
            return;
        }

        using var process = Process.Start("yt-dlp",
            $"--js-runtimes node --restrict-filenames -o \"{dirs.VideoPath}\" \"{videoUrl}\"");
        process?.WaitForExit();
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

