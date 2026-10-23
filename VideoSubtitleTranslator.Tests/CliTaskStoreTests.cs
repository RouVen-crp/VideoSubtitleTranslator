using VideoSubtitleTranslator.Cli;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

public sealed class CliTaskStoreTests
{
    [Fact]
    public void Task_identity_and_request_survive_a_new_store_instance()
    {
        using var temp = new TemporaryDirectory();
        var request = new TranslationRequest("https://example.com/video", "standard", temp.Path);
        var created = new CliTaskStore(temp.Path).Create(request);

        var loaded = new CliTaskStore(temp.Path).Get(created.Id);

        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded.Id);
        Assert.Equal(request.Input, loaded.Input);
        Assert.Equal(CliTaskStatus.Queued, loaded.Status);
        Assert.Equal(8, loaded.Id.Length);
    }

    [Fact]
    public void Stored_input_redacts_api_keys_but_preserves_resume_authentication_parameters()
    {
        var sanitized = CliInputSanitizer.Sanitize("https://example.com/video?api_key=secret&token=signed");
        Assert.Contains("api_key=[redacted]", sanitized);
        Assert.Contains("token=signed", sanitized);
        Assert.DoesNotContain("secret", sanitized);
    }

    [Fact]
    public void History_marks_orphaned_running_task_interrupted()
    {
        using var temp = new TemporaryDirectory();
        var store = new CliTaskStore(temp.Path, _ => false);
        var task = store.Create(new TranslationRequest("local.mp4", "meme", temp.Path, true));
        task.Status = CliTaskStatus.Running;
        task.Stage = "Translate";
        task.ProcessId = 999999;
        store.Save(task);

        var history = new CliTaskStore(temp.Path, _ => false).List();

        Assert.Single(history);
        Assert.Equal(CliTaskStatus.Interrupted, history[0].Status);
        Assert.Equal("Interrupted", history[0].Stage);
    }

    [Fact]
    public void Queue_positions_follow_creation_order_and_close_gaps_when_a_task_starts()
    {
        using var temp = new TemporaryDirectory();
        var store = new CliTaskStore(temp.Path);
        var first = store.Create(new TranslationRequest("first.mp4", "standard", temp.Path, true));
        var second = store.Create(new TranslationRequest("second.mp4", "standard", temp.Path, true));
        var third = store.Create(new TranslationRequest("third.mp4", "standard", temp.Path, true));
        first.Status = CliTaskStatus.Running;
        store.Save(first);

        var history = store.List().ToDictionary(task => task.Id);

        Assert.Null(history[first.Id].QueuePosition);
        Assert.Equal(1, history[second.Id].QueuePosition);
        Assert.Equal(2, history[third.Id].QueuePosition);
        Assert.True(first.QueueOrder < second.QueueOrder);
        Assert.True(second.QueueOrder < third.QueueOrder);
    }

    [Fact]
    public void Orphaned_queued_tasks_become_interrupted_for_history_recovery()
    {
        using var temp = new TemporaryDirectory();
        var store = new CliTaskStore(temp.Path);
        store.Create(new TranslationRequest("video.mp4", "standard", temp.Path, true));

        var history = new CliTaskStore(temp.Path, _ => false).List();

        Assert.Equal(CliTaskStatus.Interrupted, Assert.Single(history).Status);
        Assert.Equal("Interrupted", history[0].Stage);
    }

    [Fact]
    public async Task Checkpoint_detects_missing_or_modified_artifacts()
    {
        using var temp = new TemporaryDirectory();
        var artifact = Path.Combine(temp.Path, "stage.srt");
        await File.WriteAllTextAsync(artifact, "subtitle");
        var checkpoint = CliArtifactCheckpoint.Create("NormalizeSubtitle", artifact);

        Assert.True(CliArtifactCheckpoint.IsValid(checkpoint));
        await File.WriteAllTextAsync(artifact, "changed");
        Assert.False(CliArtifactCheckpoint.IsValid(checkpoint));
    }

    [Fact]
    public async Task History_lists_persisted_task_and_resume_warns_about_repeated_model_cost()
    {
        using var temp = new TemporaryDirectory();
        var task = new CliTaskRecord { Id = "a1b2c3d4", Input = "https://example.com/video", Status = CliTaskStatus.Interrupted, Stage = "Translate" };
        var workflow = new FakeTaskWorkflow(task, Path.Combine(temp.Path, "translated.srt"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var historyExit = await CliApplication.RunAsync(["history"], workflow, output, error, false, temp.Path);
        Assert.Equal(0, historyExit);
        Assert.Contains("a1b2c3d4", output.ToString());
        Assert.Contains("已中断", output.ToString());

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        var resumeExit = await CliApplication.RunAsync(["resume", "a1b2c3d4"], workflow, output, error, false, temp.Path);
        Assert.Equal(0, resumeExit);
        Assert.Contains("可能会重新执行并产生费用", error.ToString());
        Assert.Contains("a1b2c3d4", output.ToString());
    }

    [Fact]
    public async Task Resume_reuses_valid_checkpoints_and_restarts_from_first_corrupt_artifact()
    {
        using var temp = new TemporaryDirectory();
        var store = new CliTaskStore(Path.Combine(temp.Path, "tasks"));
        var task = store.Create(new TranslationRequest("https://example.com/video", "standard", temp.Path));
        var steps = new PipelineConfig { Steps = [new() { Step = "Prepare" }, new() { Step = "NormalizeSubtitle" }, new() { Step = "Translate" }] };
        var prepareRuns = 0;
        var normalizeRuns = 0;
        var translateRuns = 0;
        var runner = new CliPipelineExecutor(store, (step, context, _) =>
        {
            if (step.Step == "Prepare") prepareRuns++;
            if (step.Step == "NormalizeSubtitle")
            {
                normalizeRuns++;
                File.WriteAllText(Path.Combine(context["WorkspacePath"], "subtitle.normalized.srt"), "normalized " + normalizeRuns);
            }
            if (step.Step == "Translate")
            {
                translateRuns++;
                File.WriteAllText(Path.Combine(context["WorkspacePath"], "translated_subtitle.srt"), "translated " + translateRuns);
            }
            return context;
        });
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkspacePath"] = temp.Path, ["VideoExt"] = ".mp4", ["Url"] = "https://example.com/video"
        };
        var runtime = new TranslatorRuntimeConfig();

        runner.Execute(steps, context, runtime, task);
        runner.Execute(steps, task.Context, runtime, task);
        Assert.Equal((1, 1, 1), (prepareRuns, normalizeRuns, translateRuns));

        await File.WriteAllTextAsync(Path.Combine(temp.Path, "subtitle.normalized.srt"), "tampered");
        runner.Execute(steps, task.Context, runtime, task);
        Assert.Equal((1, 2, 2), (prepareRuns, normalizeRuns, translateRuns));
    }

    [Fact]
    public void Resume_repeats_an_unfinished_model_stage_after_earlier_checkpoint_is_reused()
    {
        using var temp = new TemporaryDirectory();
        var store = new CliTaskStore(Path.Combine(temp.Path, "tasks"));
        var task = store.Create(new TranslationRequest("https://example.com/video", "standard", temp.Path));
        var steps = new PipelineConfig { Steps = [new() { Step = "NormalizeSubtitle" }, new() { Step = "Translate" }] };
        var normalizeRuns = 0;
        var translateRuns = 0;
        var failFirstTranslation = true;
        var runner = new CliPipelineExecutor(store, (step, context, _) =>
        {
            if (step.Step == "NormalizeSubtitle")
            {
                normalizeRuns++;
                File.WriteAllText(Path.Combine(context["WorkspacePath"], "subtitle.normalized.srt"), "normalized");
            }
            if (step.Step == "Translate")
            {
                translateRuns++;
                if (failFirstTranslation)
                {
                    failFirstTranslation = false;
                    throw new InvalidOperationException("controlled unfinished model request");
                }
                File.WriteAllText(Path.Combine(context["WorkspacePath"], "translated_subtitle.srt"), "translated");
            }
            return context;
        });
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkspacePath"] = temp.Path, ["VideoExt"] = ".mp4"
        };

        Assert.Throws<InvalidOperationException>(() => runner.Execute(steps, context, new TranslatorRuntimeConfig(), task));
        runner.Execute(steps, task.Context, new TranslatorRuntimeConfig(), task);

        Assert.Equal(1, normalizeRuns);
        Assert.Equal(2, translateRuns);
    }

    [Fact]
    public async Task Cli_resume_reloads_real_task_and_reuses_completed_pipeline_stages()
    {
        using var temp = new TemporaryDirectory();
        var videoPath = Path.Combine(temp.Path, "lesson.mp4");
        await File.WriteAllTextAsync(videoPath, "video input");
        var store = new CliTaskStore(Path.Combine(temp.Path, "tasks"));
        var failFirstTranslation = true;
        var normalizeRuns = 0;
        var translateRuns = 0;

        PipelineTranslationWorkflow CreateWorkflow(CliTaskStore taskStore) => new(
            mediaValidator: (_, _) => Task.FromResult<string?>(null),
            taskStore: taskStore,
            pipelineStepRunner: (step, context, _) =>
            {
                var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
                switch (step.Step)
                {
                    case "SplitAudio": File.WriteAllText(dirs.AudioPath, "audio"); break;
                    case "ExtractSubtitle": File.WriteAllText(dirs.SubtitlePath, "raw subtitles"); break;
                    case "BuildSentences": File.WriteAllText(dirs.RawSubtitlePath, "sentences"); break;
                    case "NormalizeSubtitle":
                        normalizeRuns++;
                        File.WriteAllText(dirs.NormalizedSubtitlePath, "normalized");
                        break;
                    case "Translate":
                        translateRuns++;
                        if (failFirstTranslation)
                        {
                            failFirstTranslation = false;
                            throw new InvalidOperationException("controlled model interruption");
                        }
                        File.WriteAllText(dirs.TranslatedSubtitlePath, "translated");
                        break;
                    case "ProofreadSubtitle": File.WriteAllText(dirs.ProofreadSubtitlePath, "proofread"); break;
                }
                return context;
            });

        using var firstOutput = new StringWriter();
        using var firstError = new StringWriter();
        var firstExit = await CliApplication.RunAsync(["translate", videoPath, "--workspace", Path.Combine(temp.Path, "workspace")],
            CreateWorkflow(store), firstOutput, firstError, false, temp.Path);
        Assert.Equal(1, firstExit);
        var persistedTask = Assert.Single(store.List());
        Assert.Equal(CliTaskStatus.Failed, persistedTask.Status);
        Assert.Equal(1, translateRuns);

        using var resumedOutput = new StringWriter();
        using var resumedError = new StringWriter();
        var resumedStore = new CliTaskStore(Path.Combine(temp.Path, "tasks"));
        var resumeExit = await CliApplication.RunAsync(["resume", persistedTask.Id], CreateWorkflow(resumedStore), resumedOutput, resumedError, false, temp.Path);

        Assert.Equal(0, resumeExit);
        Assert.Contains("可能会重新执行并产生费用", resumedError.ToString());
        Assert.Equal(1, normalizeRuns);
        Assert.Equal(2, translateRuns);
        var completedTask = Assert.Single(resumedStore.List());
        Assert.Equal(CliTaskStatus.Completed, completedTask.Status);
        Assert.Contains("proofread", await File.ReadAllTextAsync(completedTask.SubtitlePath!));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }

    private sealed class FakeTaskWorkflow(CliTaskRecord task, string subtitlePath) : ICliWorkflow, ICliTaskWorkflow
    {
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TranslationResult(subtitlePath));
        public Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DownloadResult(string.Empty));
        public Task<IReadOnlyList<CliTaskRecord>> GetTaskHistoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CliTaskRecord>>([task]);
        public async Task<TranslationResult> ResumeTaskAsync(string id, CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(subtitlePath, "subtitle", cancellationToken);
            return new TranslationResult(subtitlePath, TaskId: id);
        }
    }
}
