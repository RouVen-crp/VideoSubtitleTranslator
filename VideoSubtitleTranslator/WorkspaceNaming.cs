using System.Text;

namespace VideoSubtitleTranslator;

/// <summary>
/// 把视频标题转换成安全的目录名。
/// Windows 会静默丢弃结尾的点和空格、拒绝保留设备名、禁止若干字符；
/// 一旦生成的目录名与实际创建出来的名字不一致，后续所有路径（info.txt、字幕、检查点）都找不到文件。
/// </summary>
public static class WorkspaceNaming
{
    private const int MaxLength = 80;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string ToSafeDirectoryName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "video";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(title.Length);
        foreach (var character in title.Trim())
        {
            if (Array.IndexOf(invalid, character) >= 0) continue;
            // 保持既有行为：去掉引号、逗号和撇号，空格换成下划线。
            if (character is '"' or ',' or '\'') continue;
            builder.Append(character == ' ' ? '_' : character);
        }

        var name = builder.ToString().Trim().Trim('.', ' ');

        if (name.Length > MaxLength)
        {
            name = name[..MaxLength].TrimEnd('.', ' ');
        }

        if (name.Length == 0 || name.All(character => character is '_' or '.'))
        {
            return "video";
        }

        return ReservedNames.Contains(name) ? "_" + name : name;
    }
}
