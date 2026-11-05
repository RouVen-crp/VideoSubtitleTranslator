using VideoSubtitleTranslator.Cli;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

/// <summary>
/// 队列集成测试：真实 <see cref="CliTaskStore"/> + 真实 <see cref="PipelineTranslationWorkflow.RunQueuedTaskAsync"/>，
/// 只把外部媒体工具和流水线步骤替换为受控替身，验证 Queue Position 语义和 FIFO 顺序。
/// </summary>
public sealed class CliQueueIntegrationTests
{
    [Fact]
    public async Task RunQueuedTaskAsync_uses_the_real_store_and_closes_queue_positions_in_fifo_order()
    {
        using var temp = new TemporaryDirectory();
        var store = new CliTaskStore(Path.Combine(temp.Path, "tasks"), _ => true);
        var workspaceRoot = Path.Combine(temp.Path, "workspace");
        var workflow = new PipelineTranslationWorkflow(
            mediaValidator: (_, _) => Task.FromResult<string?>(null),
            taskStore: store,
            pipelineStepRunner: (step, context, _) =>
            {
                var dirs = new WorkDirs(context["WorkspacePath"], context["VideoExt"]);
                switch (step.Step)
                {
                    case "Translate":
                        File.WriteAllText(dirs.TranslatedSubtitlePath, "翻译");
                        break;
                    case "ProofreadSubtitle":
                        File.WriteAllText(dirs.ProofreadSubtitlePath, "校对");
                        break;
                }
                return context;
            },
            progressOutput: TextWriter.Null);

        var inputs = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            var input = Path.Combine(temp.Path, $"video-{index}.mp4");
            await File.WriteAllTextAsync(input, "video");
            inputs.Add(input);
        }

        var first = await workflow.EnqueueTranslationAsync(
            new TranslationRequest(inputs[0], "standard", workspaceRoot, IsLocalFile: true));
        var second = await workflow.EnqueueTranslationAsync(
            new TranslationRequest(inputs[1], "standard", workspaceRoot, IsLocalFile: true));
        var third = await workflow.EnqueueTranslationAsync(
            new TranslationRequest(inputs[2], "standard", workspaceRoot, IsLocalFile: true));

        // EnqueueTranslationAsync 返回的 Queue Position 与真实 store 一致。
        Assert.Equal(1, first.QueuePosition);
        Assert.Equal(2, second.QueuePosition);
        Assert.Equal(3, third.QueuePosition);

        IReadOnlyList<CliTaskRecord>? snapshotOnStart = null;
        var firstResult = await workflow.RunQueuedTaskAsync(first.Id, inputs[0], tasks =>
        {
            snapshotOnStart = tasks;
            return Task.CompletedTask;
        });

        Assert.True(File.Exists(firstResult.SubtitlePath));
        Assert.NotNull(snapshotOnStart);
        var started = snapshotOnStart!.ToDictionary(task => task.Id);
        // 第一个任务开始时已不是排队状态，队列位置关闭：第二、第三个各前移一位。
        Assert.Equal(CliTaskStatus.Running, started[first.Id].Status);
        Assert.Null(started[first.Id].QueuePosition);
        Assert.Equal(1, started[second.Id].QueuePosition);
        Assert.Equal(2, started[third.Id].QueuePosition);

        var afterFirst = store.List().ToDictionary(task => task.Id);
        Assert.Equal(CliTaskStatus.Completed, afterFirst[first.Id].Status);
        Assert.Null(afterFirst[first.Id].QueuePosition);
        Assert.Equal(1, afterFirst[second.Id].QueuePosition);
        Assert.Equal(2, afterFirst[third.Id].QueuePosition);
        Assert.True(afterFirst[first.Id].QueueOrder < afterFirst[second.Id].QueueOrder);
        Assert.True(afterFirst[second.Id].QueueOrder < afterFirst[third.Id].QueueOrder);

        // 已完成的任务不能再次作为排队任务启动。
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflow.RunQueuedTaskAsync(first.Id, inputs[0]));
        Assert.Contains("排队中", exception.Message);

        IReadOnlyList<CliTaskRecord>? snapshotBeforeSecond = null;
        var secondResult = await workflow.RunQueuedTaskAsync(second.Id, inputs[1], tasks =>
        {
            snapshotBeforeSecond = tasks;
            return Task.CompletedTask;
        });

        Assert.True(File.Exists(secondResult.SubtitlePath));
        Assert.NotNull(snapshotBeforeSecond);
        var beforeSecond = snapshotBeforeSecond!.ToDictionary(task => task.Id);
        Assert.Null(beforeSecond[second.Id].QueuePosition);
        Assert.Equal(1, beforeSecond[third.Id].QueuePosition);

        var thirdResult = await workflow.RunQueuedTaskAsync(third.Id, inputs[2]);

        Assert.True(File.Exists(thirdResult.SubtitlePath));
        var history = await workflow.GetTaskHistoryAsync();
        var completed = history.Where(task => task.Status == CliTaskStatus.Completed)
            .OrderBy(task => task.QueueOrder).Select(task => task.Id).ToArray();
        Assert.Equal([first.Id, second.Id, third.Id], completed);
        Assert.All(history, task => Assert.Null(task.QueuePosition));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
