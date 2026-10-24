namespace VideoSubtitleTranslator.Cli;

public sealed class GuidedCliInterface
{
    private const string MenuFooter = "↑↓ 导航 | Enter 确认 | Esc 返回 | Q 退出";
    private readonly ICliWorkflow _workflow;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ICliTerminal _terminal;
    private readonly string _currentDirectory;
    private readonly CancellationToken _cancellationToken;

    public GuidedCliInterface(
        ICliWorkflow workflow,
        TextWriter output,
        TextWriter error,
        ICliTerminal terminal,
        string currentDirectory,
        CancellationToken cancellationToken)
    {
        _workflow = workflow;
        _output = output;
        _error = error;
        _terminal = terminal;
        _currentDirectory = currentDirectory;
        _cancellationToken = cancellationToken;
    }

    public async Task<int> RunAsync()
    {
        var selection = 0;
        while (true)
        {
            Draw("zimu", ["翻译视频", "批量加入队列", "任务历史", "退出"], selection, MenuFooter);
            var key = _terminal.ReadKey().Key;
            if (IsQuit(key) || key == ConsoleKey.Escape) return 0;
            if (key == ConsoleKey.UpArrow) selection = (selection + 3) % 4;
            else if (key == ConsoleKey.DownArrow) selection = (selection + 1) % 4;
            else if (key == ConsoleKey.Enter)
            {
                if (selection == 0)
                {
                    if (!await TranslateAsync()) return 0;
                }
                else if (selection == 1)
                {
                    if (!await QueueTranslationsAsync()) return 0;
                }
                else if (selection == 2)
                {
                    if (!await BrowseHistoryAsync()) return 0;
                }
                else return 0;
            }
        }
    }

