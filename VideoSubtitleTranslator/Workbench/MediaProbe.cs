using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace VideoSubtitleTranslator.Workbench;

public sealed record MediaProbeResult(bool IsValid, string? Error, double DurationSeconds, string? VideoCodec, string? AudioCodec, int AudioChannels);

public sealed class MediaProbe
{
    public const long MaxBytes = 500L * 1024 * 1024;
    public const double MaxDurationSeconds = 10 * 60;

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
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in new[] { "-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,channels", "-of", "json", path })
                process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) return Invalid("无法读取媒体信息；文件可能损坏或不是可播放的 MP4。");

            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var duration = root.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var durationElement) && double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDuration)
                ? parsedDuration
                : 0;
            var streams = root.TryGetProperty("streams", out var streamElements) ? streamElements.EnumerateArray().ToList() : new List<JsonElement>();
            var video = streams.FirstOrDefault(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "video");
            var audio = streams.FirstOrDefault(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "audio");
            var videoCodec = video.ValueKind == JsonValueKind.Undefined ? null : video.TryGetProperty("codec_name", out var vc) ? vc.GetString() : null;
            var audioCodec = audio.ValueKind == JsonValueKind.Undefined ? null : audio.TryGetProperty("codec_name", out var ac) ? ac.GetString() : null;
            var channels = audio.ValueKind == JsonValueKind.Undefined || !audio.TryGetProperty("channels", out var channelElement) ? 0 : channelElement.GetInt32();
            if (duration <= 0) return Invalid("媒体时长无法读取。");
            if (duration > MaxDurationSeconds) return Invalid("视频时长超过 10 分钟限制。");
            if (!string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase)) return Invalid("首版仅接受 H.264 视频编码。");
            if (!string.Equals(audioCodec, "aac", StringComparison.OrdinalIgnoreCase) || channels <= 0) return Invalid("首版需要包含可播放 AAC 音频；静音视频不受支持。");
            return new MediaProbeResult(true, null, duration, videoCodec, audioCodec, channels);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or JsonException or InvalidOperationException)
        {
            return Invalid("媒体检查失败：请确认 ffprobe 已安装并且文件可读取。");
        }
    }

    private static MediaProbeResult Invalid(string error) => new(false, error, 0, null, null, 0);
}
