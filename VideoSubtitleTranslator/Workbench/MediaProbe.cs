using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoSubtitleTranslator;

namespace VideoSubtitleTranslator.Workbench;

public sealed record MediaProbeResult(bool IsValid, string? Error, double DurationSeconds, string? VideoCodec, string? AudioCodec, int AudioChannels);

public sealed class MediaProbe
{
    public const long MaxBytes = 500L * 1024 * 1024;
    public const double MaxDurationSeconds = 10 * 60;

    private readonly string _ffprobePath;
    private readonly string _ffmpegPath;

    public MediaProbe(string ffprobePath = "ffprobe", string ffmpegPath = "ffmpeg")
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ExternalToolPaths.ResolveSiblingFfprobe(ffprobePath, ffmpegPath);
    }

    public async Task<MediaProbeResult> ValidateAsync(string path, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase))
            return Invalid("只接受 MP4 文件。");
        var info = new FileInfo(path);
        if (!info.Exists) return Invalid("上传文件不存在。");
        if (info.Length > MaxBytes) return Invalid("文件超过 500 MiB 大小限制。");

        try
        {
            return await ValidateWithFfprobeAsync(path, cancellationToken);
        }
        catch (Win32Exception)
        {
            // Some Windows distributions ship only ffmpeg.exe. The fallback keeps the Web
            // workbench usable with that installation while ffprobe remains the preferred path.
            try
            {
                return await ValidateWithFfmpegAsync(path, cancellationToken);
            }
            catch (Win32Exception)
            {
                return Invalid("媒体检查失败：请确认 ffprobe 或 ffmpeg 已安装并且文件可读取。");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (JsonException) { return Invalid("无法读取媒体信息；文件可能损坏或不是可播放的 MP4。"); }
        catch (InvalidOperationException) { return Invalid("无法读取媒体信息；文件可能损坏或不是可播放的 MP4。"); }
    }

    private async Task<MediaProbeResult> ValidateWithFfprobeAsync(string path, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "-v", "error", "-show_entries", "format=format_name,duration:format_tags=major_brand:stream=codec_type,codec_name,channels", "-of", "json", path })
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) return Invalid("无法读取媒体信息；文件可能损坏或不是可播放的 MP4。");

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var formatInfo = root.TryGetProperty("format", out var format) ? format : default;
        var formatNames = formatInfo.ValueKind != JsonValueKind.Undefined && formatInfo.TryGetProperty("format_name", out var formatNameElement)
            ? formatNameElement.GetString()?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []
            : [];
        var majorBrand = formatInfo.ValueKind != JsonValueKind.Undefined && formatInfo.TryGetProperty("tags", out var tags) && tags.TryGetProperty("major_brand", out var brandElement)
            ? brandElement.GetString()?.Trim()
            : null;
        var duration = formatInfo.ValueKind != JsonValueKind.Undefined && formatInfo.TryGetProperty("duration", out var durationElement) && double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDuration)
            ? parsedDuration
            : 0;
        var streams = root.TryGetProperty("streams", out var streamElements) ? streamElements.EnumerateArray().ToList() : new List<JsonElement>();
        var video = streams.FirstOrDefault(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "video");
        var audio = streams.FirstOrDefault(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "audio");
        var videoCodec = video.ValueKind == JsonValueKind.Undefined ? null : video.TryGetProperty("codec_name", out var vc) ? vc.GetString() : null;
        var audioCodec = audio.ValueKind == JsonValueKind.Undefined ? null : audio.TryGetProperty("codec_name", out var ac) ? ac.GetString() : null;
        var channels = audio.ValueKind == JsonValueKind.Undefined || !audio.TryGetProperty("channels", out var channelElement) ? 0 : channelElement.GetInt32();
        return ValidateMetadata(formatNames.Contains("mp4", StringComparer.OrdinalIgnoreCase), majorBrand, duration, videoCodec, audioCodec, channels);
    }

    private async Task<MediaProbeResult> ValidateWithFfmpegAsync(string path, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "-hide_banner", "-i", path, "-map", "0:v:0", "-map", "0:a:0", "-f", "null", OperatingSystem.IsWindows() ? "NUL" : "/dev/null" })
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var diagnostics = (await errorTask) + Environment.NewLine + await outputTask;
        if (process.ExitCode != 0) return Invalid("无法读取媒体信息；文件可能损坏或不是可播放的 MP4。");

        var durationMatch = Regex.Match(diagnostics, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        var duration = durationMatch.Success &&
                       double.TryParse(durationMatch.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? int.Parse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
              + int.Parse(durationMatch.Groups[2].Value, CultureInfo.InvariantCulture) * 60
              + seconds
            : 0;
        var brandMatch = Regex.Match(diagnostics, @"major_brand\s*:\s*([^\s\r\n]+)", RegexOptions.IgnoreCase);
        var majorBrand = brandMatch.Success ? brandMatch.Groups[1].Value.Trim() : null;
        var videoMatch = Regex.Match(diagnostics, @"Video:\s*([a-z0-9]+)", RegexOptions.IgnoreCase);
        var audioMatch = Regex.Match(diagnostics, @"Audio:\s*([a-z0-9]+)", RegexOptions.IgnoreCase);
        var channelMatch = Regex.Match(diagnostics, @"(?:stereo|mono|\d+(?:\.\d+)?\s*channels?)", RegexOptions.IgnoreCase);
        var channels = !audioMatch.Success ? 0 : channelMatch.Success && channelMatch.Value.Contains("mono", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        var isMp4 = majorBrand is not null || Regex.IsMatch(diagnostics, @"Input #\d+,\s*[^\r\n]*\bmp4\b", RegexOptions.IgnoreCase);
        return ValidateMetadata(isMp4, majorBrand, duration, videoMatch.Success ? videoMatch.Groups[1].Value : null, audioMatch.Success ? audioMatch.Groups[1].Value : null, channels);
    }

    private static MediaProbeResult ValidateMetadata(bool isMp4, string? majorBrand, double duration, string? videoCodec, string? audioCodec, int channels)
    {
        var mp4Brands = new[] { "isom", "iso2", "iso3", "iso4", "iso5", "iso6", "mp41", "mp42", "avc1", "M4V" };
        if (!isMp4 || (majorBrand is not null && !mp4Brands.Contains(majorBrand, StringComparer.OrdinalIgnoreCase)))
            return Invalid("文件容器不是 MP4。请上传原始 MP4 文件。");
        if (duration <= 0) return Invalid("媒体时长无法读取。");
        if (duration > MaxDurationSeconds) return Invalid("视频时长超过 10 分钟限制。");
        if (!string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase)) return Invalid("首版仅接受 H.264 视频编码。");
        if (!string.Equals(audioCodec, "aac", StringComparison.OrdinalIgnoreCase) || channels <= 0) return Invalid("首版需要包含可播放 AAC 音频；静音视频不受支持。");
        return new MediaProbeResult(true, null, duration, videoCodec, audioCodec, channels);
    }

    private static MediaProbeResult Invalid(string error) => new(false, error, 0, null, null, 0);
}
