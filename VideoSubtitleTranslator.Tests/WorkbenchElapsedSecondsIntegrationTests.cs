using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Tests;

public sealed class WorkbenchElapsedSecondsIntegrationTests
{
    [Fact]
    public async Task Successful_export_persists_elapsed_seconds()
    {
        var root = Path.Combine(Path.GetTempPath(), "vst-elapsed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new WorkbenchOptions { DataDirectory = Path.Combine(root, "data") };
            using var store = new WorkbenchStore(options);
            var workspace = Path.Combine(root, "task");
            Directory.CreateDirectory(workspace);
            var task = new WorkbenchTask { Id = "task", OperationId = "upload", WorkspacePath = workspace, Status = WorkbenchTaskStatus.AwaitingReview };
            Assert.True(store.TryCreateTask(task));
            Assert.True(store.TrySaveSubtitles(task.Id, 0, [new SubtitleItem { StartMs = 0, EndMs = 900, SourceText = "Hello", TranslatedText = "你好" }], out var document));

            var worker = new WorkbenchWorker(store, options, new DelayedExporter());
            await worker.StartAsync(CancellationToken.None);
            try
            {
                Assert.True(store.TryBeginOperation("export", task.Id, WorkKind.Export, document!.Revision));
                await worker.EnqueueAsync(new WorkCommand(task.Id, WorkKind.Export, document.Revision, Bilingual: false, OperationId: "export"));
                var timeout = DateTimeOffset.UtcNow.AddSeconds(10);
                while (DateTimeOffset.UtcNow < timeout)
                {
                    var current = store.GetTask(task.Id)!;
                    if (current.Status == WorkbenchTaskStatus.Completed && current.ElapsedSeconds > 0) break;
                    await Task.Delay(25);
                }
                var completed = store.GetTask(task.Id)!;
                Assert.Equal(WorkbenchTaskStatus.Completed, completed.Status);
                Assert.True(completed.ElapsedSeconds > 0, "A successful export must persist a non-zero elapsed duration.");
            }
            finally
            {
                await worker.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class DelayedExporter : MediaExporter
    {
        public DelayedExporter() : base("unused") { }

        public override async Task<ExportRecord> ExportAsync(WorkbenchTask task, SubtitleDocument document, bool bilingual, CancellationToken cancellationToken)
        {
            await Task.Delay(25, cancellationToken);
            return new ExportRecord { Id = "export", TaskId = task.Id, Revision = document.Revision, Bilingual = bilingual };
        }
    }
}
