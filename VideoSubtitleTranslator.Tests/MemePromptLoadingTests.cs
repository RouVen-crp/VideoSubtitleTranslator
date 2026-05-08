namespace VideoSubtitleTranslator.Tests;

public class MemePromptLoadingTests
{
    [Fact]
    public void Module_system_prompt_for_meme_exists()
    {
        var prompt = PromptProvider.Get("Translate/Meme/module_system_prompt.md");

        Assert.NotEmpty(prompt);
        Assert.Contains("短视频", prompt);
    }

    [Fact]
    public void Module_user_prompt_template_for_meme_exists()
    {
        var prompt = PromptProvider.Get("Translate/Meme/module_user_prompt_template.md");

        Assert.NotEmpty(prompt);
        Assert.Contains("翻译风格要求", prompt);
    }

    [Fact]
    public void Title_system_prompt_for_meme_exists()
    {
        var prompt = PromptProvider.Get("Translate/Meme/title_system_prompt.md");

        Assert.NotEmpty(prompt);
    }

    [Fact]
    public void Repair_system_prompt_for_meme_exists()
    {
        var prompt = PromptProvider.Get("Translate/Meme/repair_system_prompt.md");

        Assert.NotEmpty(prompt);
    }

    [Fact]
    public void Internal_ad_policy_prompt_for_meme_exists()
    {
        var prompt = PromptProvider.Get("Translate/Meme/internal_ad_policy_prompt.md");

        Assert.NotEmpty(prompt);
    }

    [Fact]
    public void Meme_step_has_different_prompt_than_standard()
    {
        var standardPrompt = PromptProvider.Get("Translate/module_system_prompt.md");
        var memePrompt = PromptProvider.Get("Translate/Meme/module_system_prompt.md");

        Assert.NotEqual(standardPrompt.Trim(), memePrompt.Trim());
    }
}
