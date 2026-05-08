namespace VideoSubtitleTranslator.Tests;

public class TranslatorRuntimeConfigTests
{
    private static string ConfigDir => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "VideoSubtitleTranslator", "Config"));

    [Fact]
    public void Load_meme_config_sets_CulturalAnnotateEnabled()
    {
        var path = Path.Combine(ConfigDir, "translator_meme.config.json");
        var config = TranslatorRuntimeConfig.Load(path);

        Assert.True(config.Translation.CulturalAnnotateEnabled);
    }

    [Fact]
    public void Load_standard_config_has_CulturalAnnotateEnabled_false_by_default()
    {
        var path = Path.Combine(ConfigDir, "translator.config.json");
        var config = TranslatorRuntimeConfig.Load(path);

        Assert.False(config.Translation.CulturalAnnotateEnabled);
    }
}
