namespace VideoSubtitleTranslator.Pipeline;

/// <summary>一条带时间范围的字幕行，用于把中文译块与英文原文行配对。</summary>
/// <param name="StartMs">起始毫秒。</param>
/// <param name="EndMs">结束毫秒。</param>
/// <param name="Text">该行的文本。</param>
public sealed record SubtitleLine(long StartMs, long EndMs, string Text);

/// <summary>
/// 按时间重叠把「目标字幕块」（例如校对后的中文块）与「源字幕行」（例如英文原文行）配对。
/// 校对会合并相邻句，目标块数量少于源行数量，因此不能按序号配对：每个源行归属重叠最多的目标块。
/// </summary>
public static class SubtitlePairing
{
    /// <summary>返回每个源行归属的目标块下标；没有时间重叠时返回 -1。</summary>
    public static IReadOnlyList<int> AssignSourceTargets(
        IReadOnlyList<SubtitleLine> targets,
        IReadOnlyList<SubtitleLine> sources)
    {
        var result = new int[sources.Count];
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var bestTarget = -1;
            var bestOverlap = 0L;
            for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                var overlap = OverlapMilliseconds(sources[sourceIndex], targets[targetIndex]);
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    bestTarget = targetIndex;
                }
            }

            result[sourceIndex] = bestTarget;
        }

        return result;
    }

    /// <summary>
    /// 为每个目标块给出对应的源文本：多个源行覆盖同一目标块时按时间顺序用空格拼接。
    /// 没有时间重叠、但两边条数相同时退化为按序号配对（校对未合并的常见场景）。
    /// </summary>
    public static IReadOnlyList<string?> PairTexts(
        IReadOnlyList<SubtitleLine> targets,
        IReadOnlyList<SubtitleLine>? sources)
    {
        var paired = new List<string?>(targets.Count);
        if (sources is null || sources.Count == 0)
        {
            for (var index = 0; index < targets.Count; index++) paired.Add(null);
            return paired;
        }

        var sourceTargets = AssignSourceTargets(targets, sources);
        var sourceTextsByTarget = new List<string>[targets.Count];
        for (var index = 0; index < sourceTextsByTarget.Length; index++) sourceTextsByTarget[index] = [];

        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var targetIndex = sourceTargets[sourceIndex];
            if (targetIndex >= 0) sourceTextsByTarget[targetIndex].Add(sources[sourceIndex].Text);
        }

        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var assigned = sourceTextsByTarget[targetIndex];
            if (assigned.Count == 0 && targets.Count == sources.Count)
                assigned.Add(sources[targetIndex].Text);

            var text = string.Join(" ", assigned);
            paired.Add(string.IsNullOrWhiteSpace(text) ? null : text);
        }

        return paired;
    }

    private static long OverlapMilliseconds(SubtitleLine first, SubtitleLine second)
    {
        var start = Math.Max(first.StartMs, second.StartMs);
        var end = Math.Min(first.EndMs, second.EndMs);
        return Math.Max(0, end - start);
    }
}
