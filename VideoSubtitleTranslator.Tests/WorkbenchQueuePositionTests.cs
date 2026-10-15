using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Tests;

public sealed class WorkbenchQueuePositionTests
{
    [Fact]
    public async Task Queue_positions_follow_the_actual_enqueue_order()
    {
        var root = Path.Combine(Path.GetTempPath(), "vst-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new WorkbenchStore(new WorkbenchOptions { DataDirectory = root });
            var worker = new WorkbenchWorker(store, new WorkbenchOptions { DataDirectory = root });

            await worker.EnqueueAsync(new WorkCommand("pipeline", WorkKind.Pipeline));
            await worker.EnqueueAsync(new WorkCommand("export", WorkKind.Export));

            var positions = worker.GetQueuePositions();
            Assert.Equal(1, positions["pipeline"]);
            Assert.Equal(2, positions["export"]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
