namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 字幕排版规则：单行字数、断行位置、停留时长与断句合并。
/// 参考通行字幕规范：中文每行不超过 15 字、英文每行不超过 37 字符，
/// 每条至少停留 1 秒、最长 6 秒，读完再留 0.5 秒，间隔过短则连成一条。
/// </summary>
public static class SubtitleLayout
{
    public const int MaxChineseCharactersPerLine = 15;
    public const int MaxEnglishCharactersPerLine = 37;
    public const double MinimumDurationSeconds = 1.0;
    public const double MaximumDurationSeconds = 6.0;
    public const double ReadTailSeconds = 0.5;
    public const double MinimumGapSeconds = 0.2;

    /// <summary>一条待显示的字幕：中文必填，英文与注释可选。</summary>
    public sealed record Cue(double Start, double End, string Chinese, string? English, string? Annotation);

    private const string ChineseBreakCharacters = "，。！？；：、,.!?;:";

    /// <summary>为切在标点上，允许单行比上限多出的字数。</summary>
    internal const int MaxLookaheadCharacters = 4;

    public static IReadOnlyList<Cue> Arrange(IReadOnlyList<Cue> source)
    {
        var arranged = new List<Cue>();
        foreach (var cue in source)
        {
            arranged.AddRange(SplitCue(cue));
        }

        ApplyTiming(arranged);
        return arranged;
    }

    private static IEnumerable<Cue> SplitCue(Cue cue)
    {
        var duration = Math.Max(0, cue.End - cue.Start);
        var parts = SplitChinese(cue.Chinese);

        // 拆得越多越容易"闪一下"：每条至少要能分到 1 秒。
        var maxParts = Math.Max(1, (int)Math.Floor(duration / MinimumDurationSeconds));
        while (parts.Count > maxParts && parts.Count > 1)
        {
            var shortest = Enumerable.Range(0, parts.Count - 1)
                .OrderBy(index => parts[index].Length + parts[index + 1].Length)
                .First();
            parts[shortest] += parts[shortest + 1];
            parts.RemoveAt(shortest + 1);
        }

        // 时长远超单条上限时继续拆，避免一条字幕停 7 秒以上。
        var needed = (int)Math.Ceiling(duration / MaximumDurationSeconds);
        while (parts.Count < needed && parts.Count > 0)
        {
            var longest = parts.OrderByDescending(part => part.Length).First();
            var index = parts.IndexOf(longest);
            if (longest.Length < 2) break;
            var middle = longest.Length / 2;
            parts.RemoveAt(index);
            parts.InsertRange(index, [longest[..middle].Trim(), longest[middle..].Trim()]);
        }

        var englishParts = SplitEnglish(cue.English, parts);

        if (parts.Count == 1)
        {
            yield return cue with { Chinese = parts[0], English = englishParts.Count > 0 ? englishParts[0] : null };
            yield break;
        }

        var weights = parts.Select(part => Math.Max(1, part.Length)).ToArray();
        var totalWeight = weights.Sum();
        var minimumTotal = parts.Count * MinimumDurationSeconds;
        var extra = Math.Max(0, duration - minimumTotal);
        var shortCue = duration < minimumTotal;

        var offset = cue.Start;
        for (var i = 0; i < parts.Count; i++)
        {
            var slice = shortCue
                ? duration / parts.Count
                : MinimumDurationSeconds + extra * weights[i] / totalWeight;
            var start = offset;
            var end = i == parts.Count - 1 ? cue.End : start + slice;
            offset = end;

            yield return new Cue(
                start,
                end,
                parts[i],
                i < englishParts.Count ? englishParts[i] : null,
                i == 0 ? cue.Annotation : null);
        }
    }

    /// <summary>
    /// 按标点把中文切成不超过 15 字的分句。
    /// 到达上限时向后最多看 4 个字找标点：宁可略长一点，也不要把词或短语从中间砍开。
    /// </summary>
    internal static List<string> SplitChinese(string text)
    {
        var raw = text.Trim();
        if (raw.Length <= MaxChineseCharactersPerLine)
        {
            return [raw];
        }

        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var index = 0; index < raw.Length; index++)
        {
            current.Append(raw[index]);
            if (current.Length < MaxChineseCharactersPerLine)
            {
                continue;
            }

            var breakAt = index;
            for (var look = 1; look <= MaxLookaheadCharacters && index + look < raw.Length; look++)
            {
                if (!ChineseBreakCharacters.Contains(raw[index + look]))
                {
                    continue;
                }

                breakAt = index + look;
                break;
            }

            while (index < breakAt)
            {
                index++;
                current.Append(raw[index]);
            }

            var part = current.ToString().Trim();
            if (part.Length > 0) parts.Add(part);
            current.Clear();
        }

        var tail = current.ToString().Trim();
        if (tail.Length > 0) parts.Add(tail);
        return parts.Count == 0 ? [raw] : parts;
    }

    /// <summary>把英文按词边界切成与中文分句数量一致的若干段。</summary>
    internal static List<string> SplitEnglish(string? english, IReadOnlyList<string> chineseParts)
    {
        if (string.IsNullOrWhiteSpace(english) || chineseParts.Count <= 1)
        {
            return english is null ? [] : [english];
        }

        var words = english.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < chineseParts.Count)
        {
            return [english];
        }

        var totalWeight = chineseParts.Sum(part => Math.Max(1, part.Length));
        var result = new List<string>();
        var consumed = 0;
        var remainingWeight = totalWeight;
        var remainingWords = words.Length;

        for (var i = 0; i < chineseParts.Count; i++)
        {
            var weight = Math.Max(1, chineseParts[i].Length);
            var take = i == chineseParts.Count - 1
                ? remainingWords
                : Math.Max(1, (int)Math.Round(remainingWords * (double)weight / remainingWeight));
            take = Math.Min(take, remainingWords - (chineseParts.Count - 1 - i));

            result.Add(string.Join(' ', words.Skip(consumed).Take(take)));
            consumed += take;
            remainingWords -= take;
            remainingWeight -= weight;
            if (remainingWords <= 0) break;
        }

        while (result.Count < chineseParts.Count) result.Add(string.Empty);
        return result;
    }

    private static void ApplyTiming(List<Cue> cues)
    {
        if (cues.Count == 0)
        {
            return;
        }

        for (var i = 0; i < cues.Count; i++)
        {
            var current = cues[i];
            var limit = i + 1 < cues.Count ? cues[i + 1].Start : double.MaxValue;

            // 读完再多留一点，但不超过下一条的开始，也不超过单条上限。
            var end = Math.Min(current.End + ReadTailSeconds, current.Start + MaximumDurationSeconds);
            end = Math.Min(end, limit);

            if (i + 1 < cues.Count && cues[i + 1].Start - end < MinimumGapSeconds)
            {
                end = cues[i + 1].Start;
            }

            if (end < current.End)
            {
                end = current.End;
            }

            cues[i] = current with { End = end };
        }
    }
}
