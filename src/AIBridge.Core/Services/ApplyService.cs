using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;
using AIBridge.Core.Models;

namespace AIBridge.Core.Services;

/// <summary>
/// Core engine that processes AI-generated XML responses.
/// Shared by both CLI and MCP workflows — all patching, file creation,
/// index management, and request handling flows through this service.
/// </summary>
public class ApplyService(
    IAIBridgeLogger logger,
    PatcherService patcherService,
    IndexService indexService,
    RequestService requestService,
    TrackerService trackerService)
{
    /// <summary>
    /// Processes raw XML content and applies changes to the codebase.
    /// </summary>
    /// <param name="rawContent">The raw XML string (may include markdown fences).</param>
    /// <param name="projectRoot">Absolute path to the project root directory.</param>
    /// <param name="dryRun">When true, reports what would change without writing to disk.</param>
    /// <returns>An <see cref="ApplyResult"/> describing the outcome.</returns>
    public async Task<ApplyResult> ExecuteAsync(string rawContent, string projectRoot, bool dryRun = false)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var failedLogFile = Path.Combine(aiWorkspace, FolderNames.Artifacts, FileNames.FailedPatches);
        if (File.Exists(failedLogFile)) File.Delete(failedLogFile);

        // Strip markdown code fences that AI sometimes wraps around XML
        rawContent = Regex.Replace(rawContent, @"(?m)^```[a-zA-Z]*\s*$", "");
        rawContent = Regex.Replace(rawContent, @"(?m)^```\s*$", "");

        var xml = new XmlDocument();
        try { xml.LoadXml(rawContent); }
        catch (Exception ex)
        {
            var msg = $"Error: Provided xml content is not valid XML. {ex.Message}";
            logger.Error(msg);
            logger.Error("The entire transaction was aborted. No partial changes were applied.");
            return new ApplyResult(IsSuccess: false, ErrorMessage: msg);
        }

        var root = xml.DocumentElement;
        if (root == null)
        {
            logger.Error("Error: No XML content found.");
            return new ApplyResult(IsSuccess: false, ErrorMessage: "No XML content found.");
        }

        if (root.Name is not (XmlTags.AiResponse or XmlTags.AiRequest or XmlTags.CreateIndex or XmlTags.UpdateIndex))
        {
            var msg = $"Error: Root element must be <{XmlTags.AiResponse}>, <{XmlTags.AiRequest}>, <{XmlTags.CreateIndex}>, or <{XmlTags.UpdateIndex}>, found <{root.Name}>.";
            logger.Error(msg);
            return new ApplyResult(IsSuccess: false, ErrorMessage: msg);
        }

        // ── Handle <ai-request> ──
        if (root.Name == XmlTags.AiRequest)
        {
            var contextText = await requestService.HandleAsync((XmlElement)root, projectRoot);
            return new ApplyResult(IsSuccess: true, ContextPayload: contextText);
        }

        // ── Handle <create-index> ──
        if (root.Name == XmlTags.CreateIndex)
        {
            indexService.HandleCreate(root, projectRoot);
            return new ApplyResult(IsSuccess: true);
        }

        // ── Handle <update-index> ──
        if (root.Name == XmlTags.UpdateIndex)
        {
            indexService.HandleUpdate(root, projectRoot);
            return new ApplyResult(IsSuccess: true);
        }

        // ── Handle <ai-response> ──
        var aiEditsNode = root.SelectSingleNode(XmlTags.AiEdits);
        var indexUpdateNode = root.SelectSingleNode(XmlTags.UpdateIndex);

        if (aiEditsNode != null)
        {
            var indexFileName = WorkspaceHelper.GetIndexFileName(projectRoot);
            var idxFile = Path.Combine(aiWorkspace, indexFileName);
            bool isAdvancedMode = File.Exists(idxFile) || indexUpdateNode != null;

            if (isAdvancedMode && indexUpdateNode == null)
            {
                logger.Error("Error: AI provided <ai-edits> but completely forgot to provide an <update-ai-bridge-index> block.");
                logger.Info("Please ask the AI to regenerate the response and include the mandatory index update block.");
                return new ApplyResult(IsSuccess: false, ErrorMessage: "Missing <update-ai-bridge-index> block in advanced mode.");
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
                        var absPath = WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
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
                    return new ApplyResult(IsSuccess: false, ErrorMessage: "Empty <update-ai-bridge-index> block with structural changes.");
                }
            }
        }

        // Validate no unknown top-level elements
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType == XmlNodeType.Element && node.Name != XmlTags.AiEdits && node.Name != XmlTags.UpdateIndex && node.Name != XmlTags.TrackerUpdate && node.Name != XmlTags.Tracker)
            {
                var msg = $"Error: Unknown element '<{node.Name}>' found. Only <{XmlTags.AiEdits}>, <{XmlTags.UpdateIndex}>, <{XmlTags.TrackerUpdate}>, and <{XmlTags.Tracker}> are allowed.";
                logger.Error(msg);
                return new ApplyResult(IsSuccess: false, ErrorMessage: msg);
            }
        }

        // ── Apply edits ──
        int countFullFiles = 0, countPatchOk = 0, countPatchFailed = 0, countDeleted = 0;
        var failedFiles = new List<string>();
        var failedPatchNodes = new List<XmlNode>();

        foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.File}")!)
        {
            var relPath = node.Attributes?["path"]?.Value?.Trim();
            if (string.IsNullOrEmpty(relPath)) { logger.Error("File creation failed: missing 'path' attribute."); continue; }
            var absPath = WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
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
            var absPath = WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
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

        var trackerNode = root.SelectSingleNode(XmlTags.Tracker);
        if (trackerNode != null)
            trackerService.HandleCreate(trackerNode, projectRoot);

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
        }

        return new ApplyResult(
            IsSuccess: countPatchFailed == 0,
            Created: countFullFiles,
            Patched: countPatchOk,
            Deleted: countDeleted,
            PatchFailed: countPatchFailed,
            FailedFiles: countPatchFailed > 0 ? failedFiles : null,
            FailedPatchNodes: countPatchFailed > 0 ? failedPatchNodes : null);
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
                    logger.Info($"Removed empty folder: {Path.GetRelativePath(rootPath, dir)}");
                    removedAny = true;
                    var parent = Directory.GetParent(dir)?.FullName;
                    if (parent != null && parent.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) && parent != rootPath)
                        dirsToCheck.Add(parent);
                }
            }
        } while (removedAny);
    }
}
