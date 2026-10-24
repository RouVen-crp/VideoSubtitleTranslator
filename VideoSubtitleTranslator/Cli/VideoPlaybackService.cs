using System.Diagnostics;
using System.ComponentModel;

namespace VideoSubtitleTranslator.Cli;

public sealed record VideoPlaybackResult(string VideoPath, string SubtitlePath, bool UsedCompatiblePlayer);

public sealed class VideoPlaybackException(string message) : Exception(message);

public sealed class VideoPlaybackService
{
    private readonly Func<string?> _findCompatiblePlayer;
    private readonly Action<string, IReadOnlyList<string>> _startPlayer;
    private readonly Action<string> _openDefault;

    public VideoPlaybackService(
        Func<string?>? findCompatiblePlayer = null,
        Action<string, IReadOnlyList<string>>? startPlayer = null,
        Action<string>? openDefault = null)
    {
        _findCompatiblePlayer = findCompatiblePlayer ?? FindCompatiblePlayer;
        _startPlayer = startPlayer ?? StartProcess;
        _openDefault = openDefault ?? OpenDefault;
    }

    public VideoPlaybackResult Play(string videoPath, string subtitlePath)
    {
        if (!File.Exists(videoPath)) throw new FileNotFoundException("找不到要播放的视频文件。", videoPath);
        if (!File.Exists(subtitlePath)) throw new FileNotFoundException("找不到中文字幕文件。", subtitlePath);

        var absoluteVideoPath = Path.GetFullPath(videoPath);
        var absoluteSubtitlePath = Path.GetFullPath(subtitlePath);
        var player = _findCompatiblePlayer();
        if (player is not null)
        {
            try
            {
                var subtitleArgument = Path.GetFileNameWithoutExtension(player).Equals("vlc", StringComparison.OrdinalIgnoreCase)
                    ? $"--sub-url={new Uri(absoluteSubtitlePath).AbsoluteUri}"
                    : $"--sub-file={absoluteSubtitlePath}";
                _startPlayer(player, [subtitleArgument, absoluteVideoPath]);
                return new VideoPlaybackResult(absoluteVideoPath, absoluteSubtitlePath, UsedCompatiblePlayer: true);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
            {
                // A stale or unusable player installation should not prevent default-player playback.
            }
        }

        try
        {
            _openDefault(absoluteVideoPath);
            return new VideoPlaybackResult(absoluteVideoPath, absoluteSubtitlePath, UsedCompatiblePlayer: false);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            throw new VideoPlaybackException("系统默认播放器无法打开视频。请为该视频格式设置默认播放器后重试；中文字幕文件位于：" + absoluteSubtitlePath);
        }
    }

    private static string? FindCompatiblePlayer()
    {
        foreach (var candidate in PlayerCandidates())
        {
            var resolved = ResolveExecutable(candidate);
            if (resolved is not null) return resolved;
        }
        return null;
    }

    private static IEnumerable<string> PlayerCandidates()
    {
        yield return OperatingSystem.IsWindows() ? "mpv.exe" : "mpv";
        yield return OperatingSystem.IsWindows() ? "vlc.exe" : "vlc";
        if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/VLC.app/Contents/MacOS/VLC";
            yield return "/Applications/mpv.app/Contents/MacOS/mpv";
        }
        else if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "mpv", "mpv.exe");
        }
    }

    private static string? ResolveExecutable(string candidate)
    {
        if (Path.IsPathRooted(candidate)) return File.Exists(candidate) ? candidate : null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fullPath = Path.Combine(directory, candidate);
            if (File.Exists(fullPath)) return fullPath;
        }
        return null;
    }

    private static void StartProcess(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo { FileName = executable, UseShellExecute = false };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        Process.Start(startInfo);
    }

    private static void OpenDefault(string videoPath)
    {
        var startInfo = OperatingSystem.IsMacOS()
            ? new ProcessStartInfo { FileName = "open", UseShellExecute = false }
            : OperatingSystem.IsWindows()
                ? new ProcessStartInfo { FileName = videoPath, UseShellExecute = true }
                : new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false };
        if (!OperatingSystem.IsWindows()) startInfo.ArgumentList.Add(videoPath);
        Process.Start(startInfo);
    }
}
