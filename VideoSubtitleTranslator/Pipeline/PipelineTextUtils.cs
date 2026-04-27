using System;
using System.Collections.Generic;

namespace VideoSubtitleTranslator.Pipeline;

internal static class PipelineTextUtils
{
    public static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> replacements)
    {
        var result = template;
        foreach (var pair in replacements)
            result = result.Replace($"{{{{{pair.Key}}}}}", pair.Value ?? string.Empty, StringComparison.Ordinal);
        return result;
    }

    public static string? ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = s.IndexOf('\n');
            if (firstNl > 0)
            {
                s = s[(firstNl + 1)..].TrimStart();
                var fence = s.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0) s = s[..fence].TrimEnd();
            }
        }

        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return s.Substring(start, end - start + 1);
    }
}
