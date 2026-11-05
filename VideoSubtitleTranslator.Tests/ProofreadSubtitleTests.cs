using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VideoSubtitleTranslator.Pipeline;
using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Tests;

[Collection("Shared runtime configuration")]
public sealed class ProofreadSubtitleTests
{
    [Fact]
    public void Proofread_completes_without_fixed_wait_when_pairs_need_no_model_request()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(temp.Path);
        var dirs = new WorkDirs(temp.Path, ".mp4");
        File.WriteAllText(dirs.TranslatedSubtitlePath,
            "1\n00:00:00,000 --> 00:00:01,000\nGood morning.\n\n" +
            "2\n00:00:01,000 --> 00:00:02,000\nWho are you?\n\n" +
            "3\n00:00:02,000 --> 00:00:03,000\nI like apples.\n\n" +
            "4\n00:00:03,000 --> 00:00:04,000\nThe sky is blue.\n\n");
        var runtime = new TranslatorRuntimeConfig();
        runtime.Translation.RequestDelayMs = 1200;
        runtime.Translation.ProofreadRequestDelayMs = 0;
        runtime.Translation.ProofreadEnabled = true;

        var timer = Stopwatch.StartNew();
        new PipelineRunner(new PipelineRegistry()).Run(
            new PipelineConfig
            {
                Steps = [new PipelineStepConfig { Step = "ProofreadSubtitle", Implementation = "DeepSeek" }]
            },
            new Dictionary<string, string>
            {
                ["WorkspacePath"] = temp.Path,
                ["VideoExt"] = ".mp4",
                ["VideoTitle"] = "lesson"
            },
            runtime);
        timer.Stop();

