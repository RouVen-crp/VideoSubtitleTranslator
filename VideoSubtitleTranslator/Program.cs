using System;
using System.IO;
using VideoSubtitleTranslator.Workbench;
using VideoSubtitleTranslator.Cli;

namespace VideoSubtitleTranslator;

internal class Program
{
    internal static void LoadEnv(string? baseDir = null)
    {
        baseDir ??= AppContext.BaseDirectory;
        var searchPaths = new[]
        {
            Path.Combine(baseDir, ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(baseDir, "..", "..", "..", ".env")
        };
        var envPath = searchPaths.FirstOrDefault(File.Exists);
        if (envPath is null)
        {
            return;
        }
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var eq = trimmed.IndexOf('=');
            if (eq <= 0 || eq >= trimmed.Length - 1) continue;
            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];
            if (key.Length > 0 && Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    internal static (string pipelineConfig, string translatorConfig) ResolveConfigPaths(string mode, string baseDir)
    {
        if (mode == "meme")
        {
            return (
                Path.Combine(baseDir, "pipeline_meme.config.json"),
                Path.Combine(baseDir, "Config", "translator_meme.config.json")
            );
        }
        return (
            Path.Combine(baseDir, "pipeline.config.json"),
            Path.Combine(baseDir, "Config", "translator.config.json")
        );
    }

    internal static string ResolveDefaultWorkspace(string currentDirectory)
    {
        var projectDirectory = File.Exists(Path.Combine(currentDirectory, "VideoSubtitleTranslator.csproj"))
            ? currentDirectory
            : Path.Combine(currentDirectory, "VideoSubtitleTranslator");

        return Path.GetFullPath(Path.Combine(projectDirectory, "output"));
    }

    private static async Task<int> Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--web", StringComparison.OrdinalIgnoreCase)))
        {
            LoadEnv();
            await WorkbenchHost.RunAsync(args);
            return 0;
        }

        return await CliApplication.RunAsync(
            args,
            new PipelineTranslationWorkflow(),
            Console.Out,
            Console.Error,
            isInteractive: !Console.IsInputRedirected && !Console.IsOutputRedirected,
            currentDirectory: Directory.GetCurrentDirectory(),
            standardInput: Console.In,
            terminal: new SystemCliTerminal());
    }
}
