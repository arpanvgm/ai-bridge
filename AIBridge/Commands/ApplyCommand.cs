using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AIBridge.Core;
using AIBridge.Helpers;
using AIBridge.Constants;

namespace AIBridge.Commands;

/// <summary>
/// Handles applying AI-generated code patches to the workspace.
/// </summary>
public class ApplyCommand
{
    private readonly string _projectRoot;

    public ApplyCommand(string projectRoot)
    {
        _projectRoot = projectRoot;
    }

    /// <summary>
    /// Applies changes from the AI response file to the local codebase.
    /// </summary>
    public async Task RunAsync(bool watch = false, bool paste = false, bool dryRun = false)
    {
        if (watch)
        {
            if (paste)
            {
                ConsoleHelper.Warning("Ignoring --watch flag because --paste was used.");
                await ApplyInternalAsync(paste, dryRun);
                return;
            }

            ConsoleHelper.Info("Starting watch mode for ai-response.xml...");
            await ApplyInternalAsync(paste, dryRun);

            var projectRoot = _projectRoot;
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
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

                await Task.Delay(Timings.FileLockWaitMs); // debounce file lock
                Console.WriteLine();
                ConsoleHelper.Info("Change detected in ai-response.xml. Applying...");
                await ApplyInternalAsync(paste, dryRun);
                ConsoleHelper.Info("\nWaiting for next change... (Press Ctrl+C to exit)");
            }

            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;

            ConsoleHelper.Info("\nWaiting for next change... (Press Ctrl+C to exit)");

            using var cts = new System.Threading.CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            try { await Task.Delay(System.Threading.Timeout.Infinite, cts.Token); }
            catch (TaskCanceledException) { }
        }
        else
        {
            await ApplyInternalAsync(paste, dryRun);
        }
    }

    private async Task ApplyInternalAsync(bool paste, bool dryRun)
    {
        var projectPath = _projectRoot;
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
        var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
        var inputFile = Path.Combine(artifactsDir, FileNames.ResponseXml);
        var failedLogFile = Path.Combine(artifactsDir, FileNames.FailedPatches);

        // --- Step 1: Resolve input content into ai-response.xml ---
        if (!await InputResolver.ResolveAsync(inputFile, paste))
            return;

        // --- Step 2: Read and parse the file ---
        var rawContent = await File.ReadAllTextAsync(inputFile);

        if (File.Exists(failedLogFile)) File.Delete(failedLogFile);

        rawContent = Regex.Replace(rawContent, @"(?m)^```[a-zA-Z]*\s*$", "");
        rawContent = Regex.Replace(rawContent, @"(?m)^```\s*$", "");

        var xml = new XmlDocument();
        try
        {
            xml.LoadXml(rawContent);
        }
        catch (Exception ex)
        {
            ConsoleHelper.Error($"Error: Provided xml content is not valid XML. {ex.Message}");
            ConsoleHelper.Error("The entire transaction was aborted. No partial changes were applied.");
            return;
        }

        var root = xml.DocumentElement;
        if (root == null)
        {
            ConsoleHelper.Error("Error: No XML content found in ai-response.xml.");
            ConsoleHelper.Info("Paste a valid <ai-response> into the file, or use 'ai-bridge apply --paste'.");
            return;
        }
        if (root.Name is not (XmlTags.AiResponse or XmlTags.AiRequest or XmlTags.CreateIndex or XmlTags.UpdateIndex))
        {
            ConsoleHelper.Error($"Error: Root element must be <{XmlTags.AiResponse}>, <{XmlTags.AiRequest}>, <{XmlTags.CreateIndex}>, or <{XmlTags.UpdateIndex}>, found <{root.Name}>.");
            return;
        }

        // --- Step 3: Delegate to ai-request handler if needed ---
        if (root.Name == XmlTags.AiRequest)
        {
            await AiRequestHandler.HandleAsync(root, projectPath, paste);
            return;
        }

        if (root.Name == XmlTags.CreateIndex)
        {
            AiIndexHandler.HandleCreate(root, projectPath);
            await InputResolver.ResetInputFileAsync(inputFile);
            return;
        }

        if (root.Name == XmlTags.UpdateIndex)
        {
            AiIndexHandler.HandleUpdate(root, projectPath);
            await InputResolver.ResetInputFileAsync(inputFile);
            return;
        }

        // --- Step 4.5: Validate index update rules ---
        var aiEditsNode = root.SelectSingleNode(XmlTags.AiEdits);
        var indexUpdateNode = root.SelectSingleNode(XmlTags.UpdateIndex);

        if (aiEditsNode != null)
        {
            var indexFileName = WorkspaceHelper.GetIndexFileName(projectPath);
            var indexFile = Path.Combine(aiWorkspace, indexFileName);
            bool isAdvancedMode = File.Exists(indexFile) || indexUpdateNode != null;

            if (isAdvancedMode && indexUpdateNode == null)
            {
                ConsoleHelper.Error("Error: AI provided <ai-edits> but completely forgot to provide an <update-ai-bridge-index> block.");
                ConsoleHelper.Info("Please ask the AI to regenerate the response and include the mandatory index update block.");
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
                        var absPath = WorkspaceHelper.SafeResolvePath(projectPath, relPath);
                        if (!File.Exists(absPath))
                        {
                            actualCreates = true;
                            break;
                        }
                    }
                }
            }

            if (isAdvancedMode && (actualCreates || hasDeletes))
            {
                var hasIndexChanges = indexUpdateNode?.SelectNodes(".//file | .//delete")?.Count > 0;
                if (hasIndexChanges != true)
                {
                    ConsoleHelper.Error("Error: AI created or deleted files in <ai-edits>, but sent an empty <update-ai-bridge-index> block.");
                    ConsoleHelper.Info("The index must be structurally updated when files are added or removed. Please ask the AI to fix its response.");
                    return;
                }
            }
        }

        // --- Step 4: Validate child elements ---
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType == XmlNodeType.Element)
            {
                if (node.Name != XmlTags.AiEdits && node.Name != XmlTags.UpdateIndex)
                {
                    ConsoleHelper.Error($"Error: Unknown element '<{node.Name}>' found. Only <{XmlTags.AiEdits}> and <{XmlTags.UpdateIndex}> are allowed.");
                    return;
                }
            }
        }

        int countFullFiles = 0, countPatchOk = 0, countPatchFailed = 0, countDeleted = 0;
        var failedFiles = new List<string>();
        var failedPatchNodes = new List<XmlNode>();

        // --- Step 5: Process <file> elements (full file creation/overwrite) ---
        foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.File}")!)
        {
            var relPath = node.Attributes?["path"]?.Value?.Trim();
            if (string.IsNullOrEmpty(relPath))
            {
                ConsoleHelper.Error("File creation failed: missing 'path' attribute on <file> tag.");
                continue;
            }

            var absPath = WorkspaceHelper.SafeResolvePath(projectPath, relPath);

            if (dryRun)
            {
                ConsoleHelper.Info($"[dry-run] Would create/overwrite: {relPath}");
                countFullFiles++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
            var newContent = node.InnerText.TrimEnd('\r', '\n') + Environment.NewLine;
            await File.WriteAllTextAsync(absPath, newContent, Encoding.UTF8);
            ConsoleHelper.Success($"Created/Overwritten: {relPath}");
            countFullFiles++;
        }

        // --- Step 6: Process <patch> elements ---
        foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.Patch}")!)
        {
            if (dryRun)
            {
                var pRelPath = node.Attributes?["path"]?.Value?.Trim();
                ConsoleHelper.Info($"[dry-run] Would patch: {pRelPath}");
                countPatchOk++;
                continue;
            }

            if (await Patcher.ApplyPatchAsync(node, projectPath, failedFiles, failedPatchNodes))
                countPatchOk++;
            else
                countPatchFailed++;
        }

        // --- Step 7: Process <delete> elements ---
        var deletedFileDirs = new HashSet<string>();
        foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.Delete}")!)
        {
            var relPath = node.Attributes?["path"]?.Value?.Trim();
            if (string.IsNullOrEmpty(relPath))
            {
                ConsoleHelper.Error("Delete failed: missing 'path' attribute on <delete> tag.");
                continue;
            }

            var absPath = WorkspaceHelper.SafeResolvePath(projectPath, relPath);

            if (File.Exists(absPath))
            {
                if (dryRun)
                {
                    ConsoleHelper.Info($"[dry-run] Would delete: {relPath}");
                    countDeleted++;
                    continue;
                }

                File.Delete(absPath);
                deletedFileDirs.Add(Path.GetDirectoryName(absPath)!);
                ConsoleHelper.Success($"Deleted: {relPath}");
                countDeleted++;
            }
        }

        // --- Step 7.5: Process <update-ai-bridge-index> if present ---
        if (countPatchFailed == 0)
        {
            if (indexUpdateNode is XmlElement indexUpdateElement)
            {
                AiIndexHandler.HandleUpdate(indexUpdateElement, projectPath);
            }
        }

        // --- Step 8: Clean up empty folders after deletions ---
        if (countDeleted > 0 && !dryRun)
        {
            CleanEmptyFolders(deletedFileDirs, projectPath);
        }

        // --- Step 9: Summary ---
        ConsoleHelper.Info($"\nSummary: {countFullFiles} written, {countPatchOk} patched, {countDeleted} deleted.");

        if (countPatchFailed > 0)
        {
            // Write failed file paths for quick reference
            ConsoleHelper.Error($"Failed patches: {countPatchFailed}. Check {failedLogFile}");
            await File.WriteAllLinesAsync(failedLogFile, failedFiles.Distinct());

            // Rebuild ai-response.xml with ONLY failed patch blocks
            await Patcher.RebuildResponseWithFailedPatchesAsync(inputFile, failedPatchNodes);
            ConsoleHelper.Warning($"⚠ ai-response.xml now contains only the {countPatchFailed} failed patch(es). Fix and re-run 'ai-bridge apply'.");
        }
        else
        {
            await InputResolver.ResetInputFileAsync(inputFile);
        }
    }

    private void CleanEmptyFolders(IEnumerable<string> dirs, string rootPath)
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
                    ConsoleHelper.Info($"Removed empty folder: {Path.GetRelativePath(rootPath, dir)}");
                    removedAny = true;
                    
                    var parent = Directory.GetParent(dir)?.FullName;
                    // Ensure we don't accidentally scan outside of the directories we just deleted from
                    // and ensure we don't delete up to rootPath.
                    if (parent != null && parent.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) && parent != rootPath)
                    {
                        dirsToCheck.Add(parent);
                    }
                }
            }
        } while (removedAny);
    }
}