        Assert.True(File.Exists(dirs.ProofreadSubtitlePath));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"Unexpected fixed delay: {timer.Elapsed}");
    }

    [Fact]
    public void Standard_and_meme_keep_their_proofreading_settings_and_whisper_stage()
    {
        var standard = TranslatorRuntimeConfig.Load(Path.Combine(AppContext.BaseDirectory, "Config", "translator.config.json"));
        var meme = TranslatorRuntimeConfig.Load(Path.Combine(AppContext.BaseDirectory, "Config", "translator_meme.config.json"));
        var standardPipeline = PipelineRunner.LoadConfig(Path.Combine(AppContext.BaseDirectory, "pipeline.config.json"));
        var memePipeline = PipelineRunner.LoadConfig(Path.Combine(AppContext.BaseDirectory, "pipeline_meme.config.json"));

        Assert.True(standard.Translation.ProofreadEnabled);
        Assert.False(meme.Translation.ProofreadEnabled);
        Assert.Contains(standardPipeline.Steps, step => step.Step == "ExtractSubtitle");
        Assert.Contains(memePipeline.Steps, step => step.Step == "ExtractSubtitle");
    }

    [Fact]
    public async Task Proofread_waits_between_actual_model_requests()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(temp.Path);
        var dirs = new WorkDirs(temp.Path, ".mp4");
        File.WriteAllText(dirs.TranslatedSubtitlePath,
            "1\n00:00:00,000 --> 00:00:01,000\nSame line.\n\n" +
            "2\n00:00:01,000 --> 00:00:02,000\nSame line.\n\n" +
            "3\n00:00:02,000 --> 00:00:03,000\nA different line.\n\n" +
            "4\n00:00:03,000 --> 00:00:04,000\nA different line.\n\n");
        var requestTimes = new System.Collections.Concurrent.ConcurrentQueue<long>();
        using var server = new LoopbackHttpServer(_ =>
        {
            requestTimes.Enqueue(Stopwatch.GetTimestamp());
            var index = requestTimes.Count - 1;
            var firstStart = index == 0 ? "00:00:00,000" : "00:00:02,000";
            var firstEnd = index == 0 ? "00:00:01,000" : "00:00:03,000";
            var secondStart = index == 0 ? "00:00:01,000" : "00:00:03,000";
            var secondEnd = index == 0 ? "00:00:02,000" : "00:00:04,000";
            var proofreadContent = JsonSerializer.Serialize(new
            {
                items = new[]
                {
                    new { start = firstStart, end = firstEnd, text = "Same line." },
                    new { start = secondStart, end = secondEnd, text = "Same line." }
                }
            });
            return JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = proofreadContent } } }
            });
        });
        var runtime = new TranslatorRuntimeConfig();
        runtime.Llm.BaseUrl = server.BaseUrl;
        runtime.Llm.RetryCount = 0;
        runtime.Translation.RequestDelayMs = 180;
        runtime.Translation.ProofreadJsonMaxAttempts = 1;
        runtime.Translation.ProofreadEnabled = true;

        new PipelineRunner(new PipelineRegistry()).Run(
            new PipelineConfig
            {
                Steps = [new PipelineStepConfig { Step = "ProofreadSubtitle", Implementation = "DeepSeek" }]
            },
            new Dictionary<string, string>
            {
                ["WorkspacePath"] = temp.Path,
                ["VideoExt"] = ".mp4",
                ["VideoTitle"] = "lesson"
            },
            runtime);
        await server.WaitForRequestsAsync(2, TimeSpan.FromSeconds(10));

        var times = requestTimes.ToArray();
        Assert.Equal(2, times.Length);
        Assert.True(Stopwatch.GetElapsedTime(times[0], times[1]) >= TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void Whisper_transcribes_audio_even_when_a_source_subtitle_sidecar_exists()
    {
        if (OperatingSystem.IsWindows()) return;

        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(temp.Path);
        var dirs = new WorkDirs(temp.Path, ".mp4");
        File.WriteAllText(Path.Combine(temp.Path, "video.srt"), "source subtitle");
        var fakePython = Path.Combine(temp.Path, "fake-python");
        File.WriteAllText(fakePython,
            "#!/bin/sh\n" +
            "while [ \"$#\" -gt 0 ]; do\n" +
            "  if [ \"$1\" = \"--output\" ]; then shift; output=\"$1\"; fi\n" +
            "  shift\n" +
            "done\n" +
            "printf '1\\n00:00:00,000 --> 00:00:01,000\\ntranscribed\\n' > \"$output\"\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.SetUnixFileMode(fakePython,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var runtime = new TranslatorRuntimeConfig();
        runtime.Whisper.PythonExecutable = fakePython;

        new PipelineRunner(new PipelineRegistry()).Run(
            new PipelineConfig
            {
                Steps = [new PipelineStepConfig { Step = "ExtractSubtitle", Implementation = "Default" }]
            },
            new Dictionary<string, string>
            {
                ["WorkspacePath"] = temp.Path,
                ["VideoExt"] = ".mp4"
            },
            runtime);

        Assert.Equal("source subtitle", File.ReadAllText(Path.Combine(temp.Path, "video.srt")));
        Assert.Contains("transcribed", File.ReadAllText(dirs.SubtitlePath));
    }

    [Fact]
    public async Task Proofread_writes_its_own_artifact_without_overwriting_the_translate_artifact()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(temp.Path);
        var dirs = new WorkDirs(temp.Path, ".mp4");
        var original = "1\n00:00:00,000 --> 00:00:01,000\n同一句话。\n\n" +
                       "2\n00:00:01,000 --> 00:00:02,000\n同一句话。\n\n";
        File.WriteAllText(dirs.TranslatedSubtitlePath, original);

        // 用 TcpListener 替身而不是 HttpListener：受限环境里 http.sys 注册会失败。
        using var server = new LoopbackHttpServer(_ =>
        {
            var merged = JsonSerializer.Serialize(new
            {
                items = new[] { new { start = "00:00:00,000", end = "00:00:02,000", text = "同一句话。" } }
            });
            return JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = merged } } }
            });
        });

        var runtime = new TranslatorRuntimeConfig();
        runtime.Llm.BaseUrl = server.BaseUrl;
        runtime.Llm.RetryCount = 0;
        runtime.Translation.ProofreadEnabled = true;
        runtime.Translation.ProofreadJsonMaxAttempts = 1;

        new PipelineRunner(new PipelineRegistry()).Run(
            new PipelineConfig
            {
                Steps = [new PipelineStepConfig { Step = "ProofreadSubtitle", Implementation = "DeepSeek" }]
            },
            new Dictionary<string, string>
            {
                ["WorkspacePath"] = temp.Path,
                ["VideoExt"] = ".mp4",
                ["VideoTitle"] = "lesson"
            },
            runtime);
        await server.WaitForRequestsAsync(1, TimeSpan.FromSeconds(10));

        // 校对合并了相邻句，但 Translate 的产物必须保持原样：它是 Translate 检查点的哈希来源。
        Assert.Equal(original, File.ReadAllText(dirs.TranslatedSubtitlePath));
        var proofread = SrtCodec.Read(dirs.ProofreadSubtitlePath);
        Assert.Single(proofread);
        Assert.Equal("同一句话。", proofread[0].SourceText);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
