using System.Text.Json.Serialization;

namespace VideoSubtitleTranslator.Workbench;

public enum WorkbenchTaskStatus
{
    Queued,
    Processing,
    AwaitingReview,
    ExportQueued,
    Exporting,
    Completed,
    Failed,
    Cancelled,
    Interrupted
}

public sealed class WorkbenchTask
{
    public string Id { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public WorkbenchTaskStatus Status { get; set; } = WorkbenchTaskStatus.Queued;
    public string Stage { get; set; } = "Queued";
    public int ProcessedItems { get; set; }
    public int TotalItems { get; set; }
    public double ElapsedSeconds { get; set; }
    public int Revision { get; set; }
    public string? Error { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? QueuePosition { get; set; }
}

public sealed class SubtitleItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public bool NeedsReview { get; set; }
}

public sealed class SubtitleDocument
{
    public string TaskId { get; set; } = string.Empty;
    public int Revision { get; set; }
    public List<SubtitleItem> Items { get; set; } = new();
}

public sealed class ExportRecord
{
    public string Id { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public int Revision { get; set; }
    public bool Bilingual { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ChineseSrtPath { get; set; } = string.Empty;
    public string? BilingualSrtPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string OperationId { get; set; } = string.Empty;
}

public sealed record StageCheckpoint(string Stage, string ArtifactPath, string ArtifactSha256, DateTimeOffset CompletedAt);

public enum WorkKind
{
    Pipeline,
    Retranslate,
    Export
}

public enum WorkbenchOperationStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

public sealed record PendingOperation(string OperationId, string TaskId, WorkKind Kind, int ExpectedRevision, string? SubtitleId, bool Bilingual);

public sealed record SaveSubtitleRequest(int ExpectedRevision, IReadOnlyList<SubtitleItem> Items);

public sealed record RetranslateRequest(int ExpectedRevision, string SubtitleId, string OperationId);

public sealed record ExportRequest(int ExpectedRevision, bool Bilingual, string OperationId);

public sealed record ApiError(string Code, string Message);
