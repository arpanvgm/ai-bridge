using System.CommandLine;
using AIBridge.Cli.Providers;
using AIBridge.Cli.Helpers;
using AIBridge.Core.Constants;
using AIBridge.Core.Models;
using AIBridge.Core.Services;
using AIBridge.Cli.Services;

var logger = new ConsoleLogger();
var inputProvider = new ConsoleInputProvider();
var projectRoot = WorkspaceHelper.GetProjectRoot();
var stateService = new StateService(projectRoot, logger);
var projectDetector = new ProjectDetector(logger);
var inputService = new InputService(logger, inputProvider);
var patcherService = new PatcherService(logger);
var indexService = new IndexService(logger);
var requestService = new RequestService(logger, projectDetector);
var templateService = new TemplateService(logger);
var packerService = new PackerService(logger, projectDetector);
var indexStatusService = new IndexStatusService(logger);
var trackerService = new TrackerService(logger);
var applyService = new ApplyService(logger, patcherService, indexService, requestService, trackerService);
var rootCommand = new RootCommand("AI Bridge - Connects your local codebase to AI chatbots.");

// ── Pack ──
var packCommand = new Command("pack", "Packs source files into text context for AI.");
var incrementalOption = new Option<bool>("--incremental", "Pack only files modified or added since the last index update.");
packCommand.AddOption(incrementalOption);
packCommand.SetHandler(async (bool incremental) =>
{
    if (!stateService.EnsureUpToDate()) { Environment.ExitCode = 1; return; }
    logger.Info(incremental ? "Packing incremental AI context..." : "Packing full AI context...");
    var result = await packerService.PackAsync(projectRoot, new PackOptions(Incremental: incremental));
    if (!result.IsSuccess) { logger.Error(result.ErrorMessage ?? "Pack failed."); Environment.ExitCode = 1; }
}, incrementalOption);

// ── Apply ──
var applyCommand = new Command("apply", "Applies ai-response.xml patches to the codebase.");
var watchOption = new Option<bool>("--watch", "Keep running and auto-apply when ai-response.xml is saved.");
var pasteOption = new Option<bool>("--paste", "Read directly from clipboard.");
var dryRunOption = new Option<bool>("--dry-run", "Show what would change without applying.");
applyCommand.AddOption(watchOption);
applyCommand.AddOption(pasteOption);
applyCommand.AddOption(dryRunOption);
applyCommand.SetHandler(async (bool watch, bool paste, bool dryRun) =>
{
    if (!stateService.EnsureUpToDate()) { Environment.ExitCode = 1; return; }
    logger.Info("Applying AI code changes...");

    if (watch)
    {
        if (paste) { logger.Warning("Ignoring --watch flag because --paste was used."); await RunApplyAsync(paste, dryRun); return; }

        logger.Info("Starting watch mode for ai-response.xml...");
        await RunApplyAsync(paste, dryRun);

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
            await RunApplyAsync(paste, dryRun);
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
        await RunApplyAsync(paste, dryRun);
    }
}, watchOption, pasteOption, dryRunOption);

// ── Init ──
var initCommand = new Command("init", $"Scaffolds {FileNames.AiIgnore}, {FolderNames.SimpleMode}/, {FolderNames.AdvancedMode}/, and {FolderNames.Skills}/ for a new project.");
initCommand.SetHandler(async () =>
{
    logger.Info("Initializing AI Bridge for this project...");
    await RunInitAsync(force: false);
});

// ── Update ──
var updateCommand = new Command("update", $"Syncs {FolderNames.SimpleMode}/, {FolderNames.AdvancedMode}/, and {FolderNames.Skills}/ to match the currently installed tool version.");
updateCommand.SetHandler(async () =>
{
    logger.Info("Updating AI Bridge default templates...");
    await RunInitAsync(force: true);
});

// ── Index ──
var indexCommand = new Command("index", "Commands for managing your project index.");
var statusCommand = new Command("status", "Shows files changed since the last index update.");
indexCommand.AddCommand(statusCommand);
statusCommand.SetHandler(async () => { await indexStatusService.StatusAsync(projectRoot); });

