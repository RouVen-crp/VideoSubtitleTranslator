using VideoSubtitleTranslator.Cli;

namespace VideoSubtitleTranslator.Tests;

public sealed class CliDependencyDiagnosticsTests
{
    [Fact]
    public void Download_check_only_requires_configuration_and_yt_dlp()
    {
        using var temp = new TemporaryDirectory();
        WriteConfiguration(temp.Path);
        var diagnostics = new CliDependencyDiagnostics(
            temp.Path,
            getEnvironmentVariable: _ => null,
            commandExists: name => name == "yt-dlp",
            pythonPackagesAvailable: _ => throw new InvalidOperationException("Python must not be checked for download."));

        var checks = diagnostics.Check("download");

        Assert.All(checks, check => Assert.True(check.IsAvailable));
        Assert.DoesNotContain(checks, check => check.Message.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(checks, check => check.Message.Contains("Whisper", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(checks, check => check.Message.Contains("DEEPSEEK_API_KEY", StringComparison.Ordinal));
    }

    [Fact]
    public void Translate_check_reports_missing_tools_packages_and_key_variable_without_values()
    {
        using var temp = new TemporaryDirectory();
        WriteConfiguration(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "pipeline.config.json"), "{}");
        var diagnostics = new CliDependencyDiagnostics(
            temp.Path,
            getEnvironmentVariable: name => name == "VST_PYTHON_PATH" ? "python3" : null,
            commandExists: name => name == "python3",
            pythonPackagesAvailable: _ => false);

        var checks = diagnostics.Check("translate");
        var output = string.Join('\n', checks.Select(check => check.Message));

        Assert.Contains(checks, check => !check.IsAvailable && check.Message.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(checks, check => !check.IsAvailable && check.Message.Contains("Whisper", StringComparison.Ordinal));
        Assert.Contains("DEEPSEEK_API_KEY", output);
        Assert.DoesNotContain("configured", output);
    }

    [Fact]
    public void Download_check_does_not_report_missing_translation_config_as_missing_translation_dependencies()
    {
        using var temp = new TemporaryDirectory();
        WriteConfiguration(temp.Path);
        var diagnostics = new CliDependencyDiagnostics(
            temp.Path,
            getEnvironmentVariable: _ => null,
            commandExists: _ => false,
            pythonPackagesAvailable: _ => false);

        var checks = diagnostics.Check("download");

        Assert.Equal(2, checks.Count);
        Assert.Contains(checks, check => check.IsAvailable && check.Message.Contains("翻译配置可读取", StringComparison.Ordinal));
        Assert.Contains(checks, check => !check.IsAvailable && check.Message.Contains("yt-dlp", StringComparison.Ordinal));
        Assert.DoesNotContain(checks, check => check.Message.Contains("流水线配置", StringComparison.Ordinal));
    }

    private static void WriteConfiguration(string baseDirectory)
    {
        var configDirectory = Path.Combine(baseDirectory, "Config");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "translator.config.json"), """
            { "tools": { "ytDlpPath": "yt-dlp", "ffmpegPath": "ffmpeg", "ffprobePath": "ffprobe" }, "whisper": { "pythonExecutable": "python3" }, "llm": { "apiKeyEnv": "DEEPSEEK_API_KEY" } }
            """);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"zimu-diagnostics-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
