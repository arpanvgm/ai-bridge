using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using AIBridge.Helpers;

namespace AIBridge
{
    public static class Indexer
    {


        public static void Display()
        {
            var projectRoot = WorkspaceHelper.GetProjectRoot();
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
            var indexFile = Path.Combine(aiWorkspace, "ai-bridge-index.xml");

            if (!File.Exists(indexFile))
            {
                ConsoleHelper.Error("Error: ai-bridge-index.xml not found. Run 'ai-bridge init' and create your index first.");
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

        public static void Status()
        {
            var projectRoot = WorkspaceHelper.GetProjectRoot();
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
            var indexFile = Path.Combine(aiWorkspace, "ai-bridge-index.xml");

            if (!File.Exists(indexFile))
            {
                ConsoleHelper.Error("Error: ai-bridge-index.xml not found. Run 'ai-bridge init' and create your index first.");
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

            var lastUpdatedStr = indexRoot.GetAttribute("lastUpdated");
            if (string.IsNullOrEmpty(lastUpdatedStr))
            {
                ConsoleHelper.Warning("Warning: No 'lastUpdated' attribute found on ai-bridge-index.xml. Cannot determine status.");
                return;
            }

            DateTime lastUpdated;
            if (!DateTime.TryParse(lastUpdatedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out lastUpdated))
            {
                ConsoleHelper.Warning($"Warning: Could not parse 'lastUpdated' value: {lastUpdatedStr}");
                return;
            }
            lastUpdated = lastUpdated.ToUniversalTime();

            // Collect all indexed file paths
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

            var modifiedFiles = new List<(string path, DateTime modified)>();
            var deletedFiles = new List<string>();
            var newFiles = new List<string>();

            // Check modified and deleted files
            foreach (var relativePath in indexedPaths)
            {
                var absolutePath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absolutePath))
                {
                    var lastWrite = File.GetLastWriteTimeUtc(absolutePath);
                    if (lastWrite > lastUpdated)
                    {
                        modifiedFiles.Add((relativePath, lastWrite));
                    }
                }
                else
                {
                    deletedFiles.Add(relativePath);
                }
            }

            var aiIgnorePath = Path.Combine(projectRoot, ".aiignore");
            var (aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns) = FileFilterHelper.LoadAiIgnoreRules(aiIgnorePath);

            // Check for new files via git
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

                                // Skip ai-bridge- prefixed paths
                                if (relativePath.StartsWith("ai-bridge-", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                // Skip binary extensions
                                var fileName = Path.GetFileName(relativePath);
                                var ext = Path.GetExtension(relativePath);
                                if (FileFilterHelper.BinaryExtensions.Contains(ext))
                                    continue;

                                // Skip excluded filenames
                                if (FileFilterHelper.ExcludeFileNames.Contains(fileName))
                                    continue;

                                // Apply .aiignore rules
                                if (FileFilterHelper.IsAiIgnored(relativePath, fileName, aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns))
                                    continue;

                                // If not in index, it's a new file
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

            // Display results
            var formatted = lastUpdated.ToString("yyyy-MM-dd HH:mm:ss UTC");
            ConsoleHelper.Info($"📋 Index Status  (Last updated: {formatted})");

            if (modifiedFiles.Count == 0 && newFiles.Count == 0 && deletedFiles.Count == 0)
            {
                ConsoleHelper.Success("✅ Index is up to date. No changes detected.");
                return;
            }

            if (modifiedFiles.Count > 0)
            {
                ConsoleHelper.Warning($"⚠ {modifiedFiles.Count} file(s) modified since last index update:");
                foreach (var (path, modified) in modifiedFiles)
                {
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
