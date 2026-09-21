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
    /// <returns>An <see cref="ApplyResult"/> describing the outcome.</returns>
    public async Task<ApplyResult> ExecuteAsync(string rawContent, string projectRoot)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);

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

        if (root.Name is not (XmlTags.AiResponse or XmlTags.AiRequest))
        {
            var msg = $"Error: Root element must be <{XmlTags.AiResponse}> or <{XmlTags.AiRequest}>, found <{root.Name}>.";
            logger.Error(msg);
            return new ApplyResult(IsSuccess: false, ErrorMessage: msg);
        }

        // ── Handle <ai-request> ──
        if (root.Name == XmlTags.AiRequest)
        {
            var contextText = await requestService.HandleAsync((XmlElement)root, projectRoot);
            return new ApplyResult(IsSuccess: true, ContextPayload: contextText);
        }

        // ── Handle <ai-response> ──
        var aiEditsNode = root.SelectSingleNode(XmlTags.AiEdits);
        var indexUpdateNode = root.SelectSingleNode(XmlTags.UpdateIndex);
        var indexCreateNode = root.SelectSingleNode(XmlTags.CreateIndex);



        // Validate no unknown top-level elements inside <ai-response>
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType == XmlNodeType.Element && node.Name != XmlTags.AiEdits && node.Name != XmlTags.CreateIndex && node.Name != XmlTags.UpdateIndex && node.Name != XmlTags.Tracker)
            {
                var msg = $"Error: Unknown element '<{node.Name}>' found inside <{XmlTags.AiResponse}>. Allowed children: <{XmlTags.AiEdits}>, <{XmlTags.CreateIndex}>, <{XmlTags.UpdateIndex}>, <{XmlTags.Tracker}>.";
                logger.Error(msg);
                return new ApplyResult(IsSuccess: false, ErrorMessage: msg);
            }
        }

        // ── Apply edits ──
        int countFullFiles = 0, countPatchOk = 0, countPatchFailed = 0, countDeleted = 0;
        var failedFiles = new List<string>();
        var errors = new List<string>();

        // SelectNodes only returns null when called on a null context node; root is non-null here.
        foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.File}")!)
        {
            var relPath = node.Attributes?["path"]?.Value?.Trim();
            if (string.IsNullOrEmpty(relPath))
            {
                var err = "File creation failed: missing 'path' attribute.";
                logger.Error(err);
                errors.Add(err);
                continue;
            }
            var absPath = WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
            var newContent = node.InnerText.TrimEnd('\r', '\n') + Environment.NewLine;
            await File.WriteAllTextAsync(absPath, newContent, Encoding.UTF8);
            logger.Success($"Created/Overwritten: {relPath}");
            countFullFiles++;
        }

        // SelectNodes only returns null when called on a null context node; root is non-null here.
        foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.Patch}")!)
        {
            if (await patcherService.ApplyPatchAsync(node, projectRoot, failedFiles)) countPatchOk++;
            else countPatchFailed++;
        }

        var deletedFileDirs = new HashSet<string>();
        // SelectNodes only returns null when called on a null context node; root is non-null here.
        foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.Delete}")!)
        {
            var relPath = node.Attributes?["path"]?.Value?.Trim();
            if (string.IsNullOrEmpty(relPath))
            {
                var err = "Delete failed: missing 'path' attribute.";
                logger.Error(err);
                errors.Add(err);
                continue;
            }
            var absPath = WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
            if (File.Exists(absPath))
            {
                File.Delete(absPath);
                deletedFileDirs.Add(Path.GetDirectoryName(absPath)!);
                logger.Success($"Deleted: {relPath}");
                countDeleted++;
            }
        }

        if (countPatchFailed == 0 && indexCreateNode is XmlElement indexCreateElement)
            indexService.HandleCreate(indexCreateElement, projectRoot);

        if (countPatchFailed == 0 && indexUpdateNode is XmlElement indexUpdateElement)
            indexService.HandleUpdate(indexUpdateElement, projectRoot);

        var trackerNode = root.SelectSingleNode(XmlTags.Tracker);
        if (trackerNode != null)
            trackerService.HandleTracker(trackerNode, projectRoot);

        if (countDeleted > 0)
            CleanEmptyFolders(deletedFileDirs, projectRoot);

        logger.Info($"\nSummary: {countFullFiles} written, {countPatchOk} patched, {countDeleted} deleted.");

        bool hasEdits = aiEditsNode != null && (countFullFiles > 0 || countPatchOk > 0 || countDeleted > 0);
        bool indexExists = File.Exists(Path.Combine(aiWorkspace, FileNames.Index));
        if (hasEdits && indexUpdateNode == null && indexExists)
            logger.Warning($"⚠ Index not updated. The AI response was missing an <{XmlTags.UpdateIndex}> tag. Please ensure that the index is updated.");

        if (countPatchFailed > 0)
            foreach (var f in failedFiles.Distinct())
                logger.Error($"Patch failed: {f}");

        bool isSuccess = countPatchFailed == 0 && errors.Count == 0;
        return new ApplyResult(
            IsSuccess: isSuccess,
            Created: countFullFiles,
            Patched: countPatchOk,
            Deleted: countDeleted,
            PatchFailed: countPatchFailed,
            FailedFiles: countPatchFailed > 0 ? failedFiles : null,
            Errors: errors.Count > 0 ? errors : null);
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
