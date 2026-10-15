using System.Security.Cryptography;

namespace VideoSubtitleTranslator.Workbench;

public sealed record StageRecoveryPlan(int ResumeIndex, IReadOnlyList<string> ReusedStages, IReadOnlyList<string> InvalidatedStages);

/// <summary>Determines the longest valid, contiguous checkpoint prefix for a pipeline.</summary>
public static class StageRecovery
{
    public static StageRecoveryPlan Plan(
        IReadOnlyList<string> stages,
        IReadOnlyDictionary<string, StageCheckpoint> checkpoints,
        Func<string, string?> expectedArtifact)
    {
        for (var index = 0; index < stages.Count; index++)
        {
            var stage = stages[index];
            var expected = expectedArtifact(stage);
            if (expected is null || !checkpoints.TryGetValue(stage, out var checkpoint) ||
                !PathsEqual(expected, checkpoint.ArtifactPath) || !IsValid(checkpoint))
                return new StageRecoveryPlan(index, stages.Take(index).ToArray(), stages.Skip(index).ToArray());
        }

        return new StageRecoveryPlan(stages.Count, stages.ToArray(), Array.Empty<string>());
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsValid(StageCheckpoint checkpoint) =>
        File.Exists(checkpoint.ArtifactPath) &&
        string.Equals(ComputeSha256(checkpoint.ArtifactPath), checkpoint.ArtifactSha256, StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string expected, string actual) =>
        string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase);
}
