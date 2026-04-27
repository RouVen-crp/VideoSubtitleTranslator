using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 字幕烧录步骤（步骤层）。
/// </summary>
[PipelineStep("BurnSubtitle", RequiredKeys = new[] { "WorkspacePath", "VideoExt" })]
public abstract class BurnSubtitleStepBase : IPipelineStep
{
    public string Step => "BurnSubtitle";

    public abstract Dictionary<string, string> Execute(Dictionary<string, string> context);
}

/// <summary>
/// 调用 ffmpeg 将中文字幕烧录到视频中的默认实现（暂时仍委托 SubtitleBurner，后续会内联）。
/// </summary>
[PipelineStep("BurnSubtitle", Implementation = "Default")]
public sealed class DefaultBurnSubtitleStep : BurnSubtitleStepBase
{
    private static long? RunFfprobeBitrate(string videoPath, string showEntriesArgs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error {showEntriesArgs} -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0) return null;
            return long.TryParse(output, out var bps) && bps > 0 ? bps : null;
        }
        catch
        {
            return null;
        }
    }

    private static long? TryProbeVideoBitrateBps(string videoPath)
    {
        // 优先视频流码率；若容器/编码未提供流级码率，再回退容器总码率。
        return RunFfprobeBitrate(videoPath, "-select_streams v:0 -show_entries stream=bit_rate")
               ?? RunFfprobeBitrate(videoPath, "-show_entries format=bit_rate");
    }

    private static string BuildFfmpegArgs(
        WorkDirs dirs,
        string outputPath,
        string encoder,
        string qualityPreset,
        int? targetBitrateKbps)
    {
        var bitrateArgs = targetBitrateKbps.HasValue
            ? $" -b:v {targetBitrateKbps.Value}k -maxrate {targetBitrateKbps.Value}k -bufsize {targetBitrateKbps.Value * 2}k"
            : string.Empty;

        // auto: 按 ffmpeg 默认编码策略，不显式指定 c:v。
        if (!string.Equals(qualityPreset?.Trim(), "high", StringComparison.OrdinalIgnoreCase) && !targetBitrateKbps.HasValue)
            return $"-i \"{dirs.VideoPath}\" -vf subtitles='{dirs.TranslatedSubtitlePath}' -c:a copy \"{outputPath}\"";

        // high 或者启用了最低码率保护：显式视频编码参数。
        return
            $"-i \"{dirs.VideoPath}\" -vf subtitles='{dirs.TranslatedSubtitlePath}' -c:v {encoder}{bitrateArgs} -c:a copy \"{outputPath}\"";
    }

    private static void Burn(WorkDirs dirs)
    {
        if (!GlobalRuntimeConfig.Current.Burn.Enabled)
        {
            Console.WriteLine("Burn disabled by config, skip burn");
            return;
        }

        var translatedTitle = File.ReadAllText(dirs.TranslatedTitlePath);
        var finalVideoPath = dirs.CustomPath($"{translatedTitle}.mp4");
        if (File.Exists(finalVideoPath))
        {
            Console.WriteLine($"{translatedTitle}.mp4 exists, skip burn");
            return;
        }

        var burnConfig = GlobalRuntimeConfig.Current.Burn;
        var qualityPreset = string.IsNullOrWhiteSpace(burnConfig.QualityPreset) ? "auto" : burnConfig.QualityPreset.Trim();
        int? targetBitrateKbps = null;
        if (burnConfig.EnforceSourceBitrateFloor)
        {
            var sourceBitrateBps = TryProbeVideoBitrateBps(dirs.VideoPath);
            if (sourceBitrateBps.HasValue)
            {
                var sourceBitrateKbps = (int)Math.Ceiling(sourceBitrateBps.Value / 1000.0);
                var configuredFloor = Math.Max(0, burnConfig.MinOutputBitrateKbps);
                targetBitrateKbps = Math.Max(sourceBitrateKbps, configuredFloor);
                Console.WriteLine(
                    $"[burn] sourceBitrateKbps={sourceBitrateKbps}, configuredFloorKbps={configuredFloor}, targetBitrateKbps={targetBitrateKbps}");
            }
            else
            {
                Console.WriteLine("[burn] 无法读取原视频码率，已回退为默认烧录码率策略。");
            }
        }

        var ffmpegArgs = BuildFfmpegArgs(dirs, finalVideoPath, burnConfig.VideoEncoder, qualityPreset, targetBitrateKbps);
        Console.WriteLine(
            $"[burn] qualityPreset={qualityPreset}, encoder={burnConfig.VideoEncoder}, bitrateFloorEnabled={burnConfig.EnforceSourceBitrateFloor}");

        using var process = Process.Start("ffmpeg", ffmpegArgs);
        process?.WaitForExit();
    }

    public override Dictionary<string, string> Execute(Dictionary<string, string> context)
    {
        var workspacePath = context["WorkspacePath"];
        var ext = context["VideoExt"];
        var dirs = new WorkDirs(workspacePath, ext);

        Burn(dirs);
        return context;
    }
}

