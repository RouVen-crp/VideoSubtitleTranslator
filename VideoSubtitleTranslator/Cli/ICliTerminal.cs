namespace VideoSubtitleTranslator.Cli;

public interface ICliTerminal
{
    void Clear();
    ConsoleKeyInfo ReadKey();
    string? ReadLine();
}

public sealed class SystemCliTerminal : ICliTerminal
{
    public void Clear()
    {
        try { Console.Clear(); }
        catch (IOException) { }
        catch (InvalidOperationException) { }
    }

    public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);

    public string? ReadLine() => Console.ReadLine();
}

internal sealed class CliTerminalTextReader(ICliTerminal terminal) : TextReader
{
    public bool QuitRequested { get; private set; }

    public override string? ReadLine()
    {
        var line = terminal.ReadLine();
        QuitRequested = string.Equals(line?.Trim(), "q", StringComparison.OrdinalIgnoreCase);
        return line;
    }

    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ReadLine());
    }
}
