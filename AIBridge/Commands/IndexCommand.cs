using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using AIBridge.Helpers;

namespace AIBridge.Commands
{
    public static class IndexCommand
    {


        public static void Display()
        {
            var projectRoot = WorkspaceHelper.GetProjectRoot();
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
            var indexFileName = WorkspaceHelper.GetIndexFileName(projectRoot);
            var indexFile = Path.Combine(aiWorkspace, indexFileName);

            if (!File.Exists(indexFile))
            {
                ConsoleHelper.Error($"Error: {indexFileName} not found. Run 'ai-bridge init' and create your index first.");
                return;
            }

            var xml = new XmlDocument();
            try
            {
                xml.Load(indexFile);
            }
            catch (Exception ex)
            {
                ConsoleHelper.Error($"Error parsing ai-bridge-index.xml: {ex.Message}");
                return;
            }

            var indexRoot = xml.DocumentElement;
            if (indexRoot == null)
            {
                ConsoleHelper.Error("Error: ai-bridge-index.xml is malformed.");
                return;
            }

            var lastUpdated = indexRoot.GetAttribute("lastUpdated");
            if (string.IsNullOrEmpty(lastUpdated)) lastUpdated = "unknown";

            ConsoleHelper.Info($"📋 ai-bridge-index.xml  (Last updated: {lastUpdated})");

            int moduleCount = 0;
            int totalFileCount = 0;

            var modules = indexRoot.SelectNodes("module");
            if (modules != null)
            {
                foreach (XmlElement module in modules)
                {
                    moduleCount++;
                    var moduleName = module.GetAttribute("name");
                    var files = module.SelectNodes("file");
                    int fileCount = files?.Count ?? 0;
                    totalFileCount += fileCount;

                    ConsoleHelper.Info($"\nModule: {moduleName} ({fileCount} files)");

                    if (files != null)
                    {
                        foreach (XmlElement file in files)
                        {
                            var path = file.GetAttribute("path");
                            var purpose = file.GetAttribute("purpose");
                            ConsoleHelper.Default($"  • {path}  — {purpose}");
                        }
                    }
                }
            }

            ConsoleHelper.Info($"\nTotal: {moduleCount} module(s), {totalFileCount} file(s)");
        }

        public static (List<string> modified, List<string> newFiles, List<string> deleted, DateTime lastUpdated) GetChangedFiles()
        {
            var projectRoot = WorkspaceHelper.GetProjectRoot();
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
            var indexFileName = WorkspaceHelper.GetIndexFileName(projectRoot);
            var indexFile = Path.Combine(aiWorkspace, indexFileName);

            var modifiedFiles = new List<string>();
            var newFiles = new List<string>();
            var deletedFiles = new List<string>();

            if (!File.Exists(indexFile))
            {
                throw new Exception($"Error: {indexFileName} not found. Run 'ai-bridge init' and create your index first.");
            }

            var xml = new XmlDocument();
            try
            {
                xml.Load(indexFile);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error parsing {indexFileName}: {ex.Message}");
            }

            var indexRoot = xml.DocumentElement;
            if (indexRoot == null)
            {
                throw new Exception($"Error: {indexFileName} is malformed.");
            }

            var lastUpdatedStr = indexRoot.GetAttribute("lastUpdated");
            if (string.IsNullOrEmpty(lastUpdatedStr) || !DateTime.TryParse(lastUpdatedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime lastUpdated))
            {
                throw new Exception($"Warning: No 'lastUpdated' attribute found on {indexFileName}. Cannot determine status.");
            }
            lastUpdated = lastUpdated.ToUniversalTime();

            var indexedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileNodes = indexRoot.SelectNodes("//file[@path]");
            if (fileNodes != null)
            {
                foreach (XmlElement fileNode in fileNodes)
                {
                    var path = fileNode.GetAttribute("path");
                    if (!string.IsNullOrEmpty(path))
                    {
                        indexedPaths.Add(path);
                    }
                }
            }

            foreach (var relativePath in indexedPaths)
            {
                var absolutePath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absolutePath))
                {
                    var lastWrite = File.GetLastWriteTimeUtc(absolutePath);
                    if (lastWrite > lastUpdated)
                    {
                        modifiedFiles.Add(relativePath);
                    }
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
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();

                        if (process.ExitCode == 0)
                        {
                            var gitFiles = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var gitFile in gitFiles)
                            {
                                var relativePath = gitFile.Replace('\\', '/');

                                if (relativePath.StartsWith("ai-bridge-", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var fileName = Path.GetFileName(relativePath);
                                var ext = Path.GetExtension(relativePath);
                                if (FileFilterHelper.BinaryExtensions.Contains(ext))
                                    continue;

                                if (FileFilterHelper.ExcludeFileNames.Contains(fileName))
                                    continue;

                                if (FileFilterHelper.IsAiIgnored(relativePath, fileName, aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns))
                                    continue;

                                if (!indexedPaths.Contains(relativePath))
                                {
                                    newFiles.Add(relativePath);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                ConsoleHelper.Warning("Warning: Could not run git. Skipping new file detection.");
            }

            return (modifiedFiles, newFiles, deletedFiles, lastUpdated);
        }

        public static void Status()
        {
            var projectRoot = WorkspaceHelper.GetProjectRoot();
            var indexFileName = WorkspaceHelper.GetIndexFileName(projectRoot);

            List<string> modifiedFiles;
            List<string> newFiles;
            List<string> deletedFiles;
            DateTime lastUpdated;

            try
            {
                (modifiedFiles, newFiles, deletedFiles, lastUpdated) = GetChangedFiles();
            }
            catch (Exception ex)
            {
                ConsoleHelper.Error(ex.Message);
                return;
            }

            // Display results
            var formatted = lastUpdated.ToString("yyyy-MM-dd HH:mm:ss UTC");
            ConsoleHelper.Info($"📋 {indexFileName}  (Last updated: {formatted})");

            if (modifiedFiles.Count == 0 && newFiles.Count == 0 && deletedFiles.Count == 0)
            {
                ConsoleHelper.Success("✅ Index is up to date. No changes detected.");
                return;
            }

            if (modifiedFiles.Count > 0)
            {
                ConsoleHelper.Warning($"⚠ {modifiedFiles.Count} file(s) modified since last index update:");
                foreach (var path in modifiedFiles)
                {
                    var absolutePath = Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar));
                    var modified = File.GetLastWriteTimeUtc(absolutePath);
                    ConsoleHelper.Default($"  • {path}  (modified {modified:yyyy-MM-dd HH:mm:ss UTC})");
                }
            }

            if (newFiles.Count > 0)
            {
                ConsoleHelper.Warning($"➕ {newFiles.Count} new file(s) not in index:");
                foreach (var path in newFiles)
                {
                    ConsoleHelper.Default($"  • {path}");
                }
            }

            if (deletedFiles.Count > 0)
            {
                ConsoleHelper.Warning($"🗑️ {deletedFiles.Count} file(s) in index no longer exist on disk:");
                foreach (var path in deletedFiles)
                {
                    ConsoleHelper.Default($"  • {path}  (deleted)");
                }
            }

            int totalChanges = modifiedFiles.Count + newFiles.Count + deletedFiles.Count;
            ConsoleHelper.Info($"\nSummary: {modifiedFiles.Count} modified, {newFiles.Count} new, {deletedFiles.Count} deleted ({totalChanges} total change(s))");
        }
    }
}
