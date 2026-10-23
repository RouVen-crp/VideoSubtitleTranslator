using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Cli;

public sealed class PipelineTranslationWorkflow : ITranslationWorkflow
{
    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Program.LoadEnv();

        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths(request.Mode, AppContext.BaseDirectory);
        var translatorConfig = TranslatorRuntimeConfig.Load(translatorPath);
        translatorConfig.Mode = request.Mode;
        // Keep the CLI's fast SRT path independent of the legacy Web workbench defaults.
        translatorConfig.Burn.Enabled = false;

        var initialContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Url"] = request.Input,
            ["WorkspaceRoot"] = request.WorkspaceRoot,
            ["ConfigPath"] = pipelinePath,
            ["TranslatorConfigPath"] = translatorPath,
            ["Mode"] = request.Mode
        };

        var pipelineOutput = Console.Out;
        try
        {
            Console.SetOut(Console.Error);
            var pipelineConfig = PipelineRunner.LoadConfig(pipelinePath);
            var runner = new PipelineRunner(new PipelineRegistry());
            var context = runner.Run(pipelineConfig, initialContext, translatorConfig);
            var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
            var subtitlePath = File.Exists(dirs.ProofreadSubtitlePath)
                ? dirs.ProofreadSubtitlePath
                : dirs.TranslatedSubtitlePath;

            if (!File.Exists(subtitlePath))
                throw new InvalidOperationException("The translation pipeline produced no subtitle artifact.");

            return Task.FromResult(new TranslationResult(subtitlePath));
        }
        finally
        {
            Console.SetOut(pipelineOutput);
        }
    }
}
