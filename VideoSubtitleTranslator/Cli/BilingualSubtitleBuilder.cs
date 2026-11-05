using System.Text;
using VideoSubtitleTranslator.Pipeline;
using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Cli;

internal static class BilingualSubtitleBuilder
{
    public static string Create(string sourceSubtitlePath, string translatedSubtitlePath, string workspacePath)
    {
        var source = SrtCodec.Read(sourceSubtitlePath);
        var translated = SrtCodec.Read(translatedSubtitlePath);
        if (translated.Count == 0 || source.Count == 0)
            throw new InvalidOperationException("缺少源字幕或译文字幕，无法生成双语 SRT。");

        // 校对会合并相邻句，译文的条数可能少于英文原文行；按时间重叠配对，不能按序号。
        var sourceLines = SubtitlePairing.PairTexts(
            translated.Select(item => new SubtitleLine(item.StartMs, item.EndMs, item.SourceText)).ToList(),
            source.Select(item => new SubtitleLine(item.StartMs, item.EndMs, item.SourceText)).ToList());

        var bilingual = translated.Select((item, index) =>
        {
            var english = sourceLines[index];
            return new SubtitleItem
            {
                StartMs = item.StartMs,
                EndMs = item.EndMs,
                SourceText = string.IsNullOrWhiteSpace(english) ? item.SourceText : $"{english}\n{item.SourceText}"
            };
        }).ToList();
        var content = SrtCodec.Write(bilingual, item => item.SourceText);
        Directory.CreateDirectory(workspacePath);

        for (var suffix = 1; ; suffix++)
        {
            var name = suffix == 1 ? "translated_bilingual.srt" : $"translated_bilingual.{suffix}.srt";
            var path = Path.Combine(workspacePath, name);
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(content);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Keep an earlier bilingual subtitle and create the next numbered artifact.
            }
        }
    }
}
