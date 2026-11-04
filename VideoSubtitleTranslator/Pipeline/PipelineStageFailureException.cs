namespace VideoSubtitleTranslator.Pipeline;

/// <summary>A safe, user-facing failure summary for an external pipeline stage.</summary>
public sealed class PipelineStageFailureException(string message) : Exception(message);
