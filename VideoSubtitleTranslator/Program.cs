using System;
using System.Collections.Generic;
using System.IO;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator;

internal class Program
{
    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: VideoSubtitleTranslator <url> <workspace_dir> [pipeline_config_json_path] [translator_config_json_path]");
            return;
        }

        var url = args[0];
        var workspaceDir = args[1];

        var configPath = args.Length >= 3
            ? args[2]
            : Path.Combine(AppContext.BaseDirectory, "pipeline.config.json");
        var translatorConfigPath = args.Length >= 4
            ? args[3]
            : Path.Combine(AppContext.BaseDirectory, "Config", "translator.config.json");

        var translatorConfig = TranslatorRuntimeConfig.Load(translatorConfigPath);

        var initialContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Url"] = url,
            ["WorkspaceRoot"] = workspaceDir,
            ["ConfigPath"] = configPath,
            ["TranslatorConfigPath"] = translatorConfigPath
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