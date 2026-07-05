using AIBridge.Core.Abstractions;

namespace AIBridge.Cli;

public class ConsoleLogger : IAIBridgeLogger
{
    public void Success(string message) => WriteToStderr(message, ConsoleColor.Green);
    public void Warning(string message) => WriteToStderr(message, ConsoleColor.Yellow);
    public void Error(string message) => WriteToStderr(message, ConsoleColor.Red);
    public void Info(string message) => WriteToStderr(message, ConsoleColor.Cyan);

    public void Output(string message)
    {
        Console.WriteLine(message);
    }

    private static void WriteToStderr(string message, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = previous;
    }
}
