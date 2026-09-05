using System.Diagnostics;
using System.Text;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class IndexService(IAIBridgeLogger logger, ProjectDetector projectDetector)
{
    public async Task GenerateIndexAsync(string projectRoot)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var indexFileName = WorkspaceHelper.GetIndexFileName(projectRoot);
        var indexFile = Path.Combine(aiWorkspace, indexFileName);
        var aiIgnorePath = Path.Combine(projectRoot, FileNames.AiIgnore);

        var (detectedProjects, _) = projectDetector.DetectProjects(projectRoot);
        var rootFolderName = new DirectoryInfo(projectRoot).Name;
        var warnings = new List<string>();

        var allFiles = await FileFilterHelper.GetTrackedFilesAsync(projectRoot, logger);

        var (aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns) = FileFilterHelper.LoadAiIgnoreRules(aiIgnorePath);

        var indexData = new Dictionary<string, List<string>>();
        int totalFileCount = 0;

        foreach (var file in allFiles.OrderBy(f => f))
        {
            var relativePath = Path.GetRelativePath(projectRoot, file).Replace("\\", "/");
            var fileName = Path.GetFileName(file);
            var extension = Path.GetExtension(file);

            if (FileFilterHelper.AlwaysExcludePrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (FileFilterHelper.BinaryExtensions.Contains(extension)) continue;
            if (FileFilterHelper.ExcludeFileNames.Contains(fileName)) continue;
            if (FileFilterHelper.IsAiIgnored(relativePath, fileName, aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns)) continue;

            string projectName = rootFolderName;
            foreach (var proj in detectedProjects)
            {
                if (file.StartsWith(proj.DirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    projectName = proj.Name;
                    break;
                }
            }

            if (!indexData.TryGetValue(projectName, out var fileList))
            {
                fileList = new List<string>();
                indexData[projectName] = fileList;
            }

            fileList.Add(relativePath);
            totalFileCount++;
        }

        var doc = new XmlDocument();
        XmlNode? indexRoot = null;
        int addedCount = 0;
        int preservedCount = 0;

        if (File.Exists(indexFile))
        {
            try
            {
                doc.Load(indexFile);
                indexRoot = doc.DocumentElement;
            }
            catch (Exception ex)
            {
                logger.Warning($"⚠ Failed to load existing index: {ex.Message}. Rebuilding from scratch.");
                doc = new XmlDocument();
            }
        }

        if (indexRoot == null || indexRoot.Name != "ai-bridge-index")
        {
            indexRoot = doc.CreateElement("ai-bridge-index");
            doc.AppendChild(indexRoot);
        }

        indexRoot.Attributes?.RemoveNamedItem("lastUpdated");
        var attr = doc.CreateAttribute("lastUpdated");
        attr.Value = DateTime.UtcNow.ToString("o");
        indexRoot.Attributes?.Append(attr);

        foreach (var kvp in indexData.OrderBy(k => k.Key))
        {
            var moduleName = kvp.Key;
            var targetModule = indexRoot.SelectSingleNode($"{XmlTags.Module}[@name='{moduleName}']");
            if (targetModule == null)
            {
                targetModule = doc.CreateElement(XmlTags.Module);
                var nameAttr = doc.CreateAttribute("name");
                nameAttr.Value = moduleName;
                targetModule.Attributes?.Append(nameAttr);
                indexRoot.AppendChild(targetModule);
            }

            foreach (var fileItem in kvp.Value)
            {
                var existingFile = targetModule.SelectSingleNode($"{XmlTags.File}[@path='{fileItem}']") as XmlElement;
                if (existingFile == null)
                {
                    var fileNode = doc.CreateElement(XmlTags.File);
                    fileNode.SetAttribute("path", fileItem);
                    fileNode.SetAttribute("purpose", "");
                    targetModule.AppendChild(fileNode);
                    addedCount++;
                }
                else
                {
                    preservedCount++;
                }
            }
        }

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(false)
        };

        using (var writer = XmlWriter.Create(indexFile, settings))
        {
            doc.Save(writer);
        }

        if (preservedCount > 0)
            logger.Success($"✅ Synced {indexFileName}: {addedCount} new files added, {preservedCount} existing summaries preserved.");
        else
            logger.Success($"✅ Generated new index at {indexFileName} tracking {addedCount} files.");
    }

    public void HandleCreate(XmlNode root, string projectPath)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
        var indexFile = Path.Combine(aiWorkspace, WorkspaceHelper.GetIndexFileName(projectPath));

        var doc = new XmlDocument();
        var indexRoot = doc.CreateElement("ai-bridge-index");
        indexRoot.SetAttribute("lastUpdated", DateTime.UtcNow.ToString("o"));

        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType == XmlNodeType.Element)
            {
                var importedNode = doc.ImportNode(node, true);
                indexRoot.AppendChild(importedNode);
            }
        }

        doc.AppendChild(indexRoot);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(false)
        };

        using (var writer = XmlWriter.Create(indexFile, settings))
        {
            doc.Save(writer);
        }

        logger.Success("✅ Generated new ai-bridge-index.xml successfully.");
    }

    public void HandleUpdate(XmlNode root, string projectPath)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
        var indexFileName = WorkspaceHelper.GetIndexFileName(projectPath);
        var indexFile = Path.Combine(aiWorkspace, indexFileName);

        if (!File.Exists(indexFile))
        {
            var emptyDoc = new XmlDocument();
            var emptyRoot = emptyDoc.CreateElement("ai-bridge-index");
            emptyDoc.AppendChild(emptyRoot);
            emptyDoc.Save(indexFile);
        }

        var xml = new XmlDocument();
        try { xml.Load(indexFile); }
        catch (Exception ex)
        {
            logger.Error($"Error parsing existing {indexFileName}: {ex.Message}");
            return;
        }

        var indexRoot = xml.DocumentElement;
        if (indexRoot == null || indexRoot.Name != "ai-bridge-index")
        {
            logger.Error($"Error: {indexFileName} is malformed (missing <ai-bridge-index> root).");
            return;
        }

        int updatedCount = 0, addedCount = 0, deletedCount = 0;

        var deleteNodes = root.SelectNodes($"//{XmlTags.Delete}");
        if (deleteNodes != null)
        {
            foreach (XmlNode delNode in deleteNodes)
            {
                var path = delNode.Attributes?["path"]?.Value;
                if (!string.IsNullOrEmpty(path))
                {
                    var targetNode = indexRoot.SelectSingleNode($"//{XmlTags.File}[@path='{path}']");
                    if (targetNode != null)
                    {
                        var moduleNode = targetNode.ParentNode;
                        moduleNode?.RemoveChild(targetNode);
                        deletedCount++;
                        if (moduleNode != null && moduleNode.SelectNodes(XmlTags.File)?.Count == 0)
                            indexRoot.RemoveChild(moduleNode);
                    }
                }
            }
        }

        var moduleNodes = root.SelectNodes(XmlTags.Module);
        if (moduleNodes != null)
        {
            foreach (XmlNode moduleNode in moduleNodes)
            {
                var moduleName = moduleNode.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(moduleName)) continue;

                var targetModule = indexRoot.SelectSingleNode($"{XmlTags.Module}[@name='{moduleName}']");
                if (targetModule == null)
                {
                    targetModule = xml.CreateElement(XmlTags.Module);
                    var nameAttr = xml.CreateAttribute("name");
                    nameAttr.Value = moduleName;
                    targetModule.Attributes?.Append(nameAttr);
                    indexRoot.AppendChild(targetModule);
                }

                foreach (XmlNode fileNode in moduleNode.SelectNodes(XmlTags.File)!)
                {
                    var path = fileNode.Attributes?["path"]?.Value;
                    if (string.IsNullOrEmpty(path)) continue;

                    var purpose = fileNode.Attributes?["purpose"]?.Value;
                    if (string.IsNullOrEmpty(purpose))
                        purpose = fileNode.InnerText.Trim();

                    var targetFile = targetModule?.SelectSingleNode($"{XmlTags.File}[@path='{path}']") as XmlElement;

                    if (targetFile != null)
                    {
                        targetFile.SetAttribute("purpose", purpose);
                        updatedCount++;
                    }
                    else
                    {
                        targetFile = xml.CreateElement(XmlTags.File);
                        targetFile.SetAttribute("path", path);
                        targetFile.SetAttribute("purpose", purpose);
                        targetModule?.AppendChild(targetFile);
                        addedCount++;
                    }
                }
            }
        }

        var floatingFiles = root.SelectNodes(XmlTags.File);
        if (floatingFiles != null)
        {
            foreach (XmlNode fileNode in floatingFiles)
            {
                var path = fileNode.Attributes?["path"]?.Value;
                if (string.IsNullOrEmpty(path)) continue;
                var purpose = fileNode.Attributes?["purpose"]?.Value;
                if (string.IsNullOrEmpty(purpose)) purpose = fileNode.InnerText.Trim();

                var targetFile = indexRoot.SelectSingleNode($"//{XmlTags.File}[@path='{path}']") as XmlElement;
  
                if (targetFile != null)
                {
                    targetFile.SetAttribute("purpose", purpose);
                    updatedCount++;
                }
                else
                {
                    logger.Warning($"Cannot add '{path}' without a <module>. Please wrap new files in a <module>.");
                }
            }
        }

        indexRoot.SetAttribute("lastUpdated", DateTime.UtcNow.ToString("o"));

        var settings = new XmlWriterSettings
        {
            Indent = true, IndentChars = "  ",
            OmitXmlDeclaration = false, Encoding = new UTF8Encoding(false)
        };
        using (var writer = XmlWriter.Create(indexFile, settings))
        {
            xml.Save(writer);
        }

        logger.Success($"✅ Updated {indexFileName}: {addedCount} added, {updatedCount} updated, {deletedCount} deleted.");
    }

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
        var fileNodes = indexRoot.SelectNodes("//" + XmlTags.File + "[@path]");
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
