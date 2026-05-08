using System.Text.Json;

namespace VideoSubtitleTranslator.Tests;

public class CookiesConfigTests
{
    [Fact]
    public void Load_config_with_cookies_from_browser_deserialized()
    {
        var json = "{\"cookiesFromBrowser\":\"chrome\"}";
        var config = JsonSerializer.Deserialize<TranslatorRuntimeConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(config);
        Assert.Equal("chrome", config!.CookiesFromBrowser);
    }

    [Fact]
    public void Load_config_without_cookies_from_browser_defaults_to_empty()
    {
        var json = "{}";
        var config = JsonSerializer.Deserialize<TranslatorRuntimeConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(config);
        Assert.Equal(string.Empty, config!.CookiesFromBrowser);
    }
}
