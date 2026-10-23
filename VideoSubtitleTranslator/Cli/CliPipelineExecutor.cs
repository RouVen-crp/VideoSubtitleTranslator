using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Cli;

public sealed class CliPipelineExecutor
{
    private readonly CliTaskStore _store;
    private readonly Func<PipelineStepConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>> _runStep;

    public CliPipelineExecutor(
        CliTaskStore store,
        Func<PipelineStepConfig, Dictionary<string, string>, TranslatorRuntimeConfig, Dictionary<string, string>>? runStep = null)
    {
        _store = store;
        var runner = new PipelineRunner(new PipelineRegistry());
        _runStep = runStep ?? ((step, context, runtime) =>
        {
            GlobalRuntimeConfig.Current = runtime;
            return runner.RunStep(step, context);
        });
    }

    public Dictionary<string, string> Execute(PipelineConfig config, Dictionary<string, string> initialContext,
        TranslatorRuntimeConfig runtime, CliTaskRecord task)
    {
        var context = new Dictionary<string, string>(initialContext, StringComparer.OrdinalIgnoreCase);
        var invalidateFromHere = false;
        for (var index = 0; index < config.Steps.Count; index++)
        {
            var step = config.Steps[index];
            task.Stage = step.Step;
            var artifact = GetStageArtifact(step.Step, context);
            var validCheckpoint = artifact is not null && task.Checkpoints.TryGetValue(step.Step, out var checkpoint) &&
                string.Equals(Path.GetFullPath(artifact), checkpoint.ArtifactPath, StringComparison.OrdinalIgnoreCase) &&
                CliArtifactCheckpoint.IsValid(checkpoint);
            var hasLaterCheckpoint = config.Steps.Skip(index).Any(candidate => task.Checkpoints.ContainsKey(candidate.Step));
            var stageWasCompleted = task.CompletedStages.Contains(step.Step, StringComparer.OrdinalIgnoreCase);
            if (artifact is not null && !validCheckpoint && (hasLaterCheckpoint || stageWasCompleted))
                invalidateFromHere = true;
            if (invalidateFromHere)
            {
                foreach (var downstream in config.Steps.Skip(index))
                {
                    task.Checkpoints.Remove(downstream.Step);
                    task.CompletedStages.RemoveAll(name => string.Equals(name, downstream.Step, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (validCheckpoint && !invalidateFromHere) continue;
            if (artifact is null && stageWasCompleted && !invalidateFromHere) continue;

            _store.Save(task);
            context = _runStep(step, context, runtime);
            task.Context = new Dictionary<string, string>(context, StringComparer.OrdinalIgnoreCase);
            if (task.Context.TryGetValue("Url", out var savedUrl)) task.Context["Url"] = CliInputSanitizer.Sanitize(savedUrl);
            task.CompletedStages.RemoveAll(name => config.Steps.Skip(index).Any(later =>
                string.Equals(later.Step, name, StringComparison.OrdinalIgnoreCase)));
            task.CompletedStages.Add(step.Step);
            artifact = GetStageArtifact(step.Step, context);
            if (artifact is not null && File.Exists(artifact))
                task.Checkpoints[step.Step] = CliArtifactCheckpoint.Create(step.Step, artifact);
            _store.Save(task);
        }
        return context;
    }

    private static string? GetStageArtifact(string stage, IReadOnlyDictionary<string, string> context)
    {
        if (!context.TryGetValue("WorkspacePath", out var workspace) || !context.TryGetValue("VideoExt", out var extension)) return null;
        var dirs = new WorkDirs(workspace, extension);
        return stage switch
        {
            "Download" => dirs.VideoPath,
            "SplitAudio" => dirs.AudioPath,
            "ExtractSubtitle" => dirs.SubtitlePath,
            "BuildSentences" => dirs.RawSubtitlePath,
            "NormalizeSubtitle" => dirs.NormalizedSubtitlePath,
            "Translate" => dirs.TranslatedSubtitlePath,
            "ProofreadSubtitle" => dirs.ProofreadSubtitlePath,
            "CulturalAnnotate" => dirs.CulturalAnnotationsPath,
            "BuildDualSubtitle" => dirs.DualSubtitlePath,
            _ => null
        };
    }
}
