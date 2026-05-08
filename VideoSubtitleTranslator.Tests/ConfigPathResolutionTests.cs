namespace VideoSubtitleTranslator.Tests;

public class ConfigPathResolutionTests
{
    [Fact]
    public void Standard_mode_defaults_to_standard_pipeline_config()
    {
        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths("standard", "/app");

        Assert.Equal("/app/pipeline.config.json", pipelinePath);
        Assert.Equal("/app/Config/translator.config.json", translatorPath);
    }

    [Fact]
    public void Meme_mode_points_to_meme_configs()
    {
        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths("meme", "/app");

        Assert.Equal("/app/pipeline_meme.config.json", pipelinePath);
        Assert.Equal("/app/Config/translator_meme.config.json", translatorPath);
    }

    [Fact]
    public void Unknown_mode_falls_back_to_standard()
    {
        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths("unknown", "/app");

        Assert.Equal("/app/pipeline.config.json", pipelinePath);
        Assert.Equal("/app/Config/translator.config.json", translatorPath);
    }
}
