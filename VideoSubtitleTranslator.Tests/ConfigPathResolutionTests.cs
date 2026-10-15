namespace VideoSubtitleTranslator.Tests;

public class ConfigPathResolutionTests
{
    [Fact]
    public void Web_workbench_defaults_data_to_the_project_root_outputs_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "vst-root-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "VideoSubtitleTranslator", "bin", "Debug");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(nested);

            Assert.Equal(Path.Combine(root, "outputs"), Workbench.WorkbenchOptions.DefaultDataDirectory(nested));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Standard_mode_defaults_to_standard_pipeline_config()
    {
        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths("standard", "/app");

        Assert.Equal(Path.Combine("/app", "pipeline.config.json"), pipelinePath);
        Assert.Equal(Path.Combine("/app", "Config", "translator.config.json"), translatorPath);
    }

    [Fact]
    public void Meme_mode_points_to_meme_configs()
    {
        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths("meme", "/app");

        Assert.Equal(Path.Combine("/app", "pipeline_meme.config.json"), pipelinePath);
        Assert.Equal(Path.Combine("/app", "Config", "translator_meme.config.json"), translatorPath);
    }

    [Fact]
    public void Unknown_mode_falls_back_to_standard()
    {
        var (pipelinePath, translatorPath) = Program.ResolveConfigPaths("unknown", "/app");

        Assert.Equal(Path.Combine("/app", "pipeline.config.json"), pipelinePath);
        Assert.Equal(Path.Combine("/app", "Config", "translator.config.json"), translatorPath);
    }
}
