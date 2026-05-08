using System.Text;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

public class BuildDualSubtitleTests
{
    [Fact]
    public void Registry_resolves_BuildDualSubtitle_Default()
    {
        var registry = new PipelineRegistry(typeof(DeepSeekTranslateStep).Assembly);

        var instance = registry.Resolve("BuildDualSubtitle", "Default");

        Assert.NotNull(instance);
        Assert.Equal("BuildDualSubtitle", instance.Step);
    }

    [Fact]
    public void ParseSrt_reads_entries_correctly()
    {
        var srt = new StringBuilder()
            .AppendLine("1")
            .AppendLine("00:00:01,000 --> 00:00:03,000")
            .AppendLine("Hello world")
            .AppendLine()
            .AppendLine("2")
            .AppendLine("00:00:04,000 --> 00:00:06,500")
            .AppendLine("This is a test")
            .AppendLine()
            .ToString();
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, srt, Encoding.UTF8);
        try
        {
            var entries = DefaultBuildDualSubtitleStep.ParseSrt(tmp);

            Assert.Equal(2, entries.Count);
            Assert.Equal("00:00:01.000", entries[0].Start);
            Assert.Equal("Hello world", entries[0].Text);
            Assert.Equal("00:00:04.000", entries[1].Start);
            Assert.Equal("This is a test", entries[1].Text);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void BuildAss_produces_valid_ass_with_annotation()
    {
        var srtEntries = new List<DefaultBuildDualSubtitleStep.SrtEntry>
        {
            new() { Index = 1, Start = "0:00:01.000", End = "0:00:03.000", Text = "搞什么鬼啊" }
        };
        var annotations = new List<DefaultBuildDualSubtitleStep.CulturalAnnotation>
        {
            new() { Index = 1, NeedsAnnotation = true, Annotation = "Damn在口语中表示极度惊讶" }
        };
        var ass = DefaultBuildDualSubtitleStep.BuildAss(srtEntries, annotations);

        Assert.Contains("[Script Info]", ass);
        Assert.Contains("Style: Translation", ass);
        Assert.Contains("Style: Annotation", ass);
        Assert.Contains("[Events]", ass);
        Assert.Contains("Dialogue:", ass);
        Assert.Contains("搞什么鬼啊", ass);
        Assert.Contains("极度惊讶", ass);
    }

    [Fact]
    public void BuildAss_no_annotation_when_not_needed()
    {
        var srtEntries = new List<DefaultBuildDualSubtitleStep.SrtEntry>
        {
            new() { Index = 1, Start = "0:00:01.000", End = "0:00:02.000", Text = "他走进了房间" }
        };
        var annotations = new List<DefaultBuildDualSubtitleStep.CulturalAnnotation>
        {
            new() { Index = 1, NeedsAnnotation = false, Annotation = "" }
        };

        var ass = DefaultBuildDualSubtitleStep.BuildAss(srtEntries, annotations);

        Assert.Contains("他走进了房间", ass);
        Assert.DoesNotContain("极度惊讶", ass);
    }

    [Fact]
    public void EscapeAssText_escapes_special_chars()
    {
        var escaped = DefaultBuildDualSubtitleStep.EscapeAssText("hello\\nworld");

        Assert.Equal("hello\\\\nworld", escaped);
    }
}
