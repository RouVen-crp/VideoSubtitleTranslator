using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Tests;

public sealed class MediaProbeTests
{
    [Fact]
    public async Task Rejects_non_mp4_before_invoking_external_probe()
    {
        var result = await new MediaProbe().ValidateAsync("sample.mkv");

        Assert.False(result.IsValid);
        Assert.Contains("MP4", result.Error);
    }
}
