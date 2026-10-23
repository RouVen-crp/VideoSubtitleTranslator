using System.Text;
using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Cli;

internal static class BilingualSubtitleBuilder
{
    public static string Create(string sourceSubtitlePath, string translatedSubtitlePath, string workspacePath)
    {
        var source = SrtCodec.Read(sourceSubtitlePath);
        var translated = SrtCodec.Read(translatedSubtitlePath);
        if (source.Count == 0 || source.Count != translated.Count)
            throw new InvalidOperationException("源字幕与译文字幕条数不一致，无法生成双语 SRT。");

        if (source.Where((item, index) => item.StartMs != translated[index].StartMs || item.EndMs != translated[index].EndMs).Any())
            throw new InvalidOperationException("源字幕与译文字幕时间不一致，无法安全对齐生成双语 SRT。");

        var bilingual = source.Select((item, index) => new SubtitleItem
        {
            StartMs = item.StartMs,
            EndMs = item.EndMs,
            SourceText = $"{item.SourceText}\n{translated[index].SourceText}"
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
