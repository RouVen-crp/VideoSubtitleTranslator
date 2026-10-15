using VideoSubtitleTranslator.Workbench;

namespace VideoSubtitleTranslator.Tests;

public sealed class StageRecoveryTests
{
    [Fact]
    public void Plan_reuses_only_the_valid_contiguous_checkpoint_prefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "vst-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var audio = Path.Combine(root, "audio.wav");
            var subtitles = Path.Combine(root, "subtitle.srt");
            File.WriteAllText(audio, "audio");
            File.WriteAllText(subtitles, "subtitles");
            var checkpoints = new Dictionary<string, StageCheckpoint>
            {
                ["SplitAudio"] = new("SplitAudio", audio, StageRecovery.ComputeSha256(audio), DateTimeOffset.UtcNow),
                ["ExtractSubtitle"] = new("ExtractSubtitle", subtitles, "BAD-HASH", DateTimeOffset.UtcNow)
            };

            var plan = StageRecovery.Plan(["SplitAudio", "ExtractSubtitle", "Translate"], checkpoints,
                stage => stage switch { "SplitAudio" => audio, "ExtractSubtitle" => subtitles, _ => Path.Combine(root, "translated.srt") });

            Assert.Equal(1, plan.ResumeIndex);
            Assert.Equal(["SplitAudio"], plan.ReusedStages);
            Assert.Equal(["ExtractSubtitle", "Translate"], plan.InvalidatedStages);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Plan_rejects_a_checkpoint_with_an_unexpected_artifact_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "vst-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var expected = Path.Combine(root, "audio.wav");
            var other = Path.Combine(root, "other.wav");
            File.WriteAllText(expected, "audio");
            File.WriteAllText(other, "audio");
            var checkpoints = new Dictionary<string, StageCheckpoint>
            {
                ["SplitAudio"] = new("SplitAudio", other, StageRecovery.ComputeSha256(other), DateTimeOffset.UtcNow)
            };

            var plan = StageRecovery.Plan(["SplitAudio"], checkpoints, _ => expected);

            Assert.Equal(0, plan.ResumeIndex);
            Assert.Empty(plan.ReusedStages);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