    private async Task<bool> QueueTranslationsAsync()
    {
        if (_workflow is not ICliQueueWorkflow queueWorkflow)
        {
            Draw("批量翻译", ["当前工作流不支持队列。"], 0, "Enter 返回 | Q 退出");
            return !IsQuit(_terminal.ReadKey().Key);
        }

        var queued = new List<(CliTaskRecord Task, string ExecutionInput)>();
        while (true)
        {
            var rows = queued.Count == 0
                ? new[] { "还没有加入视频。" }
                : queued.Select(item => $"第 {item.Task.QueuePosition} 位  {CliInputSanitizer.Sanitize(item.Task.Input)}").ToArray();
            Draw("批量加入队列", [.. rows, "输入视频 URL 或本地路径："], -1,
                "输入 URL/路径 继续添加 | 空行开始处理 | q 中断队列");
            var input = _terminal.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                if (queued.Count == 0) return true;
                return await RunQueuedBatchAsync(queueWorkflow, queued);
            }
            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
            {
                await queueWorkflow.InterruptQueuedTasksAsync(queued.Select(item => item.Task.Id), _cancellationToken);
                DrawWithoutClear("队列已中断", ["已加入的任务可在任务历史中查看并恢复。"], "Enter 返回 | Q 退出");
                return !IsQuit(_terminal.ReadKey().Key);
            }

            if (!CliApplication.TryParseTranslate([input], _currentDirectory, out var request, out var parseError))
            {
                DrawWithoutClear("无法加入", [parseError], "Enter 继续添加 | Q 退出");
                if (IsQuit(_terminal.ReadKey().Key))
                {
                    await queueWorkflow.InterruptQueuedTasksAsync(queued.Select(item => item.Task.Id), _cancellationToken);
                    return false;
                }
                continue;
            }

            try
            {
                var task = await queueWorkflow.EnqueueTranslationAsync(request!, _cancellationToken);
                queued.Add((task, request!.Input));
            }
            catch (Exception)
            {
                DrawWithoutClear("无法加入", ["任务未能加入队列，请检查输入和工作区设置。"], "Enter 继续 | Q 退出");
                if (IsQuit(_terminal.ReadKey().Key))
                {
                    await queueWorkflow.InterruptQueuedTasksAsync(queued.Select(item => item.Task.Id), _cancellationToken);
                    return false;
                }
            }
        }
    }

    private async Task<bool> RunQueuedBatchAsync(
        ICliQueueWorkflow queueWorkflow,
        IReadOnlyList<(CliTaskRecord Task, string ExecutionInput)> queued)
    {
        var completed = 0;
        var failed = 0;
        foreach (var item in queued.OrderBy(item => item.Task.QueueOrder))
        {
            try
            {
                await queueWorkflow.RunQueuedTaskAsync(item.Task.Id, item.ExecutionInput, snapshot =>
                {
                    Draw("批量翻译", QueueRows(snapshot), -1, "按队列顺序处理 | 批量完成后返回");
                    return Task.CompletedTask;
                }, _cancellationToken);
                completed++;
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                await queueWorkflow.InterruptQueuedTasksAsync(queued.Select(queuedItem => queuedItem.Task.Id), CancellationToken.None);
                DrawWithoutClear("队列已中断", ["未开始的任务已保留在历史中，可逐项恢复。"], "Enter 返回 | Q 退出");
                return !IsQuit(_terminal.ReadKey().Key);
            }
            catch (Exception)
            {
                failed++;
                _error.WriteLine($"任务 {item.Task.Id} 未完成；后续队列任务仍会继续。");
            }

            var current = await queueWorkflow.GetTaskHistoryAsync(_cancellationToken);
            Draw("批量翻译", QueueRows(current), -1, "按队列顺序处理 | 批量完成后返回");
        }

        Draw("批量完成", [$"完成：{completed}", $"失败：{failed}", "播放器未自动打开。"], -1, "Enter 返回 | Q 退出");
        return !IsQuit(_terminal.ReadKey().Key);
    }

    private static IReadOnlyList<string> QueueRows(IReadOnlyList<CliTaskRecord> tasks) => tasks
        .Where(task => task.Status is CliTaskStatus.Queued or CliTaskStatus.Running)
        .OrderBy(task => task.Status == CliTaskStatus.Running ? 0 : 1)
        .ThenBy(task => task.QueuePosition ?? int.MaxValue)
        .Select(task => task.Status == CliTaskStatus.Queued
            ? $"排队第 {task.QueuePosition} 位  {CliInputSanitizer.Sanitize(task.Input)}"
            : $"正在处理  {CliInputSanitizer.Sanitize(task.Input)}")
        .ToArray();

    private async Task<bool> TranslateAsync()
    {
        while (true)
        {
            Draw("翻译视频", ["输入视频 URL 或本地路径："], 0, "Enter 确认 | 空行返回 | 输入 q 退出");
            var input = _terminal.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) return true;
            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase)) return false;

            TranslationResult? translation = null;
            var inputReader = new CliTerminalTextReader(_terminal);
            var status = await CliApplication.RunAsync(
                ["translate", input],
                _workflow,
                _output,
                _error,
                isInteractive: true,
                currentDirectory: _currentDirectory,
                cancellationToken: _cancellationToken,
                standardInput: inputReader,
                onTranslationCompleted: result => translation = result,
                interactivePromptHint: "y 播放 | Enter 返回 | Q 退出");
            if (inputReader.QuitRequested) return false;
            if (status == 0)
                DrawWithoutClear("完成", TranslationLocations(translation), "Enter 返回 | Q 退出");
            else
                DrawWithoutClear("未完成", ["请根据上方提示处理问题后重试。"], "Enter 返回 | Q 退出");

            var key = _terminal.ReadKey().Key;
            if (IsQuit(key)) return false;
            return true;
        }
    }

    private async Task<bool> BrowseHistoryAsync()
    {
        if (_workflow is not ICliTaskWorkflow taskWorkflow)
        {
            Draw("任务历史", ["当前工作流不支持任务历史。"], 0, "Enter 返回 | Q 退出");
            return !IsQuit(_terminal.ReadKey().Key);
        }

        while (true)
        {
            var tasks = await taskWorkflow.GetTaskHistoryAsync(_cancellationToken);
            if (tasks.Count == 0)
            {
                Draw("任务历史", ["还没有翻译任务。"], 0, "Enter 返回 | Esc 返回 | Q 退出");
                var emptyKey = _terminal.ReadKey().Key;
                if (IsQuit(emptyKey)) return false;
                return true;
            }

            var selection = 0;
            while (true)
            {
                var rows = tasks.Select(task =>
                {
                    var queuePosition = task.QueuePosition is int position ? $"队列第 {position} 位 " : string.Empty;
                    return $"{task.Id}  {CliApplication.StatusLabel(task.Status),-6} {queuePosition}{task.Stage,-12} {CliInputSanitizer.Sanitize(task.Input)}";
                }).ToArray();
                Draw("任务历史", rows, selection, MenuFooter);
                var key = _terminal.ReadKey().Key;
                if (IsQuit(key)) return false;
                if (key == ConsoleKey.Escape) return true;
                if (key == ConsoleKey.UpArrow) selection = (selection + tasks.Count - 1) % tasks.Count;
                else if (key == ConsoleKey.DownArrow) selection = (selection + 1) % tasks.Count;
                else if (key == ConsoleKey.Enter)
                {
                    var result = await ShowTaskActionsAsync(tasks[selection], taskWorkflow);
                    if (!result) return false;
                    break;
                }
            }
        }
    }

    private async Task<bool> ShowTaskActionsAsync(CliTaskRecord task, ICliTaskWorkflow taskWorkflow)
    {
        var actions = new List<string>();
        if (task.Status is CliTaskStatus.Interrupted or CliTaskStatus.Failed) actions.Add("恢复任务");
        if (task.Status == CliTaskStatus.Completed && _workflow is ICliPlaybackWorkflow) actions.Add("播放视频");
        actions.Add("返回");
        var selection = 0;

        while (true)
        {
            var details = new[] { $"状态：{CliApplication.StatusLabel(task.Status)}", $"阶段：{task.Stage}", $"输入：{CliInputSanitizer.Sanitize(task.Input)}" };
            Draw($"任务 {task.Id}", [.. details, .. actions], details.Length + selection,
                actions.Count == 1 ? "Enter 返回 | Esc 返回 | Q 退出" : MenuFooter);
            var key = _terminal.ReadKey().Key;
            if (IsQuit(key)) return false;
            if (key == ConsoleKey.Escape) return true;
            if (key == ConsoleKey.UpArrow) selection = (selection + actions.Count - 1) % actions.Count;
            else if (key == ConsoleKey.DownArrow) selection = (selection + 1) % actions.Count;
            else if (key == ConsoleKey.Enter)
            {
                var action = actions[selection];
                if (action == "返回") return true;
                if (action == "恢复任务")
                {
                    await ResumeAsync(task, taskWorkflow);
                    return true;
                }
                if (action == "播放视频")
                {
                    await PlayAsync(task.Id);
                    return true;
                }
            }
        }
    }

    private async Task ResumeAsync(CliTaskRecord task, ICliTaskWorkflow taskWorkflow)
    {
        TranslationResult? resumed = null;
        var result = await CliApplication.RunAsync(
            ["resume", task.Id], _workflow, _output, _error,
            isInteractive: false, currentDirectory: _currentDirectory, cancellationToken: _cancellationToken,
            onTranslationCompleted: value => resumed = value);
        if (result != 0) return;

        var lines = new List<string> { $"任务 {task.Id} 已完成。" };
        lines.AddRange(TranslationLocations(resumed));
        if (taskWorkflow is ICliPlaybackWorkflow playbackWorkflow)
        {
            DrawWithoutClear("恢复完成", lines, "P 播放 | Enter 返回 | Q 退出");
            var key = _terminal.ReadKey().Key;
            if (IsQuit(key)) return;
            if (key == ConsoleKey.P) await PlayAsync(task.Id, playbackWorkflow);
        }
        else DrawWithoutClear("恢复完成", lines, "Enter 返回 | Q 退出");
    }

    private static IReadOnlyList<string> TranslationLocations(TranslationResult? result)
    {
        if (result is null) return ["未能读取字幕产物路径。"];
        var locations = new List<string> { $"中文字幕：{Path.GetFullPath(result.SubtitlePath)}" };
        if (!string.IsNullOrWhiteSpace(result.SidecarSubtitlePath))
            locations.Add($"源视频旁字幕：{Path.GetFullPath(result.SidecarSubtitlePath)}");
        if (!string.IsNullOrWhiteSpace(result.BilingualSubtitlePath))
            locations.Add($"双语字幕：{Path.GetFullPath(result.BilingualSubtitlePath)}");
        if (!string.IsNullOrWhiteSpace(result.BurnedVideoPath))
            locations.Add($"烧录视频：{Path.GetFullPath(result.BurnedVideoPath)}");
        return locations;
    }

    private async Task PlayAsync(string taskId, ICliPlaybackWorkflow? playbackWorkflow = null)
    {
        if (playbackWorkflow is null && _workflow is ICliPlaybackWorkflow availablePlayback)
            playbackWorkflow = availablePlayback;
        if (playbackWorkflow is null) return;

        try
        {
            var result = await playbackWorkflow.PlayTaskAsync(taskId, _cancellationToken);
            CliApplication.WritePlaybackResult(result, _output);
            DrawWithoutClear("播放已启动", [], "Enter 返回 | Q 退出");
        }
        catch (Exception)
        {
            _error.WriteLine("无法启动播放器。请确认视频和字幕文件仍存在，并检查播放器设置后重试。");
            DrawWithoutClear("播放失败", [], "Enter 返回 | Q 退出");
        }
        if (IsQuit(_terminal.ReadKey().Key)) return;
    }

    private void Draw(string title, IReadOnlyList<string> lines, int selection, string footer)
    {
        _terminal.Clear();
        DrawWithoutClear(title, lines, footer, selection);
    }

    private void DrawWithoutClear(string title, IReadOnlyList<string> lines, string footer, int selection = -1)
    {
        _output.WriteLine(title);
        for (var i = 0; i < lines.Count; i++)
            _output.WriteLine(i == selection ? $"> {lines[i]}" : $"  {lines[i]}");
        _output.WriteLine();
        _output.WriteLine(footer);
    }

    private static bool IsQuit(ConsoleKey key) => key == ConsoleKey.Q;

}
