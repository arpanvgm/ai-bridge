using System;

namespace AIBridge
{
    public static class ConsoleHelper
    {
        public static void Success(string message)
        {
            WriteColored(message, ConsoleColor.Green);
        }

        public static void Warning(string message)
        {
            WriteColored(message, ConsoleColor.Yellow);
        }

        public static void Error(string message)
        {
            WriteColored(message, ConsoleColor.Red);
        }

        public static void Info(string message)
        {
            WriteColored(message, ConsoleColor.Cyan);
        }

        public static void Default(string message)
        {
            Console.WriteLine(message);
        }

        public static void WriteColored(string message, ConsoleColor color)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = previous;
        }
    }
}
