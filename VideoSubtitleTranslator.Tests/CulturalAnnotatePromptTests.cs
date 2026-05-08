namespace VideoSubtitleTranslator.Tests;

public class CulturalAnnotatePromptTests
{
    [Fact]
    public void System_prompt_exists_and_contains_culture_keywords()
    {
        var prompt = PromptProvider.Get("CulturalAnnotate/system_prompt.md");

        Assert.NotEmpty(prompt);
        Assert.Contains("文化背景", prompt);
    }

    [Fact]
    public void User_prompt_template_exists_and_contains_placeholders()
    {
        var prompt = PromptProvider.Get("CulturalAnnotate/user_prompt_template.md");

        Assert.NotEmpty(prompt);
        Assert.Contains("CULTURAL_CONTEXT", prompt);
    }
}
