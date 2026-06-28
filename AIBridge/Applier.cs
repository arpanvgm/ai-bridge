using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AIBridge.Core;
using AIBridge.Helpers;

namespace AIBridge
{
    public static class Applier
    {
        public static void Run(bool watch = false, bool paste = false)
        {
            if (watch)
            {
                if (paste)
                {
                    ConsoleHelper.Warning("Ignoring --watch flag because --paste was used.");
                    ApplyInternal(paste);
                    return;
                }

                ConsoleHelper.Info("Starting watch mode for ai-response.xml...");
                ApplyInternal(paste);

                var projectRoot = WorkspaceHelper.GetProjectRoot();
                var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
                var watchDir = Path.Combine(aiWorkspace, "aiArtifacts");
                if (!Directory.Exists(watchDir)) Directory.CreateDirectory(watchDir);

                using var watcher = new FileSystemWatcher(watchDir)
                {
                    Filter = "ai-response.xml",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                DateTime lastRun = DateTime.MinValue;

                void OnChanged(object s, FileSystemEventArgs e)
                {
                    if ((DateTime.Now - lastRun).TotalMilliseconds < 1000) return;
                    lastRun = DateTime.Now;

                    System.Threading.Thread.Sleep(500); // debounce file lock
                    Console.WriteLine();
                    ConsoleHelper.Info("Change detected in ai-response.xml. Applying...");
                    ApplyInternal(paste);
                    ConsoleHelper.Info("\nWaiting for next change... (Press Ctrl+C to exit)");
                }

                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;

                ConsoleHelper.Info("\nWaiting for next change... (Press Ctrl+C to exit)");

                var resetEvent = new System.Threading.ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    resetEvent.Set();
                };
                resetEvent.WaitOne();
            }
            else
            {
                ApplyInternal(paste);
            }
        }

