using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 初始化视频元数据（步骤层）。
/// </summary>
[PipelineStep("InitMetadata", RequiredKeys = new[] { "Url" })]
public abstract class InitMetadataStepBase : IPipelineStep
{
    public string Step => "InitMetadata";

    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

/// <summary>
/// 使用 yt-dlp 获取视频元数据的默认实现（暂时仍委托 MetaGetter，后续会内联）。
/// 输出：VideoTitle, VideoExt, VideoAuthor, VideoUploadDate
/// </summary>
[PipelineStep("InitMetadata", Implementation = "MetaGetter")]
public sealed class MetaGetterInitMetadataStep : InitMetadataStepBase
{
    private static (string title, string ext, string author, string uploadDate) GetVideoMetadata(string url)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GlobalRuntimeConfig.Current.Tools.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "--no-playlist", "--js-runtimes", "node", "--dump-json", url })
            startInfo.ArgumentList.Add(argument);
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

        using var process = new Process();
        process.StartInfo = startInfo;
        process.Start();

        var jsonTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(jsonTask, errorTask);
        var jsonOutput = jsonTask.Result;

        if (process.ExitCode != 0)
            throw new PipelineStageFailureException($"yt-dlp 获取视频信息失败（exit code {process.ExitCode}）。请检查链接、网络和 yt-dlp 配置。");

        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonOutput);
            var root = jsonDoc.RootElement;

            var title = root.TryGetProperty("title", out var titleProp)
                ? titleProp.GetString() ?? ""
                : "";

            var ext = root.TryGetProperty("ext", out var extProp)
                ? "." + (extProp.GetString() ?? "")
                : "";

            var author = root.TryGetProperty("uploader", out var uploaderProp)
                ? uploaderProp.GetString() ?? ""
                : root.TryGetProperty("channel", out var channelProp)
                    ? channelProp.GetString() ?? ""
                    : "";

            var uploadDate = root.TryGetProperty("upload_date", out var uploadDataProp)
                ? uploadDataProp.GetString() ?? ""
                : "";

            return (title, ext, author, uploadDate);
        }
        catch (JsonException)
        {
            throw new PipelineStageFailureException("yt-dlp 未返回有效的视频信息；请更新 yt-dlp 并检查链接。");
        }
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var url = context["Url"];
        var (title, ext, author, uploadDate) = GetVideoMetadata(url);
        if (string.IsNullOrEmpty(title))
        {
            throw new PipelineStageFailureException("yt-dlp 返回的视频信息缺少标题；请检查链接和 yt-dlp 版本。");
        }

        Console.WriteLine("Video metadata loaded.");
        context["VideoTitle"] = title;
        context["VideoExt"] = ext;
        context["VideoAuthor"] = author;
        context["VideoUploadDate"] = uploadDate;
        return context;
    }
}
