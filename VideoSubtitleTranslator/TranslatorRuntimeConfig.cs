using System.Text.Json;

namespace VideoSubtitleTranslator;

public sealed class TranslatorRuntimeConfig
{
    public string Mode { get; set; } = "standard";
    public string CookiesFromBrowser { get; set; } = string.Empty;
    public LlmConfig Llm { get; set; } = new();
    public TranslationConfig Translation { get; set; } = new();
    public WhisperConfig Whisper { get; set; } = new();
    public BurnConfig Burn { get; set; } = new();

    public static TranslatorRuntimeConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("未找到翻译器配置文件", path);

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<TranslatorRuntimeConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return config ?? new TranslatorRuntimeConfig();
    }
}

public sealed class LlmConfig
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1/chat/completions";
    public string ApiKeyEnv { get; set; } = "DEEPSEEK_API_KEY";
    public string Model { get; set; } = "deepseek-chat";
    public int RetryCount { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 60;
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 8192;
}

public sealed class TranslationConfig
{
    /// <summary>全局主题提示词，会注入标题翻译与正文翻译，帮助模型理解视频语境。</summary>
    public string DomainHintPrompt { get; set; } = string.Empty;
    /// <summary>元翻译指令最大保留条目数，超过后按插入顺序裁剪最旧项。</summary>
    public int MetaRulesMaxCount { get; set; } = 120;
    /// <summary>单条元翻译指令 value 最大字符数（超出截断）。</summary>
    public int MetaRuleMaxChars { get; set; } = 300;
    public int ModuleSentenceCount { get; set; } = 4;
    public int ContextModuleWindow { get; set; } = 2;
    public int RequestDelayMs { get; set; } = 200;
    /// <summary>单模块 JSON 解析失败时的最大重试次数（耗尽后抛异常）。</summary>
    public int ModuleJsonMaxAttempts { get; set; } = 4;
    /// <summary>synopsisFullText 最大字符数（超出截断）。</summary>
    public int SynopsisMaxChars { get; set; } = 12_000;
    /// <summary>字幕规范化步骤每个决策点最大重试次数。</summary>
    public int NormalizeJsonMaxAttempts { get; set; } = 3;
    /// <summary>字幕规范化滚动窗口大小，当前实现仅支持 2（相邻句）。</summary>
    public int NormalizeWindowSize { get; set; } = 2;
    /// <summary>每处理 N 个翻译模块，对前情提要执行一次去重压缩。</summary>
    public int SynopsisCompressEveryModules { get; set; } = 3;
    /// <summary>是否启用翻译后字幕校对。</summary>
    public bool ProofreadEnabled { get; set; } = true;
    /// <summary>字幕后处理校对每个窗口最大 JSON 重试次数。</summary>
    public int ProofreadJsonMaxAttempts { get; set; } = 3;
    /// <summary>字幕后处理窗口大小，当前实现仅支持 2。</summary>
    public int ProofreadWindowSize { get; set; } = 2;
    /// <summary>字幕后处理请求间隔，0 表示沿用 RequestDelayMs。</summary>
    public int ProofreadRequestDelayMs { get; set; } = 0;
    /// <summary>是否启用文化注释步骤（meme 模式下自动开启）。</summary>
    public bool CulturalAnnotateEnabled { get; set; }
}

public sealed class WhisperConfig
{
    public string PythonExecutable { get; set; } = "python3";
    public string Model { get; set; } = "medium";
    public string Language { get; set; } = "en";
    public bool Force { get; set; }
}

public sealed class BurnConfig
{
    public bool Enabled { get; set; } = false;
    public string VideoEncoder { get; set; } = "h264_nvenc";
    /// <summary>烧录质量档位：auto=ffmpeg默认参数，high=尽可能高清。</summary>
    public string QualityPreset { get; set; } = "auto";
    /// <summary>是否保证烧录输出视频码率不低于原视频码率。</summary>
    public bool EnforceSourceBitrateFloor { get; set; } = true;
    /// <summary>附加最小输出码率（kbps），与原视频码率取更大值；0 表示不额外抬高。</summary>
    public int MinOutputBitrateKbps { get; set; } = 0;
}

