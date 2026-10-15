using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Tests;

public sealed class SrtCodecTests
{
    [Fact]
    public void Parse_and_write_preserve_millisecond_timing_and_text()
    {
        var items = SrtCodec.Parse("1\n00:00:01,250 --> 00:00:03,500\nHello\nworld\n\n");

        Assert.Single(items);
        Assert.Equal(1250, items[0].StartMs);
        Assert.Equal(3500, items[0].EndMs);
        Assert.Equal("Hello\nworld", items[0].SourceText);
        Assert.Contains("00:00:01,250 --> 00:00:03,500", SrtCodec.Write(items, i => i.SourceText));
    }
}