        private static void ApplyInternal(bool paste)
        {
            var projectPath = WorkspaceHelper.GetProjectRoot();
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
            var artifactsDir = Path.Combine(aiWorkspace, "aiArtifacts");
            var inputFile = Path.Combine(artifactsDir, "ai-response.xml");
            var failedLogFile = Path.Combine(artifactsDir, "failed-patches.txt");

            // --- Step 1: Resolve input content into ai-response.xml ---
            if (!InputResolver.Resolve(inputFile, paste))
                return;

            // --- Step 2: Read and parse the file ---
            var rawContent = File.ReadAllText(inputFile);

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
            if (root.Name != "ai-response" && root.Name != "ai-request" && root.Name != "create-ai-bridge-index" && root.Name != "update-ai-bridge-index")
            {
                ConsoleHelper.Error($"Error: Root element must be <ai-response>, <ai-request>, <create-ai-bridge-index>, or <update-ai-bridge-index>, found <{root.Name}>.");
                return;
            }

            // --- Step 3: Delegate to ai-request handler if needed ---
            if (root.Name == "ai-request")
            {
                AiRequestHandler.Handle(root, projectPath, paste);
                return;
            }

            if (root.Name == "create-ai-bridge-index")
            {
                AiIndexHandler.HandleCreate(root, projectPath);
                InputResolver.ResetInputFile(inputFile);
                return;
            }

            if (root.Name == "update-ai-bridge-index")
            {
                AiIndexHandler.HandleUpdate(root, projectPath);
                InputResolver.ResetInputFile(inputFile);
                return;
            }

            // --- Step 4.5: Validate index update rules ---
            var aiEditsNode = root.SelectSingleNode("ai-edits");
            var indexUpdateNode = root.SelectSingleNode("update-ai-bridge-index");

            if (aiEditsNode != null)
            {
                if (indexUpdateNode == null)
                {
                    ConsoleHelper.Error("Error: AI provided <ai-edits> but completely forgot to provide an <update-ai-bridge-index> block.");
                    ConsoleHelper.Info("Please ask the AI to regenerate the response and include the mandatory index update block.");
                    return;
                }

                var hasDeletes = aiEditsNode.SelectNodes("delete")?.Count > 0;
                bool actualCreates = false;
                
                var fileNodes = aiEditsNode.SelectNodes("file");
                if (fileNodes != null)
                {
                    foreach (XmlNode fileNode in fileNodes)
                    {
                        var relPath = fileNode.Attributes?["path"]?.Value?.Trim();
                        if (!string.IsNullOrEmpty(relPath))
                        {
                            var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                            if (!File.Exists(absPath))
                            {
                                actualCreates = true;
                                break;
                            }
                        }
                    }
                }

                if (actualCreates || hasDeletes)
                {
                    var hasIndexChanges = indexUpdateNode.SelectNodes(".//file | .//delete")?.Count > 0;
                    if (!hasIndexChanges)
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
                    if (node.Name != "ai-edits" && node.Name != "update-ai-bridge-index")
                    {
                        ConsoleHelper.Error($"Error: Unknown element '<{node.Name}>' found. Only <ai-edits> and <update-ai-bridge-index> are allowed.");
                        return;
                    }
                }
            }

            int countFullFiles = 0, countPatchOk = 0, countPatchFailed = 0, countDeleted = 0;
            var failedFiles = new List<string>();
            var failedPatchNodes = new List<XmlNode>();

            // --- Step 5: Process <file> elements (full file creation/overwrite) ---
            foreach (XmlNode node in root.SelectNodes("ai-edits/file")!)
            {
                var relPath = node.Attributes?["path"]?.Value?.Trim();
                if (string.IsNullOrEmpty(relPath))
                {
                    ConsoleHelper.Error("File creation failed: missing 'path' attribute on <file> tag.");
                    continue;
                }

                var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
                var newContent = node.InnerText.Trim('\r', '\n') + "\r\n";
                File.WriteAllText(absPath, newContent, Encoding.UTF8);
                ConsoleHelper.Success($"Created/Overwritten: {relPath}");
                countFullFiles++;
            }

            // --- Step 6: Process <patch> elements ---
            foreach (XmlNode node in root.SelectNodes("ai-edits/patch")!)
            {
                if (Patcher.ApplyPatch(node, projectPath, failedFiles, failedPatchNodes))
                    countPatchOk++;
                else
                    countPatchFailed++;
            }

            // --- Step 7: Process <delete> elements ---
            foreach (XmlNode node in root.SelectNodes("ai-edits/delete")!)
            {
                var relPath = node.Attributes?["path"]?.Value?.Trim();
                if (string.IsNullOrEmpty(relPath))
                {
                    ConsoleHelper.Error("Delete failed: missing 'path' attribute on <delete> tag.");
                    continue;
                }

                var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(absPath))
                {
                    File.Delete(absPath);
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
            if (countDeleted > 0)
            {
                CleanEmptyFolders(projectPath);
            }

            // --- Step 9: Summary ---
            ConsoleHelper.Info($"\nSummary: {countFullFiles} written, {countPatchOk} patched, {countDeleted} deleted.");

            if (countPatchFailed > 0)
            {
                // Write failed file paths for quick reference
                ConsoleHelper.Error($"Failed patches: {countPatchFailed}. Check {failedLogFile}");
                File.WriteAllLines(failedLogFile, failedFiles.Distinct());

                // Rebuild ai-response.xml with ONLY failed patch blocks
                Patcher.RebuildResponseWithFailedPatches(inputFile, failedPatchNodes);
                ConsoleHelper.Warning($"⚠ ai-response.xml now contains only the {countPatchFailed} failed patch(es). Fix and re-run 'ai-bridge apply'.");
            }
            else
            {
                InputResolver.ResetInputFile(inputFile);
            }
        }

        private static void CleanEmptyFolders(string rootPath)
        {
            foreach (var dir in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    ConsoleHelper.Info($"Removed empty folder: {Path.GetRelativePath(rootPath, dir)}");
                }
            }
        }
    }
}
