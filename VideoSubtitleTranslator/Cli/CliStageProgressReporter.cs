using System.Diagnostics;

namespace VideoSubtitleTranslator.Cli;

public sealed class CliStageProgressReporter
{
    private readonly TextWriter? _output;
    private readonly TimeSpan _heartbeatInterval;
    private readonly object _writeLock = new();

    public CliStageProgressReporter(TextWriter? output = null, TimeSpan? heartbeatInterval = null)
    {
        _output = output;
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(15);
    }

    public void Run(string stage, Action action)
    {
        var label = Label(stage);
        if (label is null)
        {
            action();
            return;
        }

        var elapsed = Stopwatch.StartNew();
        Write($"进度：{label}开始");
        var finished = 0;
        using var timer = _heartbeatInterval > TimeSpan.Zero
            ? new Timer(_ =>
            {
                if (Volatile.Read(ref finished) == 0)
                    Write($"进度：{label}仍在运行，已用时 {Format(elapsed.Elapsed)}");
            }, null, _heartbeatInterval, _heartbeatInterval)
            : null;
        try
        {
            action();
        }
        catch
        {
            Volatile.Write(ref finished, 1);
            timer?.Dispose();
            Write($"进度：{label}失败，已用时 {Format(elapsed.Elapsed)}");
            throw;
        }
        Volatile.Write(ref finished, 1);
        timer?.Dispose();
        Write($"进度：{label}完成，已用时 {Format(elapsed.Elapsed)}");
    }

    private void Write(string message)
    {
        lock (_writeLock)
        {
            var writer = _output ?? Console.Error;
            writer.WriteLine(message);
            writer.Flush();
        }
    }

    private static string Format(TimeSpan elapsed) => elapsed.ToString("hh\\:mm\\:ss");

    private static string? Label(string stage) => stage switch
    {
        "Download" => "下载视频",
        "SplitAudio" => "提取音频",
        "ExtractSubtitle" => "加载 Whisper 模型并转录字幕",
        "BuildSentences" => "整理字幕条目",
        "NormalizeSubtitle" => "规范化字幕",
        "Translate" => "翻译字幕",
        "ProofreadSubtitle" => "自动校对字幕",
        "CulturalAnnotate" => "生成文化注释",
        "BuildDualSubtitle" => "生成双语字幕",
        _ => null
    };
}
