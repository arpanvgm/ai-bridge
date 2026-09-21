
using System.CommandLine;
using AIBridge.Cli.Providers;
using AIBridge.Core.Constants;
using AIBridge.Core.Models;
using AIBridge.Core.Services;
using AIBridge.Cli.Services;

var logger = new ConsoleLogger();
var inputProvider = new ConsoleInputProvider();
var projectRoot = AIBridge.Core.Helpers.WorkspaceHelper.GetProjectRoot(Environment.CurrentDirectory);
var stateService = new StateService(projectRoot);
var projectDetector = new ProjectDetector(logger);
var inputService = new InputService(logger, inputProvider);
var patcherService = new PatcherService(logger);
var indexService = new IndexService(logger, projectDetector);
var requestService = new RequestService(logger, projectDetector, indexService);
var templateService = new TemplateService(logger);
var packerService = new PackerService(logger, projectDetector);
var trackerService = new TrackerService(logger);
var workspaceValidator = new WorkspaceValidator(stateService);
var workspaceSetupService = new WorkspaceSetupService(logger, templateService, indexService);
var applyService = new ApplyService(logger, patcherService, indexService, requestService, trackerService);
var rootCommand = new RootCommand("AI Bridge - Connects your local codebase to AI chatbots.");

// ── Pack ──
var packCommand = new Command("pack", "Packs source files into text context for AI.");
packCommand.SetHandler(async () =>
{
    if (!AssertWorkspaceValid(workspaceValidator, projectRoot)) return;
    logger.Info("Packing full AI context...");
    var result = await packerService.PackAsync(projectRoot);
    if (!result.IsSuccess) { logger.Error(result.ErrorMessage ?? "Pack failed."); Environment.ExitCode = 1; }
});

// ── Apply ──
var applyCommand = new Command("apply", "Applies ai-response.xml patches to the codebase.");
var watchOption = new Option<bool>("--watch", "Keep running and auto-apply when ai-response.xml is saved.");
var pasteOption = new Option<bool>("--paste", "Read directly from clipboard.");
applyCommand.AddOption(watchOption);
applyCommand.AddOption(pasteOption);
applyCommand.SetHandler(async (bool watch, bool paste) =>
{
    if (!AssertWorkspaceValid(workspaceValidator, projectRoot)) return;
    logger.Info("Applying AI code changes...");

    if (watch)
    {
        if (paste) { logger.Warning("Ignoring --watch flag because --paste was used."); await RunApplyAsync(paste); return; }

        logger.Info("Starting watch mode for ai-response.xml...");
        await RunApplyAsync(paste);

        var aiWorkspace = AIBridge.Core.Helpers.WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var watchDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
        if (!Directory.Exists(watchDir)) Directory.CreateDirectory(watchDir);

        using var watcher = new FileSystemWatcher(watchDir)
        {
            Filter = FileNames.ResponseXml,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        DateTime lastRun = DateTime.MinValue;
        async void OnChanged(object s, FileSystemEventArgs e)
        {
            if ((DateTime.Now - lastRun).TotalMilliseconds < Timings.WatchDebounceMs) return;
            lastRun = DateTime.Now;
            await Task.Delay(Timings.FileLockWaitMs);
            Console.WriteLine();
            logger.Info("Change detected in ai-response.xml. Applying...");
            await RunApplyAsync(paste);
            logger.Info("\nWaiting for next change... (Press Ctrl+C to exit)");
        }

        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        logger.Info("\nWaiting for next change... (Press Ctrl+C to exit)");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
        try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (TaskCanceledException) { }
    }
    else
    {
        await RunApplyAsync(paste);
    }
}, watchOption, pasteOption);

// ── Init ──
var initCommand = new Command("init", $"Scaffolds {FileNames.AiIgnore}, {FolderNames.AutoIndexMode}/, and {FolderNames.Skills}/ for a new project.");
initCommand.SetHandler(async () =>
{
    await workspaceSetupService.SetupAsync(projectRoot);
    stateService.InitState();
    logger.Success("✅ AI Bridge setup complete! Please upload the files in the 'ai-bridge/skills' folder to your AI.");
});

// ── Migrate ──
var migrateCommand = new Command("migrate", "Updates local project AI instructions to match the globally installed tool version.");
migrateCommand.SetHandler(async () =>
{
    await workspaceSetupService.SetupAsync(projectRoot);
    stateService.InitState();
    logger.Success("✅ AI Bridge migrated successfully! Please re-upload the files in the 'ai-bridge/skills' folder to your AI.");
});

rootCommand.AddCommand(packCommand);
rootCommand.AddCommand(applyCommand);
rootCommand.AddCommand(initCommand);
rootCommand.AddCommand(migrateCommand);

try
{
    var result = await rootCommand.InvokeAsync(args);
    return Environment.ExitCode != 0 ? Environment.ExitCode : result;
}
catch (Exception ex) { logger.Error($"Fatal error: {ex.Message}"); return 2; }

// ═══════════════════════════════════════════════════════════════
// Local functions
// ═══════════════════════════════════════════════════════════════

bool AssertWorkspaceValid(WorkspaceValidator validator, string root)
{
    var status = validator.Check(root);
    switch (status)
    {
        case WorkspaceStatus.NotInitialized:
            logger.Error("AI Bridge workspace is not initialized. Please run 'ai-bridge init' first.");
            Environment.ExitCode = 1;
            return false;
        case WorkspaceStatus.VersionMismatch:
            logger.Error($"AI Bridge version mismatch. The local workspace is outdated. Please run 'ai-bridge migrate' to update it.");
            Environment.ExitCode = 1;
            return false;
        default:
            return true;
    }
}

async Task RunApplyAsync(bool paste)
{
    var aiWorkspace = AIBridge.Core.Helpers.WorkspaceHelper.GetAiWorkspacePath(projectRoot);
    var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
    var inputFile = Path.Combine(artifactsDir, FileNames.ResponseXml);

    if (!await inputService.ResolveAsync(inputFile, paste)) return;

    var rawContent = await File.ReadAllTextAsync(inputFile);
    var result = await applyService.ExecuteAsync(rawContent, projectRoot);
    if (!result.IsSuccess)
        Environment.ExitCode = 1;

    // CLI-specific post-processing: copy requested context to clipboard.
    if (result.ContextPayload != null)
    {
        try
        {
            await inputProvider.SetOutputContextAsync(result.ContextPayload);
            logger.Info("The requested context has also been copied to your output buffer (e.g. clipboard)!");
        }
        catch (Exception ex)
        {
            // Clipboard APIs are unavailable in headless/SSH environments; this is safe to ignore.
            System.Diagnostics.Debug.WriteLine($"Clipboard error suppressed: {ex.Message}");
        }
    }

    // Always reset the response file after running, regardless of success or failure.
    // Since patches are not idempotent, if a run partially fails, we want the user
    // to ask the AI for a NEW response containing only the fixes, rather than
    // re-running the old file and causing previously successful patches to fail.
    await inputService.ResetInputFileAsync(inputFile);
}
