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
            var store = new WorkbenchStore(new WorkbenchOptions { DataDirectory = directory });
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
}
