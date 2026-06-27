using System;
using System.IO;
using System.Linq;
using AIBridge.Core;
using AIBridge.Helpers;

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
                    if (!StateManager.EnsureUpToDate()) return;
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
                    if (!StateManager.EnsureUpToDate()) return;
                    ConsoleHelper.Info("Applying AI code changes...");
                    bool dryRun = flags.Contains("--dry-run");
                    bool watch = flags.Contains("--watch");
                    bool paste = flags.Contains("--paste");
                    Applier.Run(dryRun, watch, paste);
                    break;
                case "init":
                    if (flags.Count > 0)
                    {
                        ConsoleHelper.Error($"Error: Unknown arguments for 'init': {string.Join(", ", flags)}");
                        return;
                    }
                    ConsoleHelper.Info("Initializing AI Bridge for this project...");
                    Initializer.Init(force: false);
                    break;

                case "update":
                    if (flags.Count > 0)
                    {
                        ConsoleHelper.Error($"Error: Unknown arguments for 'update': {string.Join(", ", flags)}");
                        return;
                    }
                    ConsoleHelper.Info("Updating AI Bridge default templates...");
                    Initializer.Init(force: true);
                    break;

                case "index":
                    if (flags.Contains("--status"))
                    {
                        var invalidIndexFlags = flags.Except(new[] { "--status" }).ToList();
                        if (invalidIndexFlags.Count > 0)
                        {
                            ConsoleHelper.Error($"Error: Unknown arguments for 'index --status': {string.Join(", ", invalidIndexFlags)}");
                            return;
                        }
                        Indexer.Status();
                    }
                    else if (flags.Count > 0)
                    {
                        ConsoleHelper.Error($"Error: Unknown arguments for 'index': {string.Join(", ", flags)}");
                    }
                    else
                    {
                        Indexer.Display();
                    }
                    break;

                default:
                    Console.WriteLine("Usage: ai-bridge [command]");
                    Console.WriteLine("Commands:");
                    Console.WriteLine("  init                - Scaffolds .aiignore, aiSkills/, and aiPrompts/ for a new project.");
                    Console.WriteLine("  update              - Syncs aiSkills/ and aiPrompts/ to match the currently installed tool version.");
                    Console.WriteLine("  index               - Displays the contents of ai-bridge-index.xml.");
                    Console.WriteLine("  index --status      - Shows files changed since the last index update.");
                    Console.WriteLine("  pack                - Packs source files into text context for AI.");
                    Console.WriteLine("  apply [options]     - Applies ai-response.xml patches to the codebase.");
                    Console.WriteLine();
                    Console.WriteLine("Apply Options:");
                    Console.WriteLine("  --dry-run           - Preview changes without modifying files.");
                    Console.WriteLine("  --watch             - Keep running and auto-apply when ai-response.xml is saved.");
                    Console.WriteLine("  --paste             - Skip file, read directly from clipboard (optional — auto-detected by default).");
                    break;
            }
        }
    }
}
