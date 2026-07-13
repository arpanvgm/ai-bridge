using System.Diagnostics;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class IndexStatusService(IAIBridgeLogger logger)
{

    public async Task<(List<string> modified, List<string> newFiles, List<string> deleted, DateTime lastUpdated)> GetChangedFilesAsync(string projectRoot)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var indexFileName = WorkspaceHelper.GetIndexFileName(projectRoot);
        var indexFile = Path.Combine(aiWorkspace, indexFileName);

        List<string> modifiedFiles = [], newFiles = [], deletedFiles = [];

        if (!File.Exists(indexFile))
            throw new Exception($"Error: {indexFileName} not found. Run 'ai-bridge init' and create your index first.");

        var xml = new XmlDocument();
        try { xml.Load(indexFile); }
        catch (Exception ex) { throw new Exception($"Error parsing {indexFileName}: {ex.Message}"); }

        var indexRoot = xml.DocumentElement;
        if (indexRoot == null) throw new Exception($"Error: {indexFileName} is malformed.");

        var lastUpdatedStr = indexRoot.GetAttribute("lastUpdated");
        if (string.IsNullOrEmpty(lastUpdatedStr) || !DateTime.TryParse(lastUpdatedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime lastUpdated))
            throw new Exception($"Warning: No 'lastUpdated' attribute found on {indexFileName}. Cannot determine status.");
        lastUpdated = lastUpdated.ToUniversalTime();

        var indexedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNodes = indexRoot.SelectNodes("//file[@path]");
        if (fileNodes != null)
        {
            foreach (XmlElement fileNode in fileNodes)
            {
                var path = fileNode.GetAttribute("path");
                if (!string.IsNullOrEmpty(path)) indexedPaths.Add(path);
            }
        }

        foreach (var relativePath in indexedPaths)
        {
            var absolutePath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(absolutePath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(absolutePath);
                if (lastWrite > lastUpdated) modifiedFiles.Add(relativePath);
            }
            else
            {
                deletedFiles.Add(relativePath);
            }
        }

        var aiIgnorePath = Path.Combine(projectRoot, ".aiignore");
        var (aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns) = FileFilterHelper.LoadAiIgnoreRules(aiIgnorePath);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "ls-files --cached --others --exclude-standard",
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    var gitFiles = output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var gitFile in gitFiles)
                    {
                        var relativePath = gitFile.Replace('\\', '/');
                        if (relativePath.StartsWith("ai-bridge-", StringComparison.OrdinalIgnoreCase)) continue;
                        var fileName = Path.GetFileName(relativePath);
                        var ext = Path.GetExtension(relativePath);
                        if (FileFilterHelper.BinaryExtensions.Contains(ext)) continue;
                        if (FileFilterHelper.ExcludeFileNames.Contains(fileName)) continue;
                        if (FileFilterHelper.IsAiIgnored(relativePath, fileName, aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns)) continue;
                        if (!indexedPaths.Contains(relativePath)) newFiles.Add(relativePath);
                    }
                }
            }
        }
        catch (Exception ex) { logger.Warning($"Warning: Could not run git. Skipping new file detection. ({ex.Message})"); }

        return (modifiedFiles, newFiles, deletedFiles, lastUpdated);
    }

    public async Task StatusAsync(string projectRoot)
    {
        var indexFileName = WorkspaceHelper.GetIndexFileName(projectRoot);

        List<string> modifiedFiles, newFilesList, deletedFiles;
        DateTime lastUpdated;

        try { (modifiedFiles, newFilesList, deletedFiles, lastUpdated) = await GetChangedFilesAsync(projectRoot); }
        catch (Exception ex) { logger.Error(ex.Message); return; }

        logger.Info($"📋 {indexFileName}  (Last updated: {lastUpdated:yyyy-MM-dd HH:mm:ss UTC})");

        if (modifiedFiles.Count == 0 && newFilesList.Count == 0 && deletedFiles.Count == 0)
        {
            logger.Success("✅ Index is up to date. No changes detected.");
            return;
        }

        if (modifiedFiles.Count > 0)
        {
            logger.Warning($"⚠ {modifiedFiles.Count} file(s) modified since last index update:");
            foreach (var path in modifiedFiles)
            {
                var absolutePath = Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar));
                var modified = File.GetLastWriteTimeUtc(absolutePath);
                logger.Output($"  • {path}  (modified {modified:yyyy-MM-dd HH:mm:ss UTC})");
            }
        }

        if (newFilesList.Count > 0)
        {
            logger.Warning($"➕ {newFilesList.Count} new file(s) not in index:");
            foreach (var path in newFilesList) logger.Output($"  • {path}");
        }

        if (deletedFiles.Count > 0)
        {
            logger.Warning($"🗑️ {deletedFiles.Count} file(s) in index no longer exist on disk:");
            foreach (var path in deletedFiles) logger.Output($"  • {path}  (deleted)");
        }

        int totalChanges = modifiedFiles.Count + newFilesList.Count + deletedFiles.Count;
        logger.Info($"\nSummary: {modifiedFiles.Count} modified, {newFilesList.Count} new, {deletedFiles.Count} deleted ({totalChanges} total change(s))");
    }
}
