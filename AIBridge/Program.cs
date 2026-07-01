using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIBridge.Core;
using AIBridge.Commands;
using AIBridge.Helpers;
using AIBridge.Constants;

var rootCommand = new RootCommand("AI Bridge - Connects your local codebase to AI chatbots.");

// Pack Command
var packCommand = new Command("pack", "Packs source files into text context for AI.");
var incrementalOption = new Option<bool>("--incremental", "Pack only files modified or added since the last index update.");
var projectRoot = WorkspaceHelper.GetProjectRoot();
var packCommandInstance = new PackCommand(projectRoot);
var applyCommandInstance = new ApplyCommand(projectRoot);
var initCommandInstance = new InitCommand(projectRoot);
var indexCommandInstance = new IndexCommand(projectRoot);

packCommand.AddOption(incrementalOption);
packCommand.SetHandler(async (bool incremental) =>
{
    if (!StateManager.EnsureUpToDate()) { Environment.ExitCode = 1; return; }
    ConsoleHelper.Info(incremental ? "Packing incremental AI context..." : "Packing full AI context...");
    await packCommandInstance.RunAsync(incremental);
}, incrementalOption);

// Apply Command
var applyCommand = new Command("apply", "Applies ai-response.xml patches to the codebase.");
var watchOption = new Option<bool>("--watch", "Keep running and auto-apply when ai-response.xml is saved.");
var pasteOption = new Option<bool>("--paste", "Skip file, read directly from clipboard (optional — auto-detected by default).");
var dryRunOption = new Option<bool>("--dry-run", "Show what files would be created/patched/deleted without actually making changes.");
applyCommand.AddOption(watchOption);
applyCommand.AddOption(pasteOption);
applyCommand.AddOption(dryRunOption);
applyCommand.SetHandler(async (bool watch, bool paste, bool dryRun) =>
{
    if (!StateManager.EnsureUpToDate()) { Environment.ExitCode = 1; return; }
    ConsoleHelper.Info("Applying AI code changes...");
    await applyCommandInstance.RunAsync(watch, paste, dryRun);
}, watchOption, pasteOption, dryRunOption);

// Init Command
var initCommand = new Command("init", $"Scaffolds {FileNames.AiIgnore}, {FolderNames.SimpleMode}/, and {FolderNames.AdvancedMode}/ for a new project.");
initCommand.SetHandler(async () =>
{
    ConsoleHelper.Info("Initializing AI Bridge for this project...");
    await initCommandInstance.InitAsync(force: false);
});

// Update Command
var updateCommand = new Command("update", $"Syncs {FolderNames.SimpleMode}/ and {FolderNames.AdvancedMode}/ to match the currently installed tool version.");
updateCommand.SetHandler(async () =>
{
    ConsoleHelper.Info("Updating AI Bridge default templates...");
    await initCommandInstance.InitAsync(force: true);
});

// Index Command
var indexCommand = new Command("index", "Displays the contents of the index XML file.");
var statusCommand = new Command("status", "Shows files changed since the last index update.");
indexCommand.AddCommand(statusCommand);

indexCommand.SetHandler(() =>
{
    indexCommandInstance.Display();
});

statusCommand.SetHandler(async () =>
{
    await indexCommandInstance.StatusAsync();
});

rootCommand.AddCommand(packCommand);
rootCommand.AddCommand(applyCommand);
rootCommand.AddCommand(initCommand);
rootCommand.AddCommand(updateCommand);
rootCommand.AddCommand(indexCommand);

try
{
    return await rootCommand.InvokeAsync(args);
}
catch (Exception ex)
{
    ConsoleHelper.Error($"Fatal error: {ex.Message}");
    return 2;
}
