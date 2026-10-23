namespace VideoSubtitleTranslator.Cli;

public interface ICliTaskWorkflow
{
    Task<IReadOnlyList<CliTaskRecord>> GetTaskHistoryAsync(CancellationToken cancellationToken = default);
    Task<TranslationResult> ResumeTaskAsync(string id, CancellationToken cancellationToken = default);
}
