using System.Collections.Concurrent;
using System.Text;

namespace VideoSubtitleTranslator;

public static class PromptProvider
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string Get(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return Cache.GetOrAdd(normalized, LoadPrompt);
    }

    private static string LoadPrompt(string normalizedPath)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Prompts", normalizedPath),
            Path.Combine(Directory.GetCurrentDirectory(), "Prompts", normalizedPath)
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return File.ReadAllText(path, Encoding.UTF8);
        }

        throw new FileNotFoundException($"未找到提示词文件: Prompts/{normalizedPath}");
    }
}
