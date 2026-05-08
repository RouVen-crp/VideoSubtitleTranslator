using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

public class MemeTranslateStepRegistrationTests
{
    [Fact]
    public void Registry_resolves_Translate_Meme()
    {
        var registry = new PipelineRegistry(typeof(DeepSeekTranslateStep).Assembly);

        var instance = registry.Resolve("Translate", "Meme");

        Assert.NotNull(instance);
        Assert.Equal("Translate", instance.Step);
    }
}
