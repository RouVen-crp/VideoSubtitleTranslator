using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VideoSubtitleTranslator.Workbench;

public static partial class SrtCodec
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static List<SubtitleItem> Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("字幕文件不存在", path);
        return Parse(File.ReadAllText(path));
    }

    public static List<SubtitleItem> Parse(string text)
    {
        var result = new List<SubtitleItem>();
        foreach (var block in BlockRegex().Split(text.Replace("\r\n", "\n")).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var lines = block.Trim().Split('\n');
            if (lines.Length < 3 || !TimestampRegex().IsMatch(lines[1])) continue;
            var match = TimestampRegex().Match(lines[1]);
            result.Add(new SubtitleItem
            {
                StartMs = ParseTimestamp(match.Groups[1].Value),
                EndMs = ParseTimestamp(match.Groups[2].Value),
                SourceText = string.Join("\n", lines.Skip(2)).Trim()
            });
        }
        return result;
    }

    public static string Write(IEnumerable<SubtitleItem> items, Func<SubtitleItem, string> textSelector)
    {
        var builder = new StringBuilder();
        var index = 1;
        foreach (var item in items)
        {
            builder.Append(index++).Append('\n');
            builder.Append(FormatTimestamp(item.StartMs)).Append(" --> ").Append(FormatTimestamp(item.EndMs)).Append('\n');
            builder.Append(textSelector(item).Replace("\r\n", "\n")).Append("\n\n");
        }
        return builder.ToString();
    }

    private static long ParseTimestamp(string value)
    {
        var parts = value.Split(':', ',');
        return ((long.Parse(parts[0], Invariant) * 60 + long.Parse(parts[1], Invariant)) * 60 + long.Parse(parts[2], Invariant)) * 1000 + long.Parse(parts[3], Invariant);
    }

    private static string FormatTimestamp(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
    }

    [GeneratedRegex("\\n\\s*\\n")]
    private static partial Regex BlockRegex();

    [GeneratedRegex(@"(?<start>\d{2}:\d{2}:\d{2},\d{3})\s*-->\s*(?<end>\d{2}:\d{2}:\d{2},\d{3})")]
    private static partial Regex TimestampRegex();
}
