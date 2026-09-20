
using AIBridge.Core.Abstractions;

namespace AIBridge.Mcp.Providers;

/// <summary>
/// Logger used by the migrate subcommand, which runs outside the DI container.
/// Writes directly to the console with colour.
/// </summary>
public class ConsoleLogger : IAIBridgeLogger
{
    public void Success(string message) => Write(message, ConsoleColor.Green);
    public void Info(string message)    => Write(message, ConsoleColor.Cyan);
    public void Warning(string message) => Write(message, ConsoleColor.Yellow);
    public void Error(string message)   => Write(message, ConsoleColor.Red);
    public void Output(string message)  => Console.WriteLine(message);

    private static void Write(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
