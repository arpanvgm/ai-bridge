using System;
using System.IO;
using System.Linq;

namespace AIBridge
{
    class Program
    {
        static void Main(string[] args)
        {
            var command = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            var flags = args.Skip(1).Select(a => a.ToLowerInvariant()).ToHashSet();

            switch (command)
            {
                case "init":
                    ConsoleHelper.Info("Initializing AI Bridge workspace...");
                    Packer.Init();
                    break;

                case "pack":
                    ConsoleHelper.Info("Packing AI context...");
                    Packer.Run();
                    break;

                case "apply":
                    ConsoleHelper.Info("Applying AI code changes...");
                    bool dryRun = flags.Contains("--dry-run");
                    bool force = flags.Contains("--force");
                    Applier.Run(dryRun, force);
                    break;

                default:
                    Console.WriteLine("Usage: ai-bridge [command]");
                    Console.WriteLine("Commands:");
                    Console.WriteLine("  init                - Creates default .aiignore, patches .gitignore, and sets up aiSkills/.");
                    Console.WriteLine("  pack                - Packs source files into text context for AI.");
                    Console.WriteLine("  apply [options]     - Applies ai-response.xml patches to the codebase.");
                    Console.WriteLine();
                    Console.WriteLine("Apply Options:");
                    Console.WriteLine("  --dry-run           - Preview changes without modifying files.");
                    Console.WriteLine("  --force             - Apply even if there are uncommitted changes in target files.");
                    break;
            }
        }
    }
}
