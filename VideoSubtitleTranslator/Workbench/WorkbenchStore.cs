using Microsoft.Data.Sqlite;

namespace VideoSubtitleTranslator.Workbench;

/// <summary>
/// Small SQLite persistence boundary for tasks, subtitle revisions, stage checkpoints, and exports.
/// All writes are serialized so the single worker and HTTP requests cannot interleave revisions.
/// </summary>
public sealed class WorkbenchStore : IDisposable
{
    private readonly string _connectionString;
    private readonly object _gate = new();

    public WorkbenchStore(WorkbenchOptions options)
    {
        Directory.CreateDirectory(options.DataDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(options.DataDirectory, "workbench.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        Initialize();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS tasks (
                    id TEXT PRIMARY KEY,
                    source_file_name TEXT NOT NULL,
                    workspace_path TEXT NOT NULL,
                    status TEXT NOT NULL,
                    stage TEXT NOT NULL,
                    processed_items INTEGER NOT NULL DEFAULT 0,
                    total_items INTEGER NOT NULL DEFAULT 0,
                    elapsed_seconds REAL NOT NULL DEFAULT 0,
                    revision INTEGER NOT NULL DEFAULT 0,
                    error TEXT NULL,
                    operation_id TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS subtitle_items (
                    task_id TEXT NOT NULL,
                    revision INTEGER NOT NULL,
                    item_id TEXT NOT NULL,
                    ordinal INTEGER NOT NULL,
                    start_ms INTEGER NOT NULL,
                    end_ms INTEGER NOT NULL,
                    source_text TEXT NOT NULL,
                    translated_text TEXT NOT NULL,
                    needs_review INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY(task_id, revision, item_id),
                    FOREIGN KEY(task_id) REFERENCES tasks(id)
                );
                CREATE INDEX IF NOT EXISTS idx_subtitle_items_current ON subtitle_items(task_id, revision, ordinal);
                CREATE TABLE IF NOT EXISTS stage_records (
                    task_id TEXT NOT NULL,
                    stage TEXT NOT NULL,
                    artifact_path TEXT NOT NULL,
                    artifact_sha256 TEXT NOT NULL,
                    completed_at TEXT NOT NULL,
                    PRIMARY KEY(task_id, stage),
                    FOREIGN KEY(task_id) REFERENCES tasks(id)
                );
                CREATE TABLE IF NOT EXISTS exports (
                    id TEXT PRIMARY KEY,
                    task_id TEXT NOT NULL,
                    revision INTEGER NOT NULL,
                    bilingual INTEGER NOT NULL,
                    output_path TEXT NOT NULL,
                    chinese_srt_path TEXT NOT NULL,
                    bilingual_srt_path TEXT NULL,
                    operation_id TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY(task_id) REFERENCES tasks(id)
                );
                CREATE TABLE IF NOT EXISTS operations (
                    operation_id TEXT PRIMARY KEY,
                    task_id TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    expected_revision INTEGER NOT NULL DEFAULT 0,
                    subtitle_id TEXT NULL,
                    bilingual INTEGER NOT NULL DEFAULT 0,
                    status TEXT NOT NULL DEFAULT 'Queued',
                    error TEXT NULL,
                    updated_at TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL,
                    FOREIGN KEY(task_id) REFERENCES tasks(id)
                );
                """;
            command.ExecuteNonQuery();
            EnsureColumn(connection, "operations", "expected_revision", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "operations", "subtitle_id", "TEXT NULL");
            EnsureColumn(connection, "operations", "bilingual", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "operations", "status", "TEXT NOT NULL DEFAULT 'Queued'");
            EnsureColumn(connection, "operations", "error", "TEXT NULL");
            EnsureColumn(connection, "operations", "updated_at", "TEXT NOT NULL DEFAULT ''");
        }
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        var exists = false;
        using (var reader = check.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }
        if (exists) return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    public IReadOnlyList<WorkbenchTask> ListTasks()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM tasks ORDER BY created_at DESC";
            using var reader = command.ExecuteReader();
            var result = new List<WorkbenchTask>();
            while (reader.Read()) result.Add(ReadTask(reader));
            return result;
        }
    }

    public WorkbenchTask? GetTask(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM tasks WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadTask(reader) : null;
        }
    }

    public WorkbenchTask? GetTaskByOperationId(string operationId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM tasks WHERE operation_id = $operation";
            command.Parameters.AddWithValue("$operation", operationId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadTask(reader) : null;
        }
    }

    public IReadOnlyList<WorkbenchTask> FindByStatus(WorkbenchTaskStatus status)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM tasks WHERE status = $status ORDER BY created_at";
            command.Parameters.AddWithValue("$status", status.ToString());
            using var reader = command.ExecuteReader();
            var result = new List<WorkbenchTask>();
            while (reader.Read()) result.Add(ReadTask(reader));
            return result;
        }
    }

    public bool TryCreateTask(WorkbenchTask task)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO tasks
                (id, source_file_name, workspace_path, status, stage, processed_items, total_items,
                 elapsed_seconds, revision, error, operation_id, created_at, updated_at)
                VALUES ($id, $name, $workspace, $status, $stage, $processed, $total,
                        $elapsed, $revision, $error, $operation, $created, $updated)
                """;
            AddTaskParameters(command, task);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public void SaveTask(WorkbenchTask task)
    {
        lock (_gate)
        {
            task.UpdatedAt = DateTimeOffset.UtcNow;
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE tasks SET source_file_name=$name, workspace_path=$workspace,
                    status=CASE WHEN status=$cancelled THEN $cancelled ELSE $status END,
                    stage=$stage, processed_items=$processed, total_items=$total,
                    elapsed_seconds=$elapsed, revision=$revision,
                    error=CASE WHEN status=$cancelled THEN error ELSE $error END,
                    updated_at=$updated WHERE id=$id
                """;
            AddTaskParameters(command, task, includeOperation: false);
            command.Parameters.AddWithValue("$cancelled", WorkbenchTaskStatus.Cancelled.ToString());
            command.ExecuteNonQuery();
        }
    }

    public bool TryBeginOperation(string operationId, string taskId, WorkKind kind, int expectedRevision = 0, string? subtitleId = null, bool bilingual = false)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO operations(operation_id, task_id, kind, expected_revision, subtitle_id, bilingual, status, created_at, updated_at) VALUES($id,$task,$kind,$revision,$subtitle,$bilingual,$status,$created,$updated)";
            command.Parameters.AddWithValue("$id", operationId);
            command.Parameters.AddWithValue("$task", taskId);
            command.Parameters.AddWithValue("$kind", kind.ToString());
            command.Parameters.AddWithValue("$revision", expectedRevision);
            command.Parameters.AddWithValue("$subtitle", (object?)subtitleId ?? DBNull.Value);
            command.Parameters.AddWithValue("$bilingual", bilingual ? 1 : 0);
            command.Parameters.AddWithValue("$status", WorkbenchOperationStatus.Queued.ToString());
            var now = DateTimeOffset.UtcNow.ToString("O");
            command.Parameters.AddWithValue("$created", now);
            command.Parameters.AddWithValue("$updated", now);
            if (command.ExecuteNonQuery() == 1) return true;

            using var retry = connection.CreateCommand();
            retry.CommandText = "UPDATE operations SET task_id=$task, kind=$kind, expected_revision=$revision, subtitle_id=$subtitle, bilingual=$bilingual, status=$queued, error=NULL, updated_at=$updated WHERE operation_id=$id AND status=$failed";
            retry.Parameters.AddWithValue("$id", operationId);
            retry.Parameters.AddWithValue("$task", taskId);
            retry.Parameters.AddWithValue("$kind", kind.ToString());
            retry.Parameters.AddWithValue("$revision", expectedRevision);
            retry.Parameters.AddWithValue("$subtitle", (object?)subtitleId ?? DBNull.Value);
            retry.Parameters.AddWithValue("$bilingual", bilingual ? 1 : 0);
            retry.Parameters.AddWithValue("$queued", WorkbenchOperationStatus.Queued.ToString());
            retry.Parameters.AddWithValue("$failed", WorkbenchOperationStatus.Failed.ToString());
            retry.Parameters.AddWithValue("$updated", now);
            return retry.ExecuteNonQuery() == 1;
        }
    }

    public void MarkOperationRunning(string operationId) => UpdateOperation(operationId, WorkbenchOperationStatus.Running, null);
    public void MarkOperationCompleted(string operationId) => UpdateOperation(operationId, WorkbenchOperationStatus.Completed, null);
    public void MarkOperationFailed(string operationId, string error) => UpdateOperation(operationId, WorkbenchOperationStatus.Failed, error);

    private void UpdateOperation(string operationId, WorkbenchOperationStatus status, string? error)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE operations SET status=$status,error=$error,updated_at=$updated WHERE operation_id=$id";
            command.Parameters.AddWithValue("$id", operationId);
            command.Parameters.AddWithValue("$status", status.ToString());
            command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void MarkRunningOperationsAsFailed()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE operations SET status=$failed, error=$error, updated_at=$updated WHERE status=$running";
            command.Parameters.AddWithValue("$failed", WorkbenchOperationStatus.Failed.ToString());
            command.Parameters.AddWithValue("$running", WorkbenchOperationStatus.Running.ToString());
            command.Parameters.AddWithValue("$error", "服务重启时操作中断，可重试。");
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<PendingOperation> FindPendingOperations(WorkKind kind)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT o.operation_id,o.task_id,o.kind,o.expected_revision,o.subtitle_id,o.bilingual FROM operations o JOIN tasks t ON t.id=o.task_id WHERE o.kind=$kind AND o.status<>$completed AND t.status=$status";
            command.Parameters.AddWithValue("$kind", kind.ToString());
            command.Parameters.AddWithValue("$completed", WorkbenchOperationStatus.Completed.ToString());
            command.Parameters.AddWithValue("$status", WorkbenchTaskStatus.ExportQueued.ToString());
            using var reader = command.ExecuteReader();
            var result = new List<PendingOperation>();
            while (reader.Read())
            {
                result.Add(new PendingOperation(reader.GetString(0), reader.GetString(1), Enum.Parse<WorkKind>(reader.GetString(2)), reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5) != 0));
            }
            return result;
        }
    }

    public PendingOperation? FindPendingOperation(string taskId, WorkKind kind)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT operation_id,task_id,kind,expected_revision,subtitle_id,bilingual FROM operations WHERE task_id=$task AND kind=$kind AND status<>$completed ORDER BY created_at DESC LIMIT 1";
            command.Parameters.AddWithValue("$task", taskId);
            command.Parameters.AddWithValue("$kind", kind.ToString());
            command.Parameters.AddWithValue("$completed", WorkbenchOperationStatus.Completed.ToString());
            using var reader = command.ExecuteReader();
            return reader.Read()
                ? new PendingOperation(reader.GetString(0), reader.GetString(1), Enum.Parse<WorkKind>(reader.GetString(2)), reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5) != 0)
                : null;
        }
    }

    public void MarkProcessingAsInterrupted()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE tasks SET status=CASE WHEN status=$processing THEN $interrupted ELSE $exportQueued END, error=$error, updated_at=$updated WHERE status IN ($processing,$exporting,$exportQueued)";
            command.Parameters.AddWithValue("$interrupted", WorkbenchTaskStatus.Interrupted.ToString());
            command.Parameters.AddWithValue("$error", "服务重启时任务正在执行；请确认可能产生的 API 费用后恢复。");
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$processing", WorkbenchTaskStatus.Processing.ToString());
            command.Parameters.AddWithValue("$exporting", WorkbenchTaskStatus.Exporting.ToString());
            command.Parameters.AddWithValue("$exportQueued", WorkbenchTaskStatus.ExportQueued.ToString());
            command.ExecuteNonQuery();
        }
    }

    public bool TryCompletePipeline(string taskId, int revision, int itemCount)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE tasks SET revision=$revision, processed_items=$count, total_items=$count, status=$status, stage=$stage, error=NULL, updated_at=$updated WHERE id=$id AND status<>$cancelled";
            command.Parameters.AddWithValue("$id", taskId);
            command.Parameters.AddWithValue("$revision", revision);
            command.Parameters.AddWithValue("$count", itemCount);
            command.Parameters.AddWithValue("$status", WorkbenchTaskStatus.AwaitingReview.ToString());
            command.Parameters.AddWithValue("$stage", "AwaitingReview");
            command.Parameters.AddWithValue("$cancelled", WorkbenchTaskStatus.Cancelled.ToString());
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            return command.ExecuteNonQuery() == 1;
        }
    }

    public SubtitleDocument? GetSubtitles(string taskId)
    {
        lock (_gate)
        {
            var task = GetTaskUnsafe(taskId);
            if (task is null || task.Revision == 0) return null;
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT item_id,start_ms,end_ms,source_text,translated_text,needs_review FROM subtitle_items WHERE task_id=$task AND revision=$revision ORDER BY ordinal";
            command.Parameters.AddWithValue("$task", taskId);
            command.Parameters.AddWithValue("$revision", task.Revision);
            using var reader = command.ExecuteReader();
            var items = new List<SubtitleItem>();
            while (reader.Read())
            {
                items.Add(new SubtitleItem
                {
                    Id = reader.GetString(0),
                    StartMs = reader.GetInt64(1),
                    EndMs = reader.GetInt64(2),
                    SourceText = reader.GetString(3),
                    TranslatedText = reader.GetString(4),
                    NeedsReview = reader.GetInt64(5) != 0
                });
            }
            return new SubtitleDocument { TaskId = taskId, Revision = task.Revision, Items = items };
        }
    }

    public bool TrySaveSubtitles(string taskId, int expectedRevision, IReadOnlyList<SubtitleItem> items, out SubtitleDocument? document)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var task = GetTaskUnsafe(taskId, connection, transaction);
            if (task is null || task.Revision != expectedRevision)
            {
                transaction.Rollback();
                document = GetSubtitles(taskId);
                return false;
            }

            var revision = expectedRevision + 1;
            foreach (var (item, ordinal) in items.Select((value, index) => (value, index)))
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO subtitle_items(task_id,revision,item_id,ordinal,start_ms,end_ms,source_text,translated_text,needs_review) VALUES($task,$revision,$id,$ordinal,$start,$end,$source,$translated,$review)";
                AddItemParameters(insert, taskId, revision, item, ordinal);
                insert.ExecuteNonQuery();
            }

            task.Revision = revision;
            task.TotalItems = items.Count;
            task.ProcessedItems = items.Count;
            SaveTaskUnsafe(connection, transaction, task);
            transaction.Commit();
            document = new SubtitleDocument { TaskId = taskId, Revision = revision, Items = items.ToList() };
            return true;
        }
    }

    public bool TryApplyTranslation(string taskId, string itemId, int expectedRevision, string translatedText, out int newRevision)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var task = GetTaskUnsafe(taskId, connection, transaction);
            var current = GetSubtitlesUnsafe(taskId, task?.Revision ?? 0, connection, transaction);
            if (task is null || task.Status == WorkbenchTaskStatus.Cancelled || task.Revision != expectedRevision || current is null || current.Items.All(i => i.Id != itemId))
            {
                transaction.Rollback();
                newRevision = task?.Revision ?? 0;
                return false;
            }
            var item = current.Items.First(i => i.Id == itemId);
            item.TranslatedText = translatedText;
            item.NeedsReview = false;
            var revision = expectedRevision + 1;
            foreach (var (copy, ordinal) in current.Items.Select((value, index) => (value, index)))
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO subtitle_items(task_id,revision,item_id,ordinal,start_ms,end_ms,source_text,translated_text,needs_review) VALUES($task,$revision,$id,$ordinal,$start,$end,$source,$translated,$review)";
                AddItemParameters(insert, taskId, revision, copy, ordinal);
                insert.ExecuteNonQuery();
            }
            task.Revision = revision;
            task.Status = WorkbenchTaskStatus.AwaitingReview;
            task.Stage = "AwaitingReview";
            task.Error = null;
            SaveTaskUnsafe(connection, transaction, task);
            transaction.Commit();
            newRevision = revision;
            return true;
        }
    }

    public void AddStage(string taskId, string stage, string artifactPath, string sha256)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO stage_records(task_id,stage,artifact_path,artifact_sha256,completed_at) VALUES($task,$stage,$path,$sha,$completed) ON CONFLICT(task_id,stage) DO UPDATE SET artifact_path=$path,artifact_sha256=$sha,completed_at=$completed";
            command.Parameters.AddWithValue("$task", taskId);
            command.Parameters.AddWithValue("$stage", stage);
            command.Parameters.AddWithValue("$path", artifactPath);
            command.Parameters.AddWithValue("$sha", sha256);
            command.Parameters.AddWithValue("$completed", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyDictionary<string, StageCheckpoint> GetStageCheckpoints(string taskId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT stage,artifact_path,artifact_sha256,completed_at FROM stage_records WHERE task_id=$task";
            command.Parameters.AddWithValue("$task", taskId);
            using var reader = command.ExecuteReader();
            var checkpoints = new Dictionary<string, StageCheckpoint>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var checkpoint = new StageCheckpoint(reader.GetString(0), reader.GetString(1), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3)));
                checkpoints[checkpoint.Stage] = checkpoint;
            }
            return checkpoints;
        }
    }

    public void RemoveStages(string taskId, IEnumerable<string> stages)
    {
        var names = stages.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length == 0) return;
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            foreach (var stage in names)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM stage_records WHERE task_id=$task AND stage=$stage";
                command.Parameters.AddWithValue("$task", taskId);
                command.Parameters.AddWithValue("$stage", stage);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public void AddExport(ExportRecord export)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO exports(id,task_id,revision,bilingual,output_path,chinese_srt_path,bilingual_srt_path,operation_id,created_at) VALUES($id,$task,$revision,$bilingual,$output,$chinese,$bilingualPath,$operation,$created)";
            command.Parameters.AddWithValue("$id", export.Id);
            command.Parameters.AddWithValue("$task", export.TaskId);
            command.Parameters.AddWithValue("$revision", export.Revision);
            command.Parameters.AddWithValue("$bilingual", export.Bilingual ? 1 : 0);
            command.Parameters.AddWithValue("$output", export.OutputPath);
            command.Parameters.AddWithValue("$chinese", export.ChineseSrtPath);
            command.Parameters.AddWithValue("$bilingualPath", (object?)export.BilingualSrtPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$operation", export.OperationId);
            command.Parameters.AddWithValue("$created", export.CreatedAt.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<ExportRecord> ListExports(string taskId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,task_id,revision,bilingual,output_path,chinese_srt_path,bilingual_srt_path,operation_id,created_at FROM exports WHERE task_id=$task ORDER BY created_at DESC";
            command.Parameters.AddWithValue("$task", taskId);
            using var reader = command.ExecuteReader();
            var result = new List<ExportRecord>();
            while (reader.Read())
            {
                result.Add(new ExportRecord
                {
                    Id = reader.GetString(0), TaskId = reader.GetString(1), Revision = reader.GetInt32(2),
                    Bilingual = reader.GetInt64(3) != 0, OutputPath = reader.GetString(4),
                    ChineseSrtPath = reader.GetString(5), BilingualSrtPath = reader.IsDBNull(6) ? null : reader.GetString(6),
                    OperationId = reader.GetString(7), CreatedAt = DateTimeOffset.Parse(reader.GetString(8))
                });
            }
            return result;
        }
    }

    private WorkbenchTask? GetTaskUnsafe(string id, SqliteConnection? connection = null, SqliteTransaction? transaction = null)
    {
        var ownsConnection = connection is null;
        connection ??= Open();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM tasks WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        var result = reader.Read() ? ReadTask(reader) : null;
        if (ownsConnection) connection.Dispose();
        return result;
    }

    private static SubtitleDocument? GetSubtitlesUnsafe(string taskId, int revision, SqliteConnection connection, SqliteTransaction transaction)
    {
        if (revision == 0) return null;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT item_id,start_ms,end_ms,source_text,translated_text,needs_review FROM subtitle_items WHERE task_id=$task AND revision=$revision ORDER BY ordinal";
        command.Parameters.AddWithValue("$task", taskId);
        command.Parameters.AddWithValue("$revision", revision);
        using var reader = command.ExecuteReader();
        var items = new List<SubtitleItem>();
        while (reader.Read()) items.Add(new SubtitleItem
        {
            Id = reader.GetString(0), StartMs = reader.GetInt64(1), EndMs = reader.GetInt64(2), SourceText = reader.GetString(3),
            TranslatedText = reader.GetString(4), NeedsReview = reader.GetInt64(5) != 0
        });
        return new SubtitleDocument { TaskId = taskId, Revision = revision, Items = items };
    }

    private static WorkbenchTask ReadTask(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        SourceFileName = reader.GetString(reader.GetOrdinal("source_file_name")),
        WorkspacePath = reader.GetString(reader.GetOrdinal("workspace_path")),
        Status = Enum.Parse<WorkbenchTaskStatus>(reader.GetString(reader.GetOrdinal("status"))),
        Stage = reader.GetString(reader.GetOrdinal("stage")),
        ProcessedItems = reader.GetInt32(reader.GetOrdinal("processed_items")),
        TotalItems = reader.GetInt32(reader.GetOrdinal("total_items")),
        ElapsedSeconds = reader.GetDouble(reader.GetOrdinal("elapsed_seconds")),
        Revision = reader.GetInt32(reader.GetOrdinal("revision")),
        Error = reader.IsDBNull(reader.GetOrdinal("error")) ? null : reader.GetString(reader.GetOrdinal("error")),
        OperationId = reader.GetString(reader.GetOrdinal("operation_id")),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at")))
    };

    private static void AddTaskParameters(SqliteCommand command, WorkbenchTask task, bool includeOperation = true)
    {
        command.Parameters.AddWithValue("$id", task.Id);
        command.Parameters.AddWithValue("$name", task.SourceFileName);
        command.Parameters.AddWithValue("$workspace", task.WorkspacePath);
        command.Parameters.AddWithValue("$status", task.Status.ToString());
        command.Parameters.AddWithValue("$stage", task.Stage);
        command.Parameters.AddWithValue("$processed", task.ProcessedItems);
        command.Parameters.AddWithValue("$total", task.TotalItems);
        command.Parameters.AddWithValue("$elapsed", task.ElapsedSeconds);
        command.Parameters.AddWithValue("$revision", task.Revision);
        command.Parameters.AddWithValue("$error", (object?)task.Error ?? DBNull.Value);
        if (includeOperation) command.Parameters.AddWithValue("$operation", task.OperationId);
        command.Parameters.AddWithValue("$created", task.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", task.UpdatedAt.ToString("O"));
    }

    private static void SaveTaskUnsafe(SqliteConnection connection, SqliteTransaction transaction, WorkbenchTask task)
    {
        task.UpdatedAt = DateTimeOffset.UtcNow;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE tasks SET status=$status,stage=$stage,processed_items=$processed,total_items=$total,elapsed_seconds=$elapsed,revision=$revision,error=$error,updated_at=$updated WHERE id=$id";
        command.Parameters.AddWithValue("$id", task.Id);
        command.Parameters.AddWithValue("$status", task.Status.ToString());
        command.Parameters.AddWithValue("$stage", task.Stage);
        command.Parameters.AddWithValue("$processed", task.ProcessedItems);
        command.Parameters.AddWithValue("$total", task.TotalItems);
        command.Parameters.AddWithValue("$elapsed", task.ElapsedSeconds);
        command.Parameters.AddWithValue("$revision", task.Revision);
        command.Parameters.AddWithValue("$error", (object?)task.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", task.UpdatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void AddItemParameters(SqliteCommand command, string taskId, int revision, SubtitleItem item, int ordinal)
    {
        command.Parameters.AddWithValue("$task", taskId);
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$ordinal", ordinal);
        command.Parameters.AddWithValue("$start", item.StartMs);
        command.Parameters.AddWithValue("$end", item.EndMs);
        command.Parameters.AddWithValue("$source", item.SourceText);
        command.Parameters.AddWithValue("$translated", item.TranslatedText);
        command.Parameters.AddWithValue("$review", item.NeedsReview ? 1 : 0);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            SqliteConnection.ClearAllPools();
        }
    }
}
