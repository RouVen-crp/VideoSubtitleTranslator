using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;

namespace VideoSubtitleTranslator.Workbench;

public static class WorkbenchHost
{
    public static async Task RunAsync(string[] args)
    {
        var options = WorkbenchOptions.FromEnvironment();
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.Port}");
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = options.MaxUploadBytes);
        builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = options.MaxUploadBytes);
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<WorkbenchStore>();
        builder.Services.AddSingleton<WorkbenchWorker>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<WorkbenchWorker>());

        var app = builder.Build();
        var webRoot = Path.Combine(AppContext.BaseDirectory, "Web");
        if (Directory.Exists(webRoot))
        {
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(webRoot) });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(webRoot) });
        }
        WorkbenchEndpoints.Map(app, options, app.Services.GetRequiredService<WorkbenchStore>(), app.Services.GetRequiredService<WorkbenchWorker>());
        app.MapFallback(async context =>
        {
            var index = Path.Combine(webRoot, "index.html");
            if (File.Exists(index)) await context.Response.SendFileAsync(index);
            else context.Response.StatusCode = StatusCodes.Status404NotFound;
        });
        await app.RunAsync();
    }
}

internal static class WorkbenchEndpoints
{
    public static void Map(WebApplication app, WorkbenchOptions options, WorkbenchStore store, WorkbenchWorker worker)
    {
        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            bindAddress = options.BindAddress,
            tools = new { ffprobe = ToolAvailable("ffprobe"), ffmpeg = ToolAvailable("ffmpeg"), python = ToolAvailable("python3") },
            configuration = ConfigurationSummary()
        }));

        app.MapGet("/api/tasks", () => Results.Ok(store.ListTasks()));
        app.MapGet("/api/tasks/{taskId}", (string taskId) =>
        {
            var task = store.GetTask(taskId);
            return task is null ? Results.NotFound(new ApiError("task_not_found", "任务不存在。")) : Results.Ok(task);
        });

        app.MapGet("/api/tasks/{taskId}/source", (string taskId) =>
        {
            var task = store.GetTask(taskId);
            var path = task is null ? null : Path.Combine(task.WorkspacePath, "input.mp4");
            if (task is null || path is null || !IsInside(task.WorkspacePath, path) || !File.Exists(path))
                return Results.NotFound(new ApiError("source_not_ready", "原视频尚未可用。"));
            return Results.File(path, "video/mp4", enableRangeProcessing: true);
        });

        app.MapPost("/api/tasks", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new ApiError("file_required", "请选择一个英文 MP4 文件。"));
            if (file.Length > options.MaxUploadBytes)
                return Results.BadRequest(new ApiError("file_too_large", "文件超过 500 MiB 大小限制。"));

            var operationId = request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(operationId)) operationId = Guid.NewGuid().ToString("N");
            var existing = store.GetTaskByOperationId(operationId);
            if (existing is not null) return Results.Accepted($"/api/tasks/{existing.Id}", existing);

            var id = Guid.NewGuid().ToString("N");
            var workspace = Path.Combine(options.DataDirectory, "tasks", id);
            Directory.CreateDirectory(workspace);
            var input = Path.Combine(workspace, "input.mp4");
            try
            {
                await using (var stream = File.Create(input)) await file.CopyToAsync(stream, cancellationToken);
                var probe = await new MediaProbe().ValidateAsync(input, cancellationToken);
                if (!probe.IsValid)
                {
                    TryDeleteDirectory(workspace);
                    return Results.BadRequest(new ApiError("invalid_media", probe.Error ?? "媒体文件无法接受。"));
                }

                var task = new WorkbenchTask
                {
                    Id = id,
                    SourceFileName = Path.GetFileName(file.FileName),
                    WorkspacePath = workspace,
                    OperationId = operationId,
                    Status = WorkbenchTaskStatus.Queued,
                    Stage = "Queued",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                if (!store.TryCreateTask(task))
                {
                    TryDeleteDirectory(workspace);
                    existing = store.GetTaskByOperationId(operationId);
                    return existing is null ? Results.Conflict(new ApiError("duplicate_operation", "上传操作已存在，但任务状态暂不可读取。")) : Results.Accepted($"/api/tasks/{existing.Id}", existing);
                }
                store.TryBeginOperation(operationId, id, WorkKind.Pipeline);
                await worker.EnqueueAsync(new WorkCommand(id, WorkKind.Pipeline, OperationId: operationId), cancellationToken);
                return Results.Accepted($"/api/tasks/{id}", task);
            }
            catch
            {
                TryDeleteDirectory(workspace);
                throw;
            }
        });

        app.MapPost("/api/tasks/{taskId}/cancel", (string taskId) =>
        {
            var task = store.GetTask(taskId);
            if (task is null) return Results.NotFound(new ApiError("task_not_found", "任务不存在。"));
            return worker.Cancel(taskId) ? Results.Ok(store.GetTask(taskId)) : Results.Conflict(new ApiError("cannot_cancel", "任务当前不能取消。"));
        });

        app.MapPost("/api/tasks/{taskId}/resume", async (string taskId, CancellationToken cancellationToken) =>
        {
            var task = store.GetTask(taskId);
            if (task is null) return Results.NotFound(new ApiError("task_not_found", "任务不存在。"));
            if (task.Status is not (WorkbenchTaskStatus.Interrupted or WorkbenchTaskStatus.Failed))
                return Results.Conflict(new ApiError("cannot_resume", "只有中断或失败任务可以恢复。"));
            var operationId = $"resume:{taskId}:{task.Revision}:{Guid.NewGuid():N}";
            store.TryBeginOperation(operationId, taskId, WorkKind.Pipeline);
            task.Status = WorkbenchTaskStatus.Queued;
            task.Stage = "Queued";
            task.Error = "恢复可能重试未提交完成的翻译请求，并产生新的 API 费用。";
            store.SaveTask(task);
            await worker.EnqueueAsync(new WorkCommand(taskId, WorkKind.Pipeline, task.Revision, OperationId: operationId), cancellationToken);
            return Results.Accepted($"/api/tasks/{taskId}", task);
        });

        app.MapGet("/api/tasks/{taskId}/subtitles", (string taskId) =>
        {
            if (store.GetTask(taskId) is null) return Results.NotFound(new ApiError("task_not_found", "任务不存在。"));
            return store.GetSubtitles(taskId) is { } document ? Results.Ok(document) : Results.NotFound(new ApiError("subtitle_not_ready", "字幕尚未生成。"));
        });

        app.MapPut("/api/tasks/{taskId}/subtitles", (string taskId, SaveSubtitleRequest request) =>
        {
            var task = store.GetTask(taskId);
            var current = store.GetSubtitles(taskId);
            if (task is null || current is null) return Results.NotFound(new ApiError("subtitle_not_ready", "字幕尚未生成。"));
            if (request.Items.Count != current.Items.Count || request.Items.Any(i => i.StartMs < 0 || i.EndMs <= i.StartMs))
                return Results.BadRequest(new ApiError("invalid_subtitles", "字幕条目数量或时间戳无效；时间戳在首版中只读。"));
            var original = current.Items.ToDictionary(i => i.Id);
            foreach (var item in request.Items)
            {
                if (!original.TryGetValue(item.Id, out var prior) || prior.StartMs != item.StartMs || prior.EndMs != item.EndMs)
                    return Results.BadRequest(new ApiError("timestamps_read_only", "首版不允许修改字幕时间戳。"));
                item.NeedsReview = item.NeedsReview || !string.Equals(item.SourceText, prior.SourceText, StringComparison.Ordinal);
            }
            if (!store.TrySaveSubtitles(taskId, request.ExpectedRevision, request.Items, out var saved) || saved is null)
                return Results.Conflict(new ApiError("revision_conflict", "字幕已被其他操作修改，请刷新后再保存。"));
            task = store.GetTask(taskId)!;
            task.Status = WorkbenchTaskStatus.AwaitingReview;
            task.Stage = "AwaitingReview";
            store.SaveTask(task);
            return Results.Ok(saved);
        });

        app.MapPost("/api/tasks/{taskId}/subtitles/retranslate", async (string taskId, RetranslateRequest request, CancellationToken cancellationToken) =>
        {
            var task = store.GetTask(taskId);
            var document = store.GetSubtitles(taskId);
            if (task is null || document is null) return Results.NotFound(new ApiError("subtitle_not_ready", "字幕尚未生成。"));
            if (document.Revision != request.ExpectedRevision || document.Items.All(i => i.Id != request.SubtitleId))
                return Results.Conflict(new ApiError("revision_conflict", "字幕已被修改，请刷新后重试。"));
            var operationId = string.IsNullOrWhiteSpace(request.OperationId) ? $"retranslate:{taskId}:{request.SubtitleId}:{request.ExpectedRevision}" : request.OperationId;
            if (store.TryBeginOperation(operationId, taskId, WorkKind.Retranslate, request.ExpectedRevision, request.SubtitleId))
                await worker.EnqueueAsync(new WorkCommand(taskId, WorkKind.Retranslate, request.ExpectedRevision, request.SubtitleId, OperationId: operationId), cancellationToken);
            return Results.Accepted($"/api/tasks/{taskId}", new { operationId, taskId, request.ExpectedRevision });
        });

        app.MapGet("/api/tasks/{taskId}/exports", (string taskId) =>
        {
            var task = store.GetTask(taskId);
            if (task is null) return Results.NotFound(new ApiError("task_not_found", "任务不存在。"));
            return Results.Ok(store.ListExports(taskId).Select(export => new { export, stale = export.Revision != task.Revision }));
        });

        app.MapPost("/api/tasks/{taskId}/exports", async (string taskId, ExportRequest request, CancellationToken cancellationToken) =>
        {
            var task = store.GetTask(taskId);
            var document = store.GetSubtitles(taskId);
            if (task is null || document is null) return Results.NotFound(new ApiError("subtitle_not_ready", "字幕尚未生成。"));
            if (document.Revision != request.ExpectedRevision) return Results.Conflict(new ApiError("revision_conflict", "字幕已更新，请重新选择导出版本。"));
            var operationId = string.IsNullOrWhiteSpace(request.OperationId) ? $"export:{taskId}:{request.ExpectedRevision}:{request.Bilingual}" : request.OperationId;
            if (store.TryBeginOperation(operationId, taskId, WorkKind.Export, request.ExpectedRevision, bilingual: request.Bilingual))
            {
                task.Status = WorkbenchTaskStatus.ExportQueued;
                task.Stage = "ExportQueued";
                store.SaveTask(task);
                await worker.EnqueueAsync(new WorkCommand(taskId, WorkKind.Export, request.ExpectedRevision, Bilingual: request.Bilingual, OperationId: operationId), cancellationToken);
            }
            return Results.Accepted($"/api/tasks/{taskId}/exports", new { operationId, taskId, request.ExpectedRevision, request.Bilingual });
        });

        app.MapGet("/api/tasks/{taskId}/exports/{exportId}/{kind}", (string taskId, string exportId, string kind) =>
        {
            var task = store.GetTask(taskId);
            var export = store.ListExports(taskId).FirstOrDefault(e => e.Id == exportId);
            if (task is null || export is null) return Results.NotFound(new ApiError("export_not_found", "导出不存在。"));
            var path = kind switch { "video" => export.OutputPath, "zh-srt" => export.ChineseSrtPath, "bilingual-srt" => export.BilingualSrtPath, _ => null };
            if (path is null || !IsInside(task.WorkspacePath, path) || !File.Exists(path)) return Results.NotFound(new ApiError("artifact_not_found", "导出产物不存在。"));
            var contentType = path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4" : "application/x-subrip";
            return Results.File(path, contentType, Path.GetFileName(path), enableRangeProcessing: true);
        });
    }

    private static object ConfigurationSummary()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "translator.config.json");
        var config = File.Exists(path) ? TranslatorRuntimeConfig.Load(path) : new TranslatorRuntimeConfig();
        return new { llmModel = config.Llm.Model, whisperModel = config.Whisper.Model, apiKeyConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(config.Llm.ApiKeyEnv)) };
    }

    private static bool ToolAvailable(string name)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = name, Arguments = "--version", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
            process?.WaitForExit(2000);
            return process?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool IsInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
