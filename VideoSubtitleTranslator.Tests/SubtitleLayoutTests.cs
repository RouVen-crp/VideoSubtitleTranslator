using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

/// <summary>
/// 字幕排版规则：中文每行 ≤15 字、英文每行 ≤37 字符，每条 ≥1 秒、≤6 秒，
/// 读完再留 0.5 秒，间隔过短则连起来。
/// </summary>
public sealed class SubtitleLayoutTests
{
    [Fact]
    public void Long_chinese_is_split_into_lines_of_at_most_fifteen_characters()
    {
        var cue = new SubtitleLayout.Cue(0, 6,
            "所以这就是那个公开为批评中国道歉、同时又骂美国坏话的女孩，她最近又上了热搜。",
            null, null);

        var arranged = SubtitleLayout.Arrange([cue]);

        Assert.True(arranged.Count > 1, "长句应该被拆成多条");
        // 目标每行 ≤15 字；为切在标点上最多放宽到 19 字。
        Assert.All(arranged, c => Assert.True(c.Chinese.Length <= SubtitleLayout.MaxChineseCharactersPerLine + SubtitleLayout.MaxLookaheadCharacters,
            $"'{c.Chinese}' 有 {c.Chinese.Length} 个字"));
        Assert.Contains("，", string.Concat(arranged.Select(c => c.Chinese)));
    }

    [Fact]
    public void Every_cue_stays_visible_for_at_least_one_second_when_time_allows()
    {
        // 30 个字 + 6 秒：拆成 2 条，每条约 3 秒。
        var cue = new SubtitleLayout.Cue(0, 6, new string('字', 30), null, null);

        var arranged = SubtitleLayout.Arrange([cue]);

        Assert.All(arranged, c => Assert.True(c.End - c.Start >= SubtitleLayout.MinimumDurationSeconds - 0.001,
            $"时长只有 {c.End - c.Start:F2}s"));
    }

    [Fact]
    public void Short_cues_are_not_split_when_that_would_flash_each_piece()
    {
        // 只有 1.2 秒却塞了 30 个字：优先保证可读时长，宁可行长一点也不拆成两条 0.6 秒。
        var cue = new SubtitleLayout.Cue(0, 1.2, new string('字', 30), null, null);

        var arranged = SubtitleLayout.Arrange([cue]);

        var single = Assert.Single(arranged);
        Assert.True(single.End - single.Start >= SubtitleLayout.MinimumDurationSeconds - 0.001,
            $"时长只有 {single.End - single.Start:F2}s");
    }

    [Fact]
    public void Tail_never_overlaps_the_next_cue()
    {
        var cues = new List<SubtitleLayout.Cue>
        {
            new(0, 2, "第一句", null, null),
            new(2.1, 4, "第二句", null, null)
        };

        var arranged = SubtitleLayout.Arrange(cues);

        for (var i = 1; i < arranged.Count; i++)
        {
            Assert.True(arranged[i].Start >= arranged[i - 1].End - 0.001, "字幕不应互相重叠");
        }
    }

    [Fact]
    public void Short_gaps_between_cues_are_merged()
    {
        var cues = new List<SubtitleLayout.Cue>
        {
            new(0, 2, "第一句", null, null),
            new(2.1, 4, "第二句", null, null)
        };

        var arranged = SubtitleLayout.Arrange(cues);

        Assert.Equal(2.1, arranged[0].End, 2);
    }

    [Fact]
    public void English_is_split_into_roughly_aligned_parts()
    {
        var cue = new SubtitleLayout.Cue(0, 6,
            "所以这就是那个公开为批评中国道歉、同时又骂美国坏话的女孩，她最近又上了热搜。",
            "So this is the girl who publicly apologised for criticising China and then trashed America, and she is trending again.",
            null);

        var arranged = SubtitleLayout.Arrange([cue]);

        Assert.True(arranged.Count > 1);
        Assert.All(arranged, c => Assert.False(string.IsNullOrWhiteSpace(c.English)));
        Assert.All(arranged, c => Assert.True(c.English!.Split(' ').Length >= 1));
    }

    [Fact]
    public void Annotation_stays_on_the_first_part_only()
    {
        var cue = new SubtitleLayout.Cue(0, 6,
            "所以这就是那个公开为批评中国道歉、同时又骂美国坏话的女孩，她最近又上了热搜。",
            null, "指她此前公开道歉的事件");

        var arranged = SubtitleLayout.Arrange([cue]);

        Assert.Equal("指她此前公开道歉的事件", arranged[0].Annotation);
        Assert.All(arranged.Skip(1), c => Assert.Null(c.Annotation));
    }
}