rootCommand.AddCommand(packCommand);
rootCommand.AddCommand(applyCommand);
rootCommand.AddCommand(initCommand);
rootCommand.AddCommand(updateCommand);
rootCommand.AddCommand(indexCommand);

try { return await rootCommand.InvokeAsync(args); }
catch (Exception ex) { logger.Error($"Fatal error: {ex.Message}"); return 2; }

// ═══════════════════════════════════════════════════════════
// Local functions
// ═══════════════════════════════════════════════════════════

async Task RunInitAsync(bool force)
{
    var aiWorkspace = AIBridge.Core.Helpers.WorkspaceHelper.GetAiWorkspacePath(projectRoot);
    var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
    if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

    var responseFilePath = Path.Combine(artifactsDir, FileNames.ResponseXml);
    if (!File.Exists(responseFilePath))
        await File.WriteAllTextAsync(responseFilePath, "<!-- Paste the AI response XML here -->\n");

    var innerGitignorePath = Path.Combine(aiWorkspace, ".gitignore");
    var innerGitignoreContent = $"# Ignore templates and artifacts to prevent Git conflicts\n{FolderNames.Artifacts}/\n{FolderNames.SimpleMode}/\n{FolderNames.AdvancedMode}/\n{FolderNames.Skills}/\n";
    await File.WriteAllTextAsync(innerGitignorePath, innerGitignoreContent);

    var dockerignorePath = Path.Combine(projectRoot, ".dockerignore");
    if (File.Exists(dockerignorePath))
    {
        var content = await File.ReadAllTextAsync(dockerignorePath);
        if (!content.Contains($"{FolderNames.AiBridge}/"))
        {
            await File.AppendAllTextAsync(dockerignorePath, $"\n# AI Bridge\n{FolderNames.AiBridge}/\n");
            logger.Success("✅ Patched .dockerignore to exclude AI Bridge workspace from Docker builds.");
        }
    }

    var aiIgnorePath = Path.Combine(projectRoot, FileNames.AiIgnore);
    if (!File.Exists(aiIgnorePath))
    {
        var defaultIgnore = $"# =================================================================\n" +
                            $"# AI BRIDGE IGNORE FILE\n" +
                            $"# =================================================================\n" +
                            $"# NOTE: Everything in your .gitignore is ALREADY ignored by AI Bridge!\n" +
                            $"# Do not copy your .gitignore here.\n" +
                            $"#\n" +
                            $"# ONLY add files to this list if they are currently tracked by Git,\n" +
                            $"# but you want to hide them from the AI to save tokens (e.g. huge\n" +
                            $"# JSON test data, generated code) or to protect sensitive secrets.\n" +
                            $"# =================================================================\n" +
                            $"TestResults/\n*.g.cs\n*.log\n*.tmp\n";
        await File.WriteAllTextAsync(aiIgnorePath, defaultIgnore);
        logger.Success("✅ Created default .aiignore file.");
    }
    else { logger.Info("ℹ .aiignore already exists."); }

    var simpleModeDir = Path.Combine(aiWorkspace, FolderNames.SimpleMode);
    var advancedModeDir = Path.Combine(aiWorkspace, FolderNames.AdvancedMode);
    if (force)
    {
        if (Directory.Exists(simpleModeDir)) Directory.Delete(simpleModeDir, true);
        if (Directory.Exists(advancedModeDir)) Directory.Delete(advancedModeDir, true);
    }

    templateService.ExtractTemplates(aiWorkspace, force, projectRoot);
    stateService.InitState();
}

async Task RunApplyAsync(bool paste, bool dryRun)
{
    var aiWorkspace = AIBridge.Core.Helpers.WorkspaceHelper.GetAiWorkspacePath(projectRoot);
    var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
    var inputFile = Path.Combine(artifactsDir, FileNames.ResponseXml);

    if (!await inputService.ResolveAsync(inputFile, paste)) return;

    var rawContent = await File.ReadAllTextAsync(inputFile);
    var result = await applyService.ExecuteAsync(rawContent, projectRoot, dryRun);

    // CLI-specific post-processing: copy requested context to clipboard
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

