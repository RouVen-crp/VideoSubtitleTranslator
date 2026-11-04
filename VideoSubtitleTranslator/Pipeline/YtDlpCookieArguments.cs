namespace VideoSubtitleTranslator.Pipeline;

/// <summary>
/// 构造 yt-dlp 的 cookies 参数。
/// 浏览器 cookies 只是可选的鉴权辅助：浏览器正在运行时 yt-dlp 无法复制 cookie 数据库，
/// 会直接以非零退出码失败。因此调用方必须把 <c>UsedCookies</c> 当提示：
/// 带 cookies 失败后去掉 cookies 重试一次，而不是让整个任务失败。
/// </summary>
public static class YtDlpCookieArguments
{
    public static (IReadOnlyList<string> Arguments, bool UsedCookies) Build()
    {
        var cookiesFile = Environment.GetEnvironmentVariable("COOKIES_FILE");
        if (!string.IsNullOrWhiteSpace(cookiesFile) && File.Exists(cookiesFile))
        {
            return (["--cookies", cookiesFile], true);
        }

        var browser = GlobalRuntimeConfig.Current.CookiesFromBrowser;
        if (string.IsNullOrWhiteSpace(browser))
        {
            return (Array.Empty<string>(), false);
        }

        return (["--cookies-from-browser", browser], true);
    }
}
