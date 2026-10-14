using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Workbench;

public sealed record WorkCommand(string TaskId, WorkKind Kind, int ExpectedRevision = 0, string? SubtitleId = null, bool Bilingual = false, string? OperationId = null);

public sealed class WorkbenchWorker : BackgroundService
{
    private readonly Channel<WorkCommand> _commands = Channel.CreateUnbounded<WorkCommand>();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
    private readonly WorkbenchStore _store;
    private readonly PipelineExecutor _pipeline;
    private readonly MediaExporter _exporter;

    public WorkbenchWorker(WorkbenchStore store, WorkbenchOptions options)
    {
        _store = store;
        _pipeline = new PipelineExecutor(store, options);
        _exporter = new MediaExporter(options.FfmpegPath);
    }

    public ValueTask EnqueueAsync(WorkCommand command, CancellationToken cancellationToken = default) => _commands.Writer.WriteAsync(command, cancellationToken);

    public bool Cancel(string taskId)
    {
        var task = _store.GetTask(taskId);
        if (task is null || task.Status is not (WorkbenchTaskStatus.Queued or WorkbenchTaskStatus.Processing or WorkbenchTaskStatus.ExportQueued or WorkbenchTaskStatus.Exporting)) return false;
        task.Status = WorkbenchTaskStatus.Cancelled;
        task.Stage = "Cancelled";
        task.Error = "任务已取消；已发送给提供商的 API 请求仍可能完成并计费。";
        _store.SaveTask(task);
        if (_running.TryGetValue(taskId, out var source)) source.Cancel();
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _store.MarkRunningOperationsAsFailed();
        _store.MarkProcessingAsInterrupted();
        foreach (var queued in _store.FindByStatus(WorkbenchTaskStatus.Queued))
            await EnqueueAsync(new WorkCommand(queued.Id, WorkKind.Pipeline, OperationId: queued.OperationId), stoppingToken);
        foreach (var pending in _store.FindPendingOperations(WorkKind.Export))
            await EnqueueAsync(new WorkCommand(pending.TaskId, pending.Kind, pending.ExpectedRevision, pending.SubtitleId, pending.Bilingual, pending.OperationId), stoppingToken);

        await foreach (var command in _commands.Reader.ReadAllAsync(stoppingToken))
        {
            var task = _store.GetTask(command.TaskId);
            if (task is null) continue;
            if (task.Status == WorkbenchTaskStatus.Cancelled)
            {
                if (!string.IsNullOrWhiteSpace(command.OperationId)) _store.MarkOperationFailed(command.OperationId, "任务已取消。");
                continue;
            }
            await ProcessOneAsync(command, stoppingToken);
        }
    }

