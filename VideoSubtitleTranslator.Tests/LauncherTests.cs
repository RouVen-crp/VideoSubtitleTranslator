namespace VideoSubtitleTranslator.Tests;

/// <summary>
/// zimu 与 zm 必须都能启动：zm 在 Unix 是 shell 包装，在 Windows 是 .cmd 包装，
/// 两者都要转发到同一个可执行文件，并且安装脚本要能把它们放进 PATH。
/// </summary>
public sealed class LauncherTests
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string ScriptsDirectory() =>
        Path.Combine(RepoRoot(), "VideoSubtitleTranslator", "scripts");

    [Fact]
    public void Posix_zm_launcher_delegates_to_the_zimu_binary()
    {
        var launcher = Path.Combine(ScriptsDirectory(), "zm");

        var text = File.ReadAllText(launcher);

        Assert.Contains("zimu", text);
        Assert.Contains("exec", text);
    }

    [Fact]
    public void Windows_zm_launcher_forwards_arguments_to_zimu_exe()
    {
        var launcher = Path.Combine(ScriptsDirectory(), "zm.cmd");

        var text = File.ReadAllText(launcher);

        Assert.Contains("zimu.exe", text);
        Assert.Contains("%*", text);
    }

    [Fact]
    public void Build_copies_the_windows_zm_launcher_next_to_the_apphost()
    {
        var launcher = Path.Combine(AppContext.BaseDirectory, "zm.cmd");

        Assert.True(
            File.Exists(launcher),
            "构建产物里缺少 zm.cmd，Windows 上就只有 zimu 而没有 zm。");
    }

    [Fact]
    public void Install_scripts_exist_for_windows_and_posix()
    {
        var scripts = ScriptsDirectory();

        Assert.True(File.Exists(Path.Combine(scripts, "install.ps1")), "缺少 Windows 安装脚本。");
        Assert.True(File.Exists(Path.Combine(scripts, "install.sh")), "缺少 macOS/Linux 安装脚本。");
    }
}
