namespace VideoSubtitleTranslator.Cli;

public interface ICliQueueWorkflow : ICliTaskWorkflow
{
    Task<CliTaskRecord> EnqueueTranslationAsync(TranslationRequest request, CancellationToken cancellationToken = default);

    Task<TranslationResult> RunQueuedTaskAsync(
        string taskId,
        string executionInput,
        Func<IReadOnlyList<CliTaskRecord>, Task>? onQueueChanged = null,
        CancellationToken cancellationToken = default);

    Task InterruptQueuedTasksAsync(IEnumerable<string> taskIds, CancellationToken cancellationToken = default);
}
