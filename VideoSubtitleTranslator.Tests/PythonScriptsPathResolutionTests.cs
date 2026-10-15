using VideoSubtitleTranslator.Pipeline;

namespace VideoSubtitleTranslator.Tests;

public class PythonScriptsPathResolutionTests
{
    [Fact]
    public void Development_build_uses_source_python_scripts_so_downloaded_model_is_reused()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "VideoSubtitleTranslator.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var baseDirectory = Path.Combine(testRoot, "bin", "Debug", "net10.0");
            var outputScriptsDirectory = Path.Combine(baseDirectory, "PythonScripts");
            var sourceScriptsDirectory = Path.Combine(testRoot, "PythonScripts");
            var sourceModelPath = Path.Combine(
                sourceScriptsDirectory,
                "models",
                "whisper",
                "large-v3-turbo.pt");

            Directory.CreateDirectory(outputScriptsDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(sourceModelPath)!);
            File.WriteAllText(Path.Combine(outputScriptsDirectory, "subtitler.py"), "# output copy");
            File.WriteAllText(Path.Combine(sourceScriptsDirectory, "subtitler.py"), "# source script");
            File.WriteAllText(sourceModelPath, "model checkpoint");

            var resolvedDirectory = DefaultExtractSubtitleStep.ResolvePythonScriptsDirectory(
                baseDirectory);

            Assert.Equal(
                Path.GetFullPath(sourceScriptsDirectory),
                Path.GetFullPath(resolvedDirectory));
            Assert.True(File.Exists(Path.Combine(
                resolvedDirectory,
                "models",
                "whisper",
                "large-v3-turbo.pt")));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void Published_build_uses_python_scripts_beside_the_application()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "VideoSubtitleTranslator.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var baseDirectory = Path.Combine(testRoot, "published");
            var outputScriptsDirectory = Path.Combine(baseDirectory, "PythonScripts");

            Directory.CreateDirectory(outputScriptsDirectory);
            File.WriteAllText(Path.Combine(outputScriptsDirectory, "subtitler.py"), "# published copy");

            var resolvedDirectory = DefaultExtractSubtitleStep.ResolvePythonScriptsDirectory(
                baseDirectory);

            Assert.Equal(
                Path.GetFullPath(outputScriptsDirectory),
                Path.GetFullPath(resolvedDirectory));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }
}
