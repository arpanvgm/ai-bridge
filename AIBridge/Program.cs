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
                case "pack":
                    if (flags.Count > 0)
                    {
                        ConsoleHelper.Error($"Error: Unknown arguments for 'pack': {string.Join(", ", flags)}");
                        return;
                    }
                    ConsoleHelper.Info("Packing AI context...");
                    Packer.Run();
                    break;

                case "apply":
                    var allowedApplyFlags = new[] { "--dry-run", "--watch", "--paste" };
                    var invalidApplyFlags = flags.Except(allowedApplyFlags).ToList();
                    if (invalidApplyFlags.Count > 0)
                    {
                        ConsoleHelper.Error($"Error: Unknown arguments for 'apply': {string.Join(", ", invalidApplyFlags)}");
                        return;
                    }
                    ConsoleHelper.Info("Applying AI code changes...");
                    bool dryRun = flags.Contains("--dry-run");
                    bool watch = flags.Contains("--watch");
                    bool paste = flags.Contains("--paste");
                    Applier.Run(dryRun, watch, paste);
                    break;

                default:
                    Console.WriteLine("Usage: ai-bridge [command]");
                    Console.WriteLine("Commands:");
                    Console.WriteLine("  pack                - Packs source files into text context for AI (auto-initializes on first run).");
                    Console.WriteLine("  apply [options]     - Applies ai-response.xml patches to the codebase.");
                    Console.WriteLine();
                    Console.WriteLine("Apply Options:");
                    Console.WriteLine("  --dry-run           - Preview changes without modifying files.");
                    Console.WriteLine("  --watch             - Keep running and auto-apply when ai-response.xml is saved.");
                    Console.WriteLine("  --paste             - Read AI response from clipboard instead of file.");
                    break;
            }
        }
    }
}
