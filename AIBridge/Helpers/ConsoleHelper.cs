using System;

namespace AIBridge.Helpers;

public static class ConsoleHelper
{
    public static void Success(string message) => WriteToStderr(message, ConsoleColor.Green);

    public static void Warning(string message) => WriteToStderr(message, ConsoleColor.Yellow);

    public static void Error(string message) => WriteToStderr(message, ConsoleColor.Red);

    public static void Info(string message) => WriteToStderr(message, ConsoleColor.Cyan);

    public static void Default(string message)
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
