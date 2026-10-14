using System.Security.Cryptography;
using System.Text;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Workbench;

public sealed record PipelineProgress(string Stage, int ProcessedItems, int TotalItems);

public sealed class PipelineExecutor
{
    private readonly WorkbenchStore _store;

    public PipelineExecutor(WorkbenchStore store) => _store = store;

    public async Task<SubtitleDocument> ExecuteAsync(WorkbenchTask task, Action<PipelineProgress> progress, CancellationToken cancellationToken)
    {
        var dirs = new WorkDirs(task.WorkspacePath, ".mp4");
        Directory.CreateDirectory(task.WorkspacePath);
        var inputPath = Path.Combine(task.WorkspacePath, "input.mp4");
        if (!File.Exists(inputPath)) throw new FileNotFoundException("任务输入文件不存在", inputPath);
        EnsureCommittedInput(inputPath, dirs.VideoPath);

        var configPath = Path.Combine(AppContext.BaseDirectory, "pipeline.web.config.json");
        var translatorPath = Path.Combine(AppContext.BaseDirectory, "Config", "translator.config.json");
        var translatorConfig = TranslatorRuntimeConfig.Load(translatorPath);
        translatorConfig.Mode = "standard";
        GlobalRuntimeConfig.Current = translatorConfig;
        var runner = new PipelineRunner(new PipelineRegistry());
        var config = PipelineRunner.LoadConfig(configPath);
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Url"] = inputPath,
            ["WorkspaceRoot"] = Directory.GetParent(task.WorkspacePath)?.FullName ?? task.WorkspacePath,
            ["WorkspacePath"] = task.WorkspacePath,
            ["VideoExt"] = ".mp4",
            ["VideoTitle"] = task.Id,
            ["VideoAuthor"] = string.Empty,
            ["VideoUploadDate"] = string.Empty,
            ["Mode"] = "standard"
        };

        foreach (var step in config.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress(new PipelineProgress(step.Step, 0, task.TotalItems));
            context = await Task.Run(() => runner.RunStep(step, context), cancellationToken);
            var artifact = FindArtifact(dirs, step.Step);
            if (artifact is null || !File.Exists(artifact))
                throw new InvalidDataException($"阶段 {step.Step} 未生成有效产物。");
            _store.AddStage(task.Id, step.Step, artifact, ComputeSha256(artifact));
            progress(new PipelineProgress(step.Step, step.Step == "BuildSentences" ? CountLines(dirs.RawSubtitlePath) : 0, task.TotalItems));
        }

        var source = File.Exists(dirs.NormalizedSubtitlePath) ? dirs.NormalizedSubtitlePath : dirs.SubtitlePath;
        var translated = File.Exists(dirs.ProofreadSubtitlePath) ? dirs.ProofreadSubtitlePath : dirs.TranslatedSubtitlePath;
        var sourceItems = SrtCodec.Read(source);
        var translatedItems = SrtCodec.Read(translated);
        if (sourceItems.Count == 0 || translatedItems.Count == 0)
            throw new InvalidDataException("字幕阶段未生成可编辑字幕。");
        var items = new List<SubtitleItem>();
        for (var i = 0; i < sourceItems.Count; i++)
        {
            var sourceItem = sourceItems[i];
            var translatedItem = i < translatedItems.Count ? translatedItems[i] : new SubtitleItem();
            items.Add(new SubtitleItem
            {
                StartMs = sourceItem.StartMs,
                EndMs = sourceItem.EndMs,
                SourceText = sourceItem.SourceText,
                TranslatedText = translatedItem.SourceText
            });
        }

        var current = _store.GetSubtitles(task.Id);
        if (current is null)
        {
            if (!_store.TrySaveSubtitles(task.Id, 0, items, out current) || current is null)
                throw new InvalidOperationException("无法保存初始字幕版本。");
        }
        progress(new PipelineProgress("AwaitingReview", current.Items.Count, current.Items.Count));
        return current;
    }

    private static void EnsureCommittedInput(string inputPath, string videoPath)
    {
        if (File.Exists(videoPath)) return;
        var temporary = videoPath + ".tmp";
        File.Copy(inputPath, temporary, true);
        File.Move(temporary, videoPath, true);
    }

    private static string? FindArtifact(WorkDirs dirs, string step) => step switch
    {
        "SplitAudio" => dirs.AudioPath,
        "ExtractSubtitle" => dirs.SubtitlePath,
        "BuildSentences" => dirs.RawSubtitlePath,
        "NormalizeSubtitle" => dirs.NormalizedSubtitlePath,
        "Translate" => dirs.TranslatedSubtitlePath,
        "ProofreadSubtitle" => File.Exists(dirs.ProofreadSubtitlePath) ? dirs.ProofreadSubtitlePath : dirs.TranslatedSubtitlePath,
        _ => null
    };

    private static int CountLines(string path) => File.Exists(path) ? File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line)) : 0;

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
