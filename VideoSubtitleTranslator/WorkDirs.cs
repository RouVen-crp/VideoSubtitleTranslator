namespace VideoSubtitleTranslator;

public class WorkDirs(string workspacePath, string videoExt)
{
    private readonly string _workspacePath = workspacePath;
    private readonly string _videoExt = videoExt;
    
    public string CustomPath(string name) => Path.Combine(_workspacePath, name);
    
    public string ThumbnailPath => Path.Combine(_workspacePath, "thumbnail");
    public string VideoPath => Path.Combine(_workspacePath, $"video{_videoExt}");
    public string AudioPath => Path.Combine(_workspacePath, "audio.wav");
    public string WordSubtitlePath => Path.Combine(_workspacePath, "word_subtitle.srt");
    public string RawSubtitlePath => Path.Combine(_workspacePath, "raw_subtitle.txt");
    public string SubtitlePath => Path.Combine(_workspacePath, "subtitle.srt");
    public string NormalizedSubtitlePath => Path.Combine(_workspacePath, "subtitle.normalized.srt");
    public string NormalizedRawSubtitlePath => Path.Combine(_workspacePath, "raw_subtitle.normalized.txt");
    public string SubtitleNormalizeLogPath => Path.Combine(_workspacePath, "subtitle_normalize.log");
    public string TranslatedSubtitlePath => Path.Combine(_workspacePath, "translated_subtitle.srt");
    public string ProofreadSubtitlePath => Path.Combine(_workspacePath, "translated_subtitle.proofread.srt");
    public string TranslatedTitlePath => Path.Combine(_workspacePath, "translated_title.txt");
    public string RawTranslatedSubtitlePath => Path.Combine(_workspacePath, "raw_translated_subtitle.txt");
    public string SubtitleProofreadLogPath => Path.Combine(_workspacePath, "subtitle_proofread.log");
    public string TermTablePath => Path.Combine(_workspacePath, "term_consistency_table.txt");
    /// <summary>本视频广告概括记忆（逐轮追加），供后续轮次识别广告语境。</summary>
    public string AdMemoryPath => Path.Combine(_workspacePath, "ad_memory.txt");
    /// <summary>元翻译指令（key\tvalue）持久化文件。</summary>
    public string MetaRulesPath => Path.Combine(_workspacePath, "meta_translation_rules.txt");
    public string SynopsisPath => Path.Combine(_workspacePath, "synopsis_memory.txt");
    public string MemoryHistoryPath => Path.Combine(_workspacePath, "memory_history.log");
    public string PromptHistoryPath => Path.Combine(_workspacePath, "prompt_history.log");
    public string InfoPath => Path.Combine(_workspacePath, "info.txt");
}