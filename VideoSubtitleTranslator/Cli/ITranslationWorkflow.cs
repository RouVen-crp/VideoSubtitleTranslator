namespace VideoSubtitleTranslator.Cli;

public sealed record TranslationRequest(string Input, string Mode, string WorkspaceRoot);

public sealed record TranslationResult(string SubtitlePath);

public interface ITranslationWorkflow
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