    private async Task ProcessOneAsync(WorkCommand command, CancellationToken stoppingToken)
    {
        var task = _store.GetTask(command.TaskId);
        if (task is null || task.Status == WorkbenchTaskStatus.Cancelled) return;
        if (!string.IsNullOrWhiteSpace(command.OperationId)) _store.MarkOperationRunning(command.OperationId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _running[task.Id] = linked;
        var started = DateTimeOffset.UtcNow;
        var succeeded = false;
        try
        {
            switch (command.Kind)
            {
                case WorkKind.Pipeline: await RunPipelineAsync(task, linked.Token); break;
                case WorkKind.Retranslate: await RunRetranslationAsync(task, command, linked.Token); break;
                case WorkKind.Export: await RunExportAsync(task, command, linked.Token); break;
                default: throw new InvalidOperationException($"未知工作命令：{command.Kind}");
            }
            succeeded = true;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (task.Status != WorkbenchTaskStatus.Cancelled)
            {
                task.Status = WorkbenchTaskStatus.Cancelled;
                task.Stage = "Cancelled";
                task.Error = "任务已取消；已发送给提供商的 API 请求仍可能完成并计费。";
                _store.SaveTask(task);
            }
        }
        catch (Exception ex)
        {
            task.Status = WorkbenchTaskStatus.Failed;
            task.Stage = "Failed";
            task.Error = ex.Message;
            _store.SaveTask(task);
        }
        finally
        {
            task.ElapsedSeconds += (DateTimeOffset.UtcNow - started).TotalSeconds;
            if (task.Status is WorkbenchTaskStatus.Failed or WorkbenchTaskStatus.Cancelled)
                _store.SaveTask(task);
            if (!string.IsNullOrWhiteSpace(command.OperationId))
            {
                if (succeeded) _store.MarkOperationCompleted(command.OperationId);
                else _store.MarkOperationFailed(command.OperationId, task.Error ?? "操作未完成。");
            }
            _running.TryRemove(task.Id, out _);
        }
    }

    private async Task RunPipelineAsync(WorkbenchTask task, CancellationToken cancellationToken)
    {
        task.Status = WorkbenchTaskStatus.Processing;
        task.Error = null;
        _store.SaveTask(task);
        var document = await _pipeline.ExecuteAsync(task, progress =>
        {
            if (cancellationToken.IsCancellationRequested) return;
            task.Stage = progress.Stage;
            task.ProcessedItems = progress.ProcessedItems;
            task.TotalItems = Math.Max(task.TotalItems, progress.TotalItems);
            _store.SaveTask(task);
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_store.TryCompletePipeline(task.Id, document.Revision, document.Items.Count))
            throw new OperationCanceledException(cancellationToken);
        task.Revision = document.Revision;
        task.ProcessedItems = document.Items.Count;
        task.TotalItems = document.Items.Count;
        task.Status = WorkbenchTaskStatus.AwaitingReview;
        task.Stage = "AwaitingReview";
    }

    private async Task RunRetranslationAsync(WorkbenchTask task, WorkCommand command, CancellationToken cancellationToken)
    {
        var document = _store.GetSubtitles(task.Id) ?? throw new InvalidOperationException("字幕版本不存在。");
        if (document.Revision != command.ExpectedRevision) throw new InvalidOperationException("字幕已被修改，请刷新后重试单句翻译。");
        var item = document.Items.FirstOrDefault(i => i.Id == command.SubtitleId) ?? throw new InvalidOperationException("字幕条目不存在。");
        task.Status = WorkbenchTaskStatus.Processing;
        task.Stage = "Retranslate";
        _store.SaveTask(task);
        var config = TranslatorRuntimeConfig.Load(Path.Combine(AppContext.BaseDirectory, "Config", "translator.config.json"));
        GlobalRuntimeConfig.Current = config;
        var systemPrompt = PromptProvider.Get("Translate/repair_system_prompt.md");
        var userPrompt = PromptProvider.Get("Translate/repair_user_prompt_template.md")
            .Replace("{{COUNT}}", "1", StringComparison.Ordinal)
            .Replace("{{LINES}}", item.SourceText, StringComparison.Ordinal);
        var raw = await ApiCaller.CallApi(config.Llm.Model, systemPrompt, userPrompt, cancellationToken);
        using var json = JsonDocument.Parse(ExtractJson(raw));
        var translation = json.RootElement.GetProperty("translations")[0].GetString();
        if (string.IsNullOrWhiteSpace(translation)) throw new InvalidDataException("模型未返回有效的单句翻译。");
        cancellationToken.ThrowIfCancellationRequested();
        if (!_store.TryApplyTranslation(task.Id, item.Id, command.ExpectedRevision, translation, out var revision))
            throw new InvalidOperationException("单句翻译返回过晚；较新的人工修改已得到保护。");
        task.Revision = revision;
        task.Status = WorkbenchTaskStatus.AwaitingReview;
        task.Stage = "AwaitingReview";
    }

    private async Task RunExportAsync(WorkbenchTask task, WorkCommand command, CancellationToken cancellationToken)
    {
        var document = _store.GetSubtitles(task.Id) ?? throw new InvalidOperationException("字幕版本不存在。");
        if (document.Revision != command.ExpectedRevision) throw new InvalidOperationException("字幕已更新，请重新选择要导出的版本。");
        task.Status = WorkbenchTaskStatus.Exporting;
        task.Stage = "Exporting";
        task.Error = null;
        _store.SaveTask(task);
        var export = await _exporter.ExportAsync(task, document, command.Bilingual, cancellationToken);
        _store.AddExport(export);
        task.Status = WorkbenchTaskStatus.Completed;
        task.Stage = "Completed";
        task.Error = null;
        _store.SaveTask(task);
    }

    private static string ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }
}
