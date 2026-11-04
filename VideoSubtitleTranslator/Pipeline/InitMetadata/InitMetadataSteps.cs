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
    private static ProcessStartInfo CreateStartInfo(string url, bool includeCookies)
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

        if (includeCookies)
        {
            foreach (var argument in YtDlpCookieArguments.Build().Arguments)
                startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static (string title, string ext, string author, string uploadDate) GetVideoMetadata(string url)
    {
        // 浏览器 cookies 只是可选鉴权：浏览器运行时 yt-dlp 复制不了 cookie 数据库，
        // 带 cookies 会直接失败，这时必须去掉 cookies 再试，而不是让任务失败。
        // 元数据查询还会走网络，偶发的代理/限流抖动也不应该让整条流水线失败。
        var usedCookies = YtDlpCookieArguments.Build().UsedCookies;
        var includeCookieVariants = usedCookies ? new[] { true, false } : new[] { false };
        const int attemptsPerVariant = 2;

        foreach (var includeCookies in includeCookieVariants)
        {
            for (var attempt = 1; attempt <= attemptsPerVariant; attempt++)
            {
                using var process = new Process { StartInfo = CreateStartInfo(url, includeCookies) };
                process.Start();

                // 同时读两个流，避免任一管道写满时互相阻塞。
                var jsonTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                Task.WaitAll(jsonTask, errorTask);

                if (!string.IsNullOrEmpty(errorTask.Result))
                    Console.Error.WriteLine("yt-dlp metadata lookup returned diagnostic output.");

                if (process.ExitCode == 0 && TryParseMetadata(jsonTask.Result, out var metadata))
                {
                    return metadata;
                }

                Console.Error.WriteLine(
                    $"yt-dlp 获取视频信息失败（第 {attempt}/{attemptsPerVariant} 次，cookies={includeCookies}，exit code {process.ExitCode}）。");
                if (attempt < attemptsPerVariant)
                    Thread.Sleep(TimeSpan.FromSeconds(attempt));
            }
        }

        throw new PipelineStageFailureException("yt-dlp 未能返回有效的视频信息；请确认链接可访问，并检查网络与 yt-dlp 配置。");
    }

    private static bool TryParseMetadata(
        string jsonOutput,
        out (string title, string ext, string author, string uploadDate) metadata)
    {
        metadata = (string.Empty, string.Empty, string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(jsonOutput))
        {
            return false;
        }

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

            metadata = (title, ext, author, uploadDate);
            return true;
        }
        catch (JsonException)
        {
            return false;
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
