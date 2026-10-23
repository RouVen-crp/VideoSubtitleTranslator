using System.Security.Cryptography;
using System.Text.Json;
using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Cli;

public enum CliTaskStatus { Queued, Running, Completed, Failed, Interrupted }

public sealed class CliTaskRecord
{
    public string Id { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Mode { get; set; } = "standard";
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string? WorkspacePath { get; set; }
    public bool IsLocalFile { get; set; }
    public bool Bilingual { get; set; }
    public bool Burn { get; set; }
    public CliTaskStatus Status { get; set; }
    public string Stage { get; set; } = "Queued";
    public string? SubtitlePath { get; set; }
    public Dictionary<string, string> Context { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int ProcessId { get; set; }
    public Dictionary<string, StageCheckpoint> Checkpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CompletedStages { get; set; } = new();
}

public sealed class CliTaskStore
{
    private readonly string _directory;
    private readonly Func<int, bool> _isProcessAlive;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public CliTaskStore(string? directory = null, Func<int, bool>? isProcessAlive = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "zimu", "tasks");
        _isProcessAlive = isProcessAlive ?? (pid => { try { return System.Diagnostics.Process.GetProcessById(pid) is { HasExited: false }; } catch { return false; } });
    }

    public CliTaskRecord Create(TranslationRequest request)
    {
        Directory.CreateDirectory(_directory);
        var task = new CliTaskRecord
        {
            Id = Guid.NewGuid().ToString("N")[..8], Input = CliInputSanitizer.Sanitize(request.Input), Mode = request.Mode,
            WorkspaceRoot = request.WorkspaceRoot, IsLocalFile = request.IsLocalFile,
            Bilingual = request.CreateBilingualSubtitle, Burn = request.BurnSubtitles,
            Status = CliTaskStatus.Queued, ProcessId = Environment.ProcessId
        };
        Save(task);
        return task;
    }

    public void Save(CliTaskRecord task)
    {
        Directory.CreateDirectory(_directory);
        task.UpdatedAt = DateTimeOffset.UtcNow;
        var path = GetPath(task.Id);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(task, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    public CliTaskRecord? Get(string id)
    {
        if (!IsValidId(id)) return null;
        var path = GetPath(id);
        if (!File.Exists(path)) return null;
        var task = JsonSerializer.Deserialize<CliTaskRecord>(File.ReadAllText(path));
        if (task is not null) MarkInterruptedIfOrphaned(task);
        return task;
    }

    public IReadOnlyList<CliTaskRecord> List()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<CliTaskRecord>();
        return Directory.EnumerateFiles(_directory, "*.json").Select(path =>
        {
            try
            {
                var task = JsonSerializer.Deserialize<CliTaskRecord>(File.ReadAllText(path));
                if (task is null) return null;
                MarkInterruptedIfOrphaned(task);
                return task;
            }
            catch (JsonException) { return null; }
        }).Where(task => task is not null).Cast<CliTaskRecord>().OrderByDescending(task => task.CreatedAt).ToArray();
    }

    public static bool IsValidId(string id) => id.Length == 8 && id.All(Uri.IsHexDigit);
    private string GetPath(string id) => Path.Combine(_directory, id + ".json");
    private void MarkInterruptedIfOrphaned(CliTaskRecord task)
    {
        if (task.Status == CliTaskStatus.Running && !_isProcessAlive(task.ProcessId))
        {
            task.Status = CliTaskStatus.Interrupted;
            task.Stage = "Interrupted";
            Save(task);
        }
    }
}

public static class CliInputSanitizer
{
    public static string Sanitize(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return input;
        var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
        var query = builder.Query.TrimStart('?');
        if (query.Length > 0)
            builder.Query = string.Join('&', query.Split('&').Select(part =>
            {
                var separator = part.IndexOf('=');
                var key = separator < 0 ? part : part[..separator];
                return IsApiKeyParameter(key)
                    ? key + "=[redacted]" : part;
            }));
        return builder.Uri.AbsoluteUri;
    }

    private static bool IsApiKeyParameter(string name)
    {
        var normalized = Uri.UnescapeDataString(name).Replace('-', '_').ToLowerInvariant();
        return normalized is "key" or "api_key" or "apikey" || normalized.EndsWith("_api_key", StringComparison.Ordinal);
    }
}

public static class CliArtifactCheckpoint
{
    public static StageCheckpoint Create(string stage, string path) =>
        new(stage, Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), DateTimeOffset.UtcNow);
    public static bool IsValid(StageCheckpoint checkpoint) => File.Exists(checkpoint.ArtifactPath) &&
        string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(checkpoint.ArtifactPath))), checkpoint.ArtifactSha256, StringComparison.OrdinalIgnoreCase);
}
