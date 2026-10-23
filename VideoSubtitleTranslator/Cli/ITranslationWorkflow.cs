namespace VideoSubtitleTranslator.Cli;

public sealed record TranslationRequest(string Input, string Mode, string WorkspaceRoot, bool IsLocalFile = false);

public sealed record TranslationResult(string SubtitlePath, string? SidecarSubtitlePath = null);

public interface ITranslationWorkflow
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
