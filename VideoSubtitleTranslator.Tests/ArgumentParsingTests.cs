namespace VideoSubtitleTranslator.Tests;

public class ArgumentParsingTests
{
    [Fact]
    public void No_mode_flag_defaults_to_standard()
    {
        var (mode, positional) = Program.ParseArgs(["http://example.com/video", "/tmp/ws"]);

        Assert.Equal("standard", mode);
        Assert.Equal(["http://example.com/video", "/tmp/ws"], positional);
    }

    [Fact]
    public void Mode_meme_parsed_and_removed_from_positional()
    {
        var (mode, positional) = Program.ParseArgs(["--mode", "meme", "http://example.com/video", "/tmp/ws"]);

        Assert.Equal("meme", mode);
        Assert.Equal(["http://example.com/video", "/tmp/ws"], positional);
    }

    [Fact]
    public void Mode_standard_parsed_explicitly()
    {
        var (mode, positional) = Program.ParseArgs(["--mode", "standard", "http://example.com/video", "/tmp/ws"]);

        Assert.Equal("standard", mode);
        Assert.Equal(["http://example.com/video", "/tmp/ws"], positional);
    }

    [Fact]
    public void Mode_flag_at_end_of_args_without_value_ignored()
    {
        var (mode, positional) = Program.ParseArgs(["http://example.com/video", "/tmp/ws", "--mode"]);

        Assert.Equal("standard", mode);
        Assert.Equal(["http://example.com/video", "/tmp/ws", "--mode"], positional);
    }
}
