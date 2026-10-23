namespace VideoSubtitleTranslator.Cli;

public sealed record TranslationRequest(string Input, string Mode, string WorkspaceRoot, bool IsLocalFile = false);

public sealed record TranslationResult(string SubtitlePath, string? SidecarSubtitlePath = null);

public sealed class LocalVideoValidationException(string message) : Exception(message);

public interface ITranslationWorkflow
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
