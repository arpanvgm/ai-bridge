using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
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
    var failedLogFile = Path.Combine(artifactsDir, FileNames.FailedPatches);

    if (!await inputService.ResolveAsync(inputFile, paste)) return;

    var rawContent = await File.ReadAllTextAsync(inputFile);
    if (File.Exists(failedLogFile)) File.Delete(failedLogFile);

    rawContent = Regex.Replace(rawContent, @"(?m)^```[a-zA-Z]*\s*$", "");
    rawContent = Regex.Replace(rawContent, @"(?m)^```\s*$", "");

    var xml = new XmlDocument();
    try { xml.LoadXml(rawContent); }
    catch (Exception ex)
    {
        logger.Error($"Error: Provided xml content is not valid XML. {ex.Message}");
        logger.Error("The entire transaction was aborted. No partial changes were applied.");
        return;
    }

    var root = xml.DocumentElement;
    if (root == null) { logger.Error("Error: No XML content found."); return; }
    if (root.Name is not (XmlTags.AiResponse or XmlTags.AiRequest or XmlTags.CreateIndex or XmlTags.UpdateIndex or XmlTags.Tracker))
    {
        logger.Error($"Error: Root element must be <{XmlTags.AiResponse}>, <{XmlTags.AiRequest}>, <{XmlTags.CreateIndex}>, <{XmlTags.UpdateIndex}>, or <{XmlTags.Tracker}>, found <{root.Name}>.");
        return;
    }

    if (root.Name == XmlTags.AiRequest) 
    { 
        var contextText = await requestService.HandleAsync((XmlElement)root, projectRoot); 
        try
        {
            await inputProvider.SetOutputContextAsync(contextText);
            logger.Info("The requested context has also been copied to your output buffer (e.g. clipboard)!");
        }
        catch { /* Suppress clipboard errors */ }

        await inputService.ResetInputFileAsync(inputFile);
        return; 
    }
    if (root.Name == XmlTags.CreateIndex) { indexService.HandleCreate(root, projectRoot); await inputService.ResetInputFileAsync(inputFile); return; }
    if (root.Name == XmlTags.UpdateIndex) { indexService.HandleUpdate(root, projectRoot); await inputService.ResetInputFileAsync(inputFile); return; }
    if (root.Name == XmlTags.Tracker) { trackerService.HandleCreate(root, projectRoot); await inputService.ResetInputFileAsync(inputFile); return; }

    var aiEditsNode = root.SelectSingleNode(XmlTags.AiEdits);
    var indexUpdateNode = root.SelectSingleNode(XmlTags.UpdateIndex);

    if (aiEditsNode != null)
    {
        var indexFileName = AIBridge.Core.Helpers.WorkspaceHelper.GetIndexFileName(projectRoot);
        var idxFile = Path.Combine(aiWorkspace, indexFileName);
        bool isAdvancedMode = File.Exists(idxFile) || indexUpdateNode != null;

        if (isAdvancedMode && indexUpdateNode == null)
        {
            logger.Error("Error: AI provided <ai-edits> but completely forgot to provide an <update-ai-bridge-index> block.");
            logger.Info("Please ask the AI to regenerate the response and include the mandatory index update block.");
            return;
        }

        var hasDeletes = aiEditsNode.SelectNodes(XmlTags.Delete)?.Count > 0;
        bool actualCreates = false;
        var fileNodes = aiEditsNode.SelectNodes(XmlTags.File);
        if (fileNodes != null)
        {
            foreach (XmlNode fileNode in fileNodes)
            {
                var relPath = fileNode.Attributes?["path"]?.Value?.Trim();
                if (!string.IsNullOrEmpty(relPath))
                {
                    var absPath = AIBridge.Core.Helpers.WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
                    if (!File.Exists(absPath)) { actualCreates = true; break; }
                }
            }
        }

        if (isAdvancedMode && (actualCreates || hasDeletes))
        {
            var hasIndexChanges = indexUpdateNode?.SelectNodes(".//file | .//delete")?.Count > 0;
            if (hasIndexChanges != true)
            {
                logger.Error("Error: AI created or deleted files in <ai-edits>, but sent an empty <update-ai-bridge-index> block.");
                logger.Info("The index must be structurally updated when files are added or removed.");
                return;
            }
        }
    }

    foreach (XmlNode node in root.ChildNodes)
    {
        if (node.NodeType == XmlNodeType.Element && node.Name != XmlTags.AiEdits && node.Name != XmlTags.UpdateIndex && node.Name != XmlTags.TrackerUpdate)
        {
            logger.Error($"Error: Unknown element '<{node.Name}>' found. Only <{XmlTags.AiEdits}>, <{XmlTags.UpdateIndex}>, and <{XmlTags.TrackerUpdate}> are allowed.");
            return;
        }
    }

    int countFullFiles = 0, countPatchOk = 0, countPatchFailed = 0, countDeleted = 0;
    var failedFiles = new List<string>();
    var failedPatchNodes = new List<XmlNode>();

    foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.File}")!)
    {
        var relPath = node.Attributes?["path"]?.Value?.Trim();
        if (string.IsNullOrEmpty(relPath)) { logger.Error("File creation failed: missing 'path' attribute."); continue; }
        var absPath = AIBridge.Core.Helpers.WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
        if (dryRun) { logger.Info($"[dry-run] Would create/overwrite: {relPath}"); countFullFiles++; continue; }
        Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
        var newContent = node.InnerText.TrimEnd('\r', '\n') + Environment.NewLine;
        await File.WriteAllTextAsync(absPath, newContent, Encoding.UTF8);
        logger.Success($"Created/Overwritten: {relPath}");
        countFullFiles++;
    }

    foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.Patch}")!)
    {
        if (dryRun) { logger.Info($"[dry-run] Would patch: {node.Attributes?["path"]?.Value?.Trim()}"); countPatchOk++; continue; }
        if (await patcherService.ApplyPatchAsync(node, projectRoot, failedFiles, failedPatchNodes)) countPatchOk++;
        else countPatchFailed++;
    }

    var deletedFileDirs = new HashSet<string>();
    foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.Delete}")!)
    {
        var relPath = node.Attributes?["path"]?.Value?.Trim();
        if (string.IsNullOrEmpty(relPath)) { logger.Error("Delete failed: missing 'path' attribute."); continue; }
        var absPath = AIBridge.Core.Helpers.WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
        if (File.Exists(absPath))
        {
            if (dryRun) { logger.Info($"[dry-run] Would delete: {relPath}"); countDeleted++; continue; }
            File.Delete(absPath);
            deletedFileDirs.Add(Path.GetDirectoryName(absPath)!);
            logger.Success($"Deleted: {relPath}");
            countDeleted++;
        }
    }

    if (countPatchFailed == 0 && indexUpdateNode is XmlElement indexUpdateElement)
        indexService.HandleUpdate(indexUpdateElement, projectRoot);

    var trackerUpdateNode = root.SelectSingleNode(XmlTags.TrackerUpdate);
    if (trackerUpdateNode != null)
        trackerService.HandleUpdate(trackerUpdateNode, projectRoot);

    if (countDeleted > 0 && !dryRun)
        CleanEmptyFolders(deletedFileDirs, projectRoot);

    logger.Info($"\nSummary: {countFullFiles} written, {countPatchOk} patched, {countDeleted} deleted.");

    if (countPatchFailed > 0)
    {
        logger.Error($"Failed patches: {countPatchFailed}. Check {failedLogFile}");
        await File.WriteAllLinesAsync(failedLogFile, failedFiles.Distinct());
        await PatcherService.RebuildResponseWithFailedPatchesAsync(inputFile, failedPatchNodes);
        logger.Warning($"⚠ ai-response.xml now contains only the {countPatchFailed} failed patch(es). Fix and re-run 'ai-bridge apply'.");
    }
    else
    {
        await inputService.ResetInputFileAsync(inputFile);
    }
}

void CleanEmptyFolders(IEnumerable<string> dirs, string rootPath)
{
    var dirsToCheck = new HashSet<string>(dirs);
    bool removedAny;
    do
    {
        removedAny = false;
        var currentDirs = dirsToCheck.ToList();
        dirsToCheck.Clear();
        foreach (var dir in currentDirs)
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                logger.Info($"Removed empty folder: {Path.GetRelativePath(rootPath, dir)}");
                removedAny = true;
                var parent = Directory.GetParent(dir)?.FullName;
                if (parent != null && parent.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) && parent != rootPath)
                    dirsToCheck.Add(parent);
            }
        }
    } while (removedAny);
}
