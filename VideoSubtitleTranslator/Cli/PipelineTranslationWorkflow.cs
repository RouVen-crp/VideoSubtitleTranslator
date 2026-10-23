using VideoSubtitleTranslator.Pipeline;
using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Cli;

public sealed class PipelineTranslationWorkflow : ITranslationWorkflow
{
    private readonly Func<string, CancellationToken, Task<string?>> _mediaValidator;
    private readonly Func<PipelineConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>> _pipelineRunner;

    public PipelineTranslationWorkflow(
        Func<string, CancellationToken, Task<string?>>? mediaValidator = null,
        Func<PipelineConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>>? pipelineRunner = null)
    {
        _mediaValidator = mediaValidator ?? ValidateMediaAsync;
        _pipelineRunner = pipelineRunner ?? ((config, context, runtime) => new PipelineRunner(new PipelineRegistry()).Run(config, context, runtime));
    }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Program.LoadEnv();

        if (request.IsLocalFile)
            return TranslateLocalAsync(request, cancellationToken);

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

    private async Task<TranslationResult> TranslateLocalAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.Input))
            throw new FileNotFoundException("本地视频文件不存在。", request.Input);
        var validationError = await _mediaValidator(request.Input, cancellationToken);
        if (!string.IsNullOrWhiteSpace(validationError))
            throw new InvalidDataException(validationError);

        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths(request.Mode, AppContext.BaseDirectory);
        var translatorConfig = TranslatorRuntimeConfig.Load(translatorPath);
        translatorConfig.Mode = request.Mode;
        translatorConfig.Burn.Enabled = false;

        var extension = Path.GetExtension(request.Input);
        var title = Path.GetFileNameWithoutExtension(request.Input);
        var initialContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Url"] = request.Input,
            ["WorkspaceRoot"] = request.WorkspaceRoot,
            ["WorkspacePath"] = Path.Combine(request.WorkspaceRoot, title),
            ["VideoExt"] = extension,
            ["VideoTitle"] = title,
            ["VideoAuthor"] = string.Empty,
            ["VideoUploadDate"] = string.Empty,
            ["Mode"] = request.Mode
        };

        var pipelineOutput = Console.Out;
        try
        {
            Console.SetOut(Console.Error);
            Directory.CreateDirectory(initialContext["WorkspacePath"]);
            var dirs = new WorkDirs(initialContext["WorkspacePath"], extension);
            File.Copy(request.Input, dirs.VideoPath, overwrite: true);

            var config = PipelineRunner.LoadConfig(pipelinePath);
            config.Steps = config.Steps.Where(step => step.Step is not ("InitMetadata" or "Download" or "BurnSubtitle")).ToList();
            var context = _pipelineRunner(config, initialContext, translatorConfig);
            dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
            var subtitlePath = File.Exists(dirs.ProofreadSubtitlePath) ? dirs.ProofreadSubtitlePath : dirs.TranslatedSubtitlePath;
            if (!File.Exists(subtitlePath))
                throw new InvalidOperationException("The translation pipeline produced no subtitle artifact.");

            var sidecar = GetAvailableSidecarPath(request.Input);
            File.Copy(subtitlePath, sidecar, overwrite: false);
            return new TranslationResult(subtitlePath, sidecar);
        }
        finally
        {
            Console.SetOut(pipelineOutput);
        }
    }

    private static string GetAvailableSidecarPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath)!;
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var candidate = Path.Combine(directory, $"{name}.zh.srt");
        for (var suffix = 2; File.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{name}.zh.{suffix}.srt");
        return candidate;
    }

    private static async Task<string?> ValidateMediaAsync(string path, CancellationToken cancellationToken)
    {
        var result = await new MediaProbe().ValidateAsync(path, cancellationToken);
        return result.IsValid ? null : result.Error ?? "无法读取视频媒体信息。请确认文件未损坏，且已安装 ffmpeg/ffprobe。";
    }
}
