using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Tests;

public sealed class WorkbenchStoreTests
{
    [Fact]
    public void Subtitle_save_uses_optimistic_revision_and_preserves_old_revision()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vst-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new WorkbenchStore(new WorkbenchOptions { DataDirectory = directory });
            var task = new WorkbenchTask { Id = "task", OperationId = "upload", WorkspacePath = directory };
            Assert.True(store.TryCreateTask(task));
            var first = new List<SubtitleItem> { new() { StartMs = 0, EndMs = 1000, SourceText = "hello", TranslatedText = "你好" } };
            Assert.True(store.TrySaveSubtitles("task", 0, first, out var saved));
            Assert.Equal(1, saved!.Revision);
            first[0].TranslatedText = "你好呀";
            Assert.False(store.TrySaveSubtitles("task", 0, first, out _));
            var current = store.GetSubtitles("task");
            Assert.Equal(1, current!.Revision);
            Assert.Equal("你好", current.Items[0].TranslatedText);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Pending_export_operation_survives_restart_recovery_lookup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vst-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new WorkbenchStore(new WorkbenchOptions { DataDirectory = directory });
            var task = new WorkbenchTask { Id = "task", OperationId = "upload", WorkspacePath = directory, Status = WorkbenchTaskStatus.ExportQueued };
            Assert.True(store.TryCreateTask(task));
            Assert.True(store.TryBeginOperation("export-1", task.Id, WorkKind.Export, expectedRevision: 3, bilingual: true));
            Assert.False(store.TryBeginOperation("export-1", task.Id, WorkKind.Export, expectedRevision: 3, bilingual: true));
            store.MarkOperationFailed("export-1", "test failure");
            Assert.True(store.TryBeginOperation("export-1", task.Id, WorkKind.Export, expectedRevision: 3, bilingual: true));

            var pending = Assert.Single(store.FindPendingOperations(WorkKind.Export));
            Assert.Equal("export-1", pending.OperationId);
            Assert.Equal(3, pending.ExpectedRevision);
            Assert.True(pending.Bilingual);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Cancelled_task_cannot_accept_a_late_translation_response()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vst-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new WorkbenchStore(new WorkbenchOptions { DataDirectory = directory });
            var task = new WorkbenchTask { Id = "task", OperationId = "upload", WorkspacePath = directory, Status = WorkbenchTaskStatus.AwaitingReview };
            Assert.True(store.TryCreateTask(task));
            var items = new List<SubtitleItem> { new() { StartMs = 0, EndMs = 1000, SourceText = "hello", TranslatedText = "你好" } };
            Assert.True(store.TrySaveSubtitles(task.Id, 0, items, out _));
            task = store.GetTask(task.Id)!;
            task.Status = WorkbenchTaskStatus.Cancelled;
            store.SaveTask(task);

            Assert.False(store.TryApplyTranslation(task.Id, items[0].Id, 1, "晚到的翻译", out _));
            Assert.Equal(1, store.GetSubtitles(task.Id)!.Revision);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
