using System;
using System.IO;

namespace AIBridge
{
    class Program
    {
        static void Main(string[] args)
        {
            var command = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            switch (command)
            {
                case "init":
                    Console.WriteLine("Initializing AI Bridge workspace...");
                    Packer.Init();
                    break;

                case "pack":
                    Console.WriteLine("Packing AI context...");
                    Packer.Run();
                    break;

                case "apply":
                    Console.WriteLine("Applying AI code changes...");
                    Applier.Run();
                    break;

                default:
                    Console.WriteLine("Usage: ai-bridge [command]");
                    Console.WriteLine("Commands:");
                    Console.WriteLine("  init   - Creates default .aiignore and patches .gitignore.");
                    Console.WriteLine("  pack   - Packs source files into text context for AI.");
                    Console.WriteLine("  apply  - Applies ai-response.xml patches to the codebase.");
                    break;
            }
        }
    }
}
