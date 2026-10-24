using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VideoSubtitleTranslator.Pipeline;

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
        using var portReservation = new TcpListener(System.Net.IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((System.Net.IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var requestTimes = new List<long>();
        var server = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var context = await listener.GetContextAsync();
                requestTimes.Add(Stopwatch.GetTimestamp());
                var firstStart = i == 0 ? "00:00:00,000" : "00:00:02,000";
                var firstEnd = i == 0 ? "00:00:01,000" : "00:00:03,000";
                var secondStart = i == 0 ? "00:00:01,000" : "00:00:03,000";
                var secondEnd = i == 0 ? "00:00:02,000" : "00:00:04,000";
                var proofreadContent = JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new { start = firstStart, end = firstEnd, text = "Same line." },
                        new { start = secondStart, end = secondEnd, text = "Same line." }
                    }
                });
                var response = JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = proofreadContent } } }
                });
                var bytes = Encoding.UTF8.GetBytes(response);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
        var runtime = new TranslatorRuntimeConfig();
        runtime.Llm.BaseUrl = $"http://127.0.0.1:{port}/";
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
        await server.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, requestTimes.Count);
        Assert.True(Stopwatch.GetElapsedTime(requestTimes[0], requestTimes[1]) >= TimeSpan.FromMilliseconds(150));
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
