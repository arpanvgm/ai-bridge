using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace AIBridge
{
    public static class Applier
    {
        public static void Run(bool dryRun = false, bool watch = false, bool paste = false)
        {
            if (watch)
            {
                if (paste)
                {
                    ConsoleHelper.Warning("Ignoring --watch flag because --paste was used.");
                    ApplyInternal(dryRun, paste);
                    return;
                }

                ConsoleHelper.Info("Starting watch mode for ai-response.xml...");
                ApplyInternal(dryRun, paste);

                var watchDir = Path.Combine(Environment.CurrentDirectory, "aiArtifacts");
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
                    ApplyInternal(dryRun, paste);
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
                ApplyInternal(dryRun, paste);
            }
        }

        private static void ApplyInternal(bool dryRun, bool paste)
        {
            var projectPath = Environment.CurrentDirectory;
            var artifactsDir = Path.Combine(projectPath, "aiArtifacts");
            var inputFile = Path.Combine(artifactsDir, "ai-response.xml");
            var failedLogFile = Path.Combine(artifactsDir, "failed-patches.txt");

            string rawContent;

            if (paste)
            {
                rawContent = TextCopy.ClipboardService.GetText() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rawContent))
                {
                    ConsoleHelper.Error("Error: Clipboard is empty.");
                    return;
                }
                ConsoleHelper.Info("Read AI response from clipboard.");
            }
            else
            {
                if (!File.Exists(inputFile))
                {
                    ConsoleHelper.Error($"Error: Cannot find '{inputFile}'.");
                    return;
                }
                rawContent = File.ReadAllText(inputFile);
            }

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
                ConsoleHelper.Error($"Error: '{inputFile}' is not valid XML. {ex.Message}");
                return;
            }

            var root = xml.DocumentElement;
            if (root == null || root.Name != "ai-response")
            {
                ConsoleHelper.Error("Error: Root element must be <ai-response>.");
                return;
            }

            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.NodeType == XmlNodeType.Element)
                {
                    if (node.Name != "file" && node.Name != "patch" && node.Name != "delete")
                    {
                        ConsoleHelper.Error($"Error: Unknown element '<{node.Name}>' found. Only <file>, <patch>, and <delete> are allowed.");
                        return;
                    }
                }
            }

            // Collect all target file paths from the response
            var targetFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlNode node in xml.SelectNodes("//file")!)
            {
                var p = node.Attributes?["path"]?.Value.Trim();
                if (!string.IsNullOrEmpty(p)) targetFiles.Add(p.Replace('/', Path.DirectorySeparatorChar));
            }
            foreach (XmlNode node in xml.SelectNodes("//patch")!)
            {
                var p = node.Attributes?["path"]?.Value.Trim();
                if (!string.IsNullOrEmpty(p)) targetFiles.Add(p.Replace('/', Path.DirectorySeparatorChar));
            }
            foreach (XmlNode node in xml.SelectNodes("//delete")!)
            {
                var p = node.Attributes?["path"]?.Value.Trim();
                if (!string.IsNullOrEmpty(p)) targetFiles.Add(p.Replace('/', Path.DirectorySeparatorChar));
            }

            if (dryRun)
            {
                ConsoleHelper.Info("\n--- DRY RUN (no files will be modified) ---\n");
            }

            int countFullFiles = 0, countPatchOk = 0, countPatchFailed = 0, countDeleted = 0;
            var failedFiles = new List<string>();
            var failedPatchNodes = new List<XmlNode>();

            // 1. Full Files
            foreach (XmlNode node in xml.SelectNodes("//file")!)
            {
                var relPath = node.Attributes?["path"]?.Value.Trim();
                if (string.IsNullOrEmpty(relPath)) continue;

                var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));

                if (dryRun)
                {
                    var action = File.Exists(absPath) ? "OVERWRITE" : "CREATE";
                    ConsoleHelper.Info($"  {action}: {relPath}");
                    countFullFiles++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
                var newContent = node.InnerText.Trim('\r', '\n') + "\r\n";
                File.WriteAllText(absPath, newContent, Encoding.UTF8);
                ConsoleHelper.Success($"Created/Overwritten: {relPath}");
                countFullFiles++;
            }

            // 2. Patches
            foreach (XmlNode node in xml.SelectNodes("//patch")!)
            {
                var relPath = node.Attributes?["path"]?.Value.Trim();
                if (string.IsNullOrEmpty(relPath)) continue;

                var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                var searchNode = node.SelectSingleNode("search");
                var replaceNode = node.SelectSingleNode("replace");

                if (dryRun)
                {
                    ConsoleHelper.Info($"  PATCH: {relPath}");
                    countPatchOk++;
                    continue;
                }

                if (!File.Exists(absPath) || searchNode == null || replaceNode == null)
                {
                    ConsoleHelper.Error($"Patch failed: File not found or invalid XML -> {relPath}");
                    failedFiles.Add(relPath);
                    failedPatchNodes.Add(node);
                    countPatchFailed++;
                    continue;
                }

                var targetContent = Normalize(File.ReadAllText(absPath));
                var search = TrimCDATA(Normalize(searchNode.InnerText));
                var replace = TrimCDATA(Normalize(replaceNode.InnerText));

                if (targetContent.Contains(search))
                {
                    // Exact match
                    var updated = targetContent.Replace(search, replace);
                    File.WriteAllText(absPath, updated, Encoding.UTF8);
                    ConsoleHelper.Success($"Patched: {relPath}");
                    countPatchOk++;
                }
                else if (TryFuzzyPatch(targetContent, search, replace, out var fuzzyResult))
                {
                    // Fuzzy match (whitespace-normalized)
                    File.WriteAllText(absPath, fuzzyResult, Encoding.UTF8);
                    ConsoleHelper.Warning($"Patched (fuzzy): {relPath}");
                    countPatchOk++;
                }
                else
                {
                    ConsoleHelper.Error($"Patch failed: Match not found -> {relPath}");
                    failedFiles.Add(relPath);
                    failedPatchNodes.Add(node);
                    countPatchFailed++;
                }
            }

            // 3. Deletes
            foreach (XmlNode node in xml.SelectNodes("//delete")!)
            {
                var relPath = node.Attributes?["path"]?.Value.Trim();
                if (string.IsNullOrEmpty(relPath)) continue;

                var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));

                if (dryRun)
                {
                    ConsoleHelper.Info($"  DELETE: {relPath}");
                    countDeleted++;
                    continue;
                }

                if (File.Exists(absPath))
                {
                    File.Delete(absPath);
                    ConsoleHelper.Success($"Deleted: {relPath}");
                    countDeleted++;
                }
            }

            // 4. Clean up empty folders after deletions
            if (countDeleted > 0 && !dryRun)
            {
                CleanEmptyFolders(projectPath);
            }

            // Summary
            if (dryRun)
            {
                ConsoleHelper.Info($"\nDry run complete: {countFullFiles} file(s), {countPatchOk} patch(es), {countDeleted} delete(s).");
                ConsoleHelper.Info("No files were modified. Run 'ai-bridge apply' to apply for real.");
            }
            else
            {
                ConsoleHelper.Info($"\nSummary: {countFullFiles} written, {countPatchOk} patched, {countDeleted} deleted.");

                if (countPatchFailed > 0)
                {
                    // Write failed file paths for quick reference
                    ConsoleHelper.Error($"Failed patches: {countPatchFailed}. Check {failedLogFile}");
                    File.WriteAllLines(failedLogFile, failedFiles.Distinct());

                    // Rebuild ai-response.xml with ONLY failed patch blocks
                    RebuildResponseWithFailedPatches(inputFile, failedPatchNodes);
                    ConsoleHelper.Warning($"⚠ ai-response.xml now contains only the {countPatchFailed} failed patch(es). Fix and re-run 'ai-bridge apply'.");
                }
                else
                {
                    if (!paste)
                    {
                        File.WriteAllText(inputFile, "<!-- Paste the AI response XML here -->\n");
                        ConsoleHelper.Success("✅ Cleared ai-response.xml to prevent accidental re-application.");
                    }
                }
            }
        }

        private static bool TryFuzzyPatch(string fileContent, string search, string replace, out string result)
        {
            result = fileContent;

            // Normalize whitespace: collapse runs of spaces/tabs to single space, trim each line
            var normalizedFile = NormalizeWhitespace(fileContent);
            var normalizedSearch = NormalizeWhitespace(search);

            if (!normalizedFile.Contains(normalizedSearch))
            {
                return false;
            }

            // Find the matching region by line-by-line comparison
            var fileLines = fileContent.Split('\n');
            var searchLines = search.Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToArray();

            if (searchLines.Length == 0) return false;

            var normalizedSearchLines = searchLines.Select(NormalizeLineWhitespace).ToArray();

            // Find the starting line index in the file
            for (int i = 0; i <= fileLines.Length - normalizedSearchLines.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < normalizedSearchLines.Length; j++)
                {
                    if (NormalizeLineWhitespace(fileLines[i + j].TrimEnd()) != normalizedSearchLines[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    // Replace the matched range with the replacement text
                    var before = string.Join('\n', fileLines.Take(i));
                    var after = string.Join('\n', fileLines.Skip(i + normalizedSearchLines.Length));

                    if (before.Length > 0) before += '\n';
                    if (after.Length > 0) replace += '\n';

                    result = before + replace + after;
                    return true;
                }
            }

            return false;
        }

        private static void RebuildResponseWithFailedPatches(string inputFile, List<XmlNode> failedPatchNodes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<ai-response>");
            sb.AppendLine();

            foreach (var node in failedPatchNodes)
            {
                sb.AppendLine(node.OuterXml);
                sb.AppendLine();
            }

            sb.AppendLine("</ai-response>");
            File.WriteAllText(inputFile, sb.ToString(), Encoding.UTF8);
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

        private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");
        private static string TrimCDATA(string text) => Regex.Replace(Regex.Replace(text, @"^\r?\n", ""), @"\r?\n[ \t]*$", "");
        private static string NormalizeWhitespace(string text) => Regex.Replace(text, @"[ \t]+", " ").Trim();
        private static string NormalizeLineWhitespace(string line) => Regex.Replace(line, @"[ \t]+", " ").Trim();
    }
}
