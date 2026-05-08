using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

public class CulturalAnnotateStepRegistrationTests
{
    [Fact]
    public void Registry_resolves_CulturalAnnotate_DeepSeek()
    {
        var registry = new PipelineRegistry(typeof(DeepSeekTranslateStep).Assembly);

        var instance = registry.Resolve("CulturalAnnotate", "DeepSeek");

        Assert.NotNull(instance);
        Assert.Equal("CulturalAnnotate", instance.Step);
    }
}
