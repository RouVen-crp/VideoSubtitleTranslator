namespace VideoSubtitleTranslator.Tests;

public class ApiKeyPromptTests
{
    [Fact]
    public void Missing_api_key_shows_env_setup_instructions()
    {
        GlobalRuntimeConfig.Current = new TranslatorRuntimeConfig();
        Environment.SetEnvironmentVariable("TEST_API_KEY", null);
        GlobalRuntimeConfig.Current.Llm.ApiKeyEnv = "TEST_API_KEY";

        using var sw = new StringWriter();
        var original = Console.Out;
        Console.SetOut(sw);

        try
        {
            var ex = Assert.Throws<AggregateException>(() =>
            {
                ApiCaller.CallApi("test-model", "system", "user").Wait();
            });

            var output = sw.ToString();
            Assert.Contains(".env", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
