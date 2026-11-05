using System.Text;
using VideoSubtitleTranslator.Cli;
using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Tests;

public sealed class BilingualSubtitleBuilderTests
{
    [Fact]
    public void Create_pairs_english_lines_with_proofread_blocks_by_time_after_merge()
    {
        using var temp = new TemporaryDirectory();
        var normalized = Path.Combine(temp.Path, "subtitle.normalized.srt");
        var proofread = Path.Combine(temp.Path, "translated_subtitle.proofread.srt");
        File.WriteAllText(normalized,
            "1\n00:00:00,000 --> 00:00:01,000\nHello.\n\n" +
            "2\n00:00:01,000 --> 00:00:02,000\nHow are you?\n\n" +
            "3\n00:00:02,000 --> 00:00:03,000\nFine.\n\n", Encoding.UTF8);
        File.WriteAllText(proofread,
            "1\n00:00:00,000 --> 00:00:02,000\n你好，你好吗？\n\n" +
            "2\n00:00:02,000 --> 00:00:03,000\n很好。\n\n", Encoding.UTF8);

        var path = BilingualSubtitleBuilder.Create(normalized, proofread, temp.Path);

        var items = SrtCodec.Read(path);
        Assert.Equal(2, items.Count);
        Assert.Equal("Hello. How are you?\n你好，你好吗？", items[0].SourceText);
        Assert.Equal("Fine.\n很好。", items[1].SourceText);
    }

    [Fact]
    public void Create_pairs_one_to_one_when_counts_and_timestamps_match()
    {
        using var temp = new TemporaryDirectory();
        var normalized = Path.Combine(temp.Path, "subtitle.normalized.srt");
        var translated = Path.Combine(temp.Path, "translated_subtitle.srt");
        File.WriteAllText(normalized,
            "1\n00:00:00,000 --> 00:00:01,000\nHello there.\n\n", Encoding.UTF8);
        File.WriteAllText(translated,
            "1\n00:00:00,000 --> 00:00:01,000\n你好\n\n", Encoding.UTF8);

        var path = BilingualSubtitleBuilder.Create(normalized, translated, temp.Path);

        var items = SrtCodec.Read(path);
        var item = Assert.Single(items);
        Assert.Equal("Hello there.\n你好", item.SourceText);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
