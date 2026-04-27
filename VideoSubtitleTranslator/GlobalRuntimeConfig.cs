namespace VideoSubtitleTranslator;

public static class GlobalRuntimeConfig
{
    public static TranslatorRuntimeConfig Current { get; set; } = new();
}
