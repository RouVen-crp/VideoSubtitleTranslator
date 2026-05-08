using System;
using System.Collections.Generic;
using System.IO;
using VideoSubtitleTranslator.Pipeline;

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
            Console.WriteLine($"[env] not found, searched: {string.Join(", ", searchPaths)}");
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
        Console.WriteLine($"[env] loaded {envPath}");
    }
    internal static (string mode, List<string> positional) ParseArgs(string[] args)
    {
        var mode = "standard";
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--mode" && i + 1 < args.Length)
            {
                mode = args[i + 1].ToLowerInvariant();
                i++;
            }
            else
            {
                positional.Add(args[i]);
            }
        }
        return (mode, positional);
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

    private static void Main(string[] args)
    {
        LoadEnv();
        var (mode, positionalArgs) = ParseArgs(args);

        if (positionalArgs.Count < 2)
        {
            Console.WriteLine("用法: VideoSubtitleTranslator <url> <workspace_dir> [--mode <standard|meme>] [pipeline_config_json_path] [translator_config_json_path]");
            return;
        }

        var url = positionalArgs[0];
        var workspaceDir = positionalArgs[1];

        var (defaultPipelinePath, defaultTranslatorPath) = ResolveConfigPaths(mode, AppContext.BaseDirectory);
        var configPath = positionalArgs.Count >= 3
            ? positionalArgs[2]
            : defaultPipelinePath;
        var translatorConfigPath = positionalArgs.Count >= 4
            ? positionalArgs[3]
            : defaultTranslatorPath;

        var translatorConfig = TranslatorRuntimeConfig.Load(translatorConfigPath);
        translatorConfig.Mode = mode;

        var initialContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Url"] = url,
            ["WorkspaceRoot"] = workspaceDir,
            ["ConfigPath"] = configPath,
            ["TranslatorConfigPath"] = translatorConfigPath,
            ["Mode"] = mode
        };

        try
        {
            var config = PipelineRunner.LoadConfig(configPath);
            var registry = new PipelineRegistry();
            var runner = new PipelineRunner(registry);

            runner.Run(config, initialContext, translatorConfig);

            Console.WriteLine("Done");
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine($"未找到文件: {ex.FileName ?? "unknown"}");
            Console.WriteLine(ex.Message);
        }
        catch (OperationCanceledException ex)
        {
            Console.WriteLine($"流程被取消：{ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"执行过程中发生错误: {ex}");
        }
    }
}