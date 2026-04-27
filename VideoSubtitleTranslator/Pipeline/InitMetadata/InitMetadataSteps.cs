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
            FileName = "yt-dlp",
            Arguments = $"--js-runtimes node --dump-json \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = new Process();
        process.StartInfo = startInfo;
        process.Start();

        var jsonOutput = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine($"Error from yt-dlp: {error}");
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

            return (title, ext, author, uploadDate);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse yt-dlp JSON output: {ex.Message}");
            return ("", "", "", "");
        }
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var url = context["Url"];
        var (title, ext, author, uploadDate) = GetVideoMetadata(url);
        if (string.IsNullOrEmpty(title))
        {
            throw new InvalidOperationException("No title found");
        }

        Console.WriteLine($"Title: {title}, Ext: {ext}, Author: {author}, UploadDate: {uploadDate}");
        context["VideoTitle"] = title;
        context["VideoExt"] = ext;
        context["VideoAuthor"] = author;
        context["VideoUploadDate"] = uploadDate;
        return context;
    }
}

