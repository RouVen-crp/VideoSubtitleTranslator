namespace VideoSubtitleTranslator;

/// <summary>
/// Resolves external programs without baking a machine-specific installation path into the app.
/// Environment variables take precedence over JSON defaults so local Windows installs can be used
/// directly while Docker can continue to use the programs installed in its image.
/// </summary>
public static class ExternalToolPaths
{
    public static string FfmpegFromEnvironment(string fallback = "ffmpeg") =>
        GetConfiguredPath("VST_FFMPEG_PATH", "FFMPEG_PATH") ?? fallback;

    public static string FfprobeFromEnvironment(string fallback = "ffprobe") =>
        GetConfiguredPath("VST_FFPROBE_PATH", "FFPROBE_PATH") ?? fallback;

    public static string YtDlpFromEnvironment(string fallback = "yt-dlp") =>
        GetConfiguredPath("VST_YTDLP_PATH", "YTDLP_PATH") ?? fallback;

    public static string ResolveSiblingFfprobe(string ffprobePath, string ffmpegPath)
    {
        if (IsExplicitPath(ffprobePath) || File.Exists(ffprobePath))
            return ffprobePath;

        if (!IsExplicitPath(ffmpegPath))
            return ffprobePath;

        var directory = Path.GetDirectoryName(ffmpegPath);
        if (string.IsNullOrWhiteSpace(directory))
            return ffprobePath;

        var sibling = Path.Combine(directory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        return File.Exists(sibling) ? sibling : ffprobePath;
    }

    public static bool IsExplicitPath(string value) =>
        Path.IsPathRooted(value) || value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar);

    private static string? GetConfiguredPath(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim().Trim('"');
        }

        return null;
    }
}
