using System.Diagnostics;
using System.Text;

namespace VideoSubtitleTranslator.Workbench;

public sealed class MediaExporter
{
    private readonly string _ffmpegPath;

    public MediaExporter(string ffmpegPath = "ffmpeg") => _ffmpegPath = ffmpegPath;

    public async Task<ExportRecord> ExportAsync(WorkbenchTask task, SubtitleDocument document, bool bilingual, CancellationToken cancellationToken)
    {
        var exportId = Guid.NewGuid().ToString("N");
        var exportDirectory = Path.Combine(task.WorkspacePath, "exports", exportId);
        Directory.CreateDirectory(exportDirectory);
        var chinesePath = Path.Combine(exportDirectory, bilingual ? "subtitles.zh.srt" : "subtitles.srt");
        var bilingualPath = bilingual ? Path.Combine(exportDirectory, "subtitles.en-zh.srt") : null;
        var outputPath = Path.Combine(exportDirectory, bilingual ? "video.en-zh.mp4" : "video.zh.mp4");
        await AtomicWriteAsync(chinesePath, SrtCodec.Write(document.Items, item => item.TranslatedText), cancellationToken);
        if (bilingualPath is not null)
            await AtomicWriteAsync(bilingualPath, SrtCodec.Write(document.Items, item => $"{item.SourceText}\n{item.TranslatedText}"), cancellationToken);

        var videoPath = Path.Combine(task.WorkspacePath, "video.mp4");
        if (!File.Exists(videoPath)) throw new FileNotFoundException("待导出的视频不存在", videoPath);
        var subtitlePath = bilingualPath ?? chinesePath;
        var filterPath = subtitlePath.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
        var filter = $"subtitles='{filterPath}'";
        var arguments = new[] { "-y", "-i", videoPath, "-vf", filter, "-c:v", "libx264", "-preset", "medium", "-c:a", "copy", outputPath };
        await RunFfmpegAsync(arguments, cancellationToken);
        return new ExportRecord
        {
            Id = exportId,
            TaskId = task.Id,
            Revision = document.Revision,
            Bilingual = bilingual,
            OutputPath = outputPath,
            ChineseSrtPath = chinesePath,
            BilingualSrtPath = bilingualPath,
            OperationId = $"export:{task.Id}:{document.Revision}:{bilingual}:{exportId}"
        };
    }

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false), cancellationToken);
        File.Move(temp, path, true);
    }

    private async Task RunFfmpegAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"视频导出失败（ffmpeg exit code {process.ExitCode}）：{TrimProcessOutput(error)}");
    }

    private static string TrimProcessOutput(string output) => output.Length <= 1200 ? output : output[^1200..];
}
