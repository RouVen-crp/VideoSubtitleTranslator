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
        Assert.Contains("[Events]", ass);
        Assert.Contains("Dialogue:", ass);
        Assert.Contains("搞什么鬼啊", ass);
        Assert.Contains("极度惊讶", ass);
    }

    [Fact]
    public void BuildAss_attaches_annotations_by_global_index()
    {
        // 15 条字幕，注释挂在第 12 条：跨批次时也必须落在正确的句子上。
        var srtEntries = Enumerable.Range(1, 15)
            .Select(i => new DefaultBuildDualSubtitleStep.SrtEntry
            {
                Index = i,
                Start = $"0:00:{i:00}.000",
                End = $"0:00:{i:00}.900",
                Text = $"第{i}句"
            })
            .ToList();
        var annotations = new List<DefaultBuildDualSubtitleStep.CulturalAnnotation>
        {
            new() { Index = 12, NeedsAnnotation = true, Annotation = "第十二句的注释" }
        };

        var ass = DefaultBuildDualSubtitleStep.BuildAss(srtEntries, annotations);

        var dialogues = ass.Split('\n').Where(line => line.StartsWith("Dialogue:", StringComparison.Ordinal)).ToList();
        Assert.Equal(15, dialogues.Count);
        Assert.Single(dialogues.Where(line => line.Contains("第十二句的注释", StringComparison.Ordinal)));
        Assert.Contains("第十二句的注释", dialogues[11]);
        Assert.DoesNotContain("第十二句的注释", dialogues[0]);
    }

    [Fact]
    public void Ass_timestamps_use_two_digit_centiseconds()
    {
        // ASS 的时间格式是 h:mm:ss.cc；沿用 SRT 的三位毫秒会让 libass 解析出错，
        // 字幕整体滞后于画面（表现为"音画不同步"）。
        Assert.Equal("0:02:08.58", DefaultBuildDualSubtitleStep.ToAssTimestamp("00:02:08,580"));
        Assert.Equal("0:00:00.00", DefaultBuildDualSubtitleStep.ToAssTimestamp("00:00:00,000"));
        Assert.Equal("1:01:02.99", DefaultBuildDualSubtitleStep.ToAssTimestamp("01:01:02,999"));
    }

    [Fact]
    public void BuildAss_defines_a_play_resolution_so_text_does_not_overflow()
    {
        var srtEntries = new List<DefaultBuildDualSubtitleStep.SrtEntry>
        {
            new() { Index = 1, Start = "0:00:01.000", End = "0:00:03.000", Text = "很长的一句话" }
        };

        var ass = DefaultBuildDualSubtitleStep.BuildAss(srtEntries, []);

        // 没有 PlayRes 时 libass 按 384x288 推算，字号会被放大且长句冲出画面。
        Assert.Contains("PlayResX: 1920", ass);
        Assert.Contains("PlayResY: 1080", ass);
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

    [Fact]
    public void BuildAss_pairs_english_lines_by_time_after_chinese_blocks_were_merged()
    {
        // 校对把前两句合并成一个中文块后，英文原文行仍应按时间落在正确的 Dialogue 上。
        var srtEntries = new List<DefaultBuildDualSubtitleStep.SrtEntry>
        {
            new() { Index = 1, Start = "00:00:00.000", End = "00:00:02.000", Text = "合并后的一句" },
            new() { Index = 2, Start = "00:00:02.000", End = "00:00:03.000", Text = "第三句" }
        };
        var english = new List<SubtitleLine>
        {
            new(0, 1000, "First line."),
            new(1000, 2000, "Second line."),
            new(2000, 3000, "Third line.")
        };

        var ass = DefaultBuildDualSubtitleStep.BuildAss(srtEntries, [], english);
        var dialogues = ass.Split('\n').Where(line => line.StartsWith("Dialogue:", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, dialogues.Count);
        Assert.Contains("First line.", dialogues[0]);
        Assert.Contains("Second line.", dialogues[0]);
        Assert.Contains("Third line.", dialogues[1]);
        Assert.DoesNotContain("Third line.", dialogues[0]);
    }
}
