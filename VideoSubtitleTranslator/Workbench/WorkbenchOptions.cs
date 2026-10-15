using VideoSubtitleTranslator;

namespace VideoSubtitleTranslator.Workbench;

public sealed class WorkbenchOptions
{
    public string DataDirectory { get; init; } = DefaultDataDirectory();
    public string BindAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5155;
    public long MaxUploadBytes { get; init; } = 500L * 1024 * 1024;
    public string FfmpegPath { get; init; } = ExternalToolPaths.FfmpegFromEnvironment();
    public string FfprobePath { get; init; } = ExternalToolPaths.FfprobeFromEnvironment();
    public string YtDlpPath { get; init; } = ExternalToolPaths.YtDlpFromEnvironment();

    public static WorkbenchOptions FromEnvironment()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("VST_WEB_PORT"), out var configuredPort)
            ? configuredPort
            : 5155;
        return new WorkbenchOptions
        {
            DataDirectory = Environment.GetEnvironmentVariable("VST_DATA_DIR")
                ?? DefaultDataDirectory(),
            BindAddress = Environment.GetEnvironmentVariable("VST_BIND_ADDRESS") ?? "127.0.0.1",
            Port = Math.Clamp(port, 1024, 65535),
            FfmpegPath = ExternalToolPaths.FfmpegFromEnvironment(),
            FfprobePath = ExternalToolPaths.FfprobeFromEnvironment(),
            YtDlpPath = ExternalToolPaths.YtDlpFromEnvironment()
        };
    }

    /// <summary>Uses a repository-local output directory unless the caller provides VST_DATA_DIR.</summary>
    public static string DefaultDataDirectory(string? startDirectory = null)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory()));
        while (true)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
                return Path.Combine(current.FullName, "outputs");
            if (current.Parent is null) return Path.Combine(Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory()), "outputs");
            current = current.Parent;
        }
    }
}
