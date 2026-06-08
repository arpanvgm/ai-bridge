using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace AIBridge
{
    public static class Applier
    {
        public static void Run()
        {
            var projectPath = Environment.CurrentDirectory;
            var artifactsDir = Path.Combine(projectPath, "aiArtifacts");
            var inputFile = Path.Combine(artifactsDir, "ai-response.xml");
            var failedLogFile = Path.Combine(artifactsDir, "failed-patches.txt");

            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"Error: Cannot find '{inputFile}'.");
                return;
            }

            if (File.Exists(failedLogFile)) File.Delete(failedLogFile);

            var rawContent = File.ReadAllText(inputFile);
            rawContent = Regex.Replace(rawContent, @"(?m)^```[a-zA-Z]*\s*$", "");
            rawContent = Regex.Replace(rawContent, @"(?m)^```\s*$", "");

            var xml = new XmlDocument();
            try
            {
                xml.LoadXml(rawContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: '{inputFile}' is not valid XML. {ex.Message}");
                return;
            }

            int countFullFiles = 0, countPatchOk = 0, countPatchFailed = 0, countDeleted = 0;
            var failedFiles = new List<string>();

            // 1. Full Files
            foreach (XmlNode node in xml.SelectNodes("//file")!)
            {
                var relPath = node.Attributes?["path"]?.Value.Trim();
                if (string.IsNullOrEmpty(relPath)) continue;

                var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);

                var newContent = node.InnerText.Trim('\r', '\n') + "\r\n";
                File.WriteAllText(absPath, newContent, Encoding.UTF8);
                Console.WriteLine($"Created/Overwritten: {relPath}");
                countFullFiles++;
            }

            // 2. Patches
            foreach (XmlNode node in xml.SelectNodes("//patch")!)
            {
                var relPath = node.Attributes?["file"]?.Value.Trim();
                if (string.IsNullOrEmpty(relPath)) continue;

                var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                var searchNode = node.SelectSingleNode("search");
                var replaceNode = node.SelectSingleNode("replace");

                if (!File.Exists(absPath) || searchNode == null || replaceNode == null)
                {
                    Console.WriteLine($"Patch failed: File not found or invalid XML -> {relPath}");
                    failedFiles.Add(relPath);
                    countPatchFailed++;
                    continue;
                }

                var targetContent = Normalize(File.ReadAllText(absPath));
                var search = TrimCDATA(Normalize(searchNode.InnerText));
                var replace = TrimCDATA(Normalize(replaceNode.InnerText));

                if (targetContent.Contains(search))
                {
                    var updated = targetContent.Replace(search, replace);
                    File.WriteAllText(absPath, updated, Encoding.UTF8);
                    Console.WriteLine($"Patched: {relPath}");
                    countPatchOk++;
                }
                else
                {
                    Console.WriteLine($"Patch failed: Match not found -> {relPath}");
                    failedFiles.Add(relPath);
                    countPatchFailed++;
                }
            }

            // 3. Deletes
            foreach (XmlNode node in xml.SelectNodes("//delete")!)
            {
                var relPath = node.Attributes?["path"]?.Value.Trim();
                if (string.IsNullOrEmpty(relPath)) continue;

                var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absPath))
                {
                    File.Delete(absPath);
                    Console.WriteLine($"Deleted: {relPath}");
                    countDeleted++;
                }
            }

            // 4. Clean up empty folders after deletions
            if (countDeleted > 0)
            {
                CleanEmptyFolders(projectPath);
            }

            // Summary
            Console.WriteLine($"\nSummary: {countFullFiles} written, {countPatchOk} patched, {countDeleted} deleted.");
            if (countPatchFailed > 0)
            {
                Console.WriteLine($"Failed patches: {countPatchFailed}. Check {failedLogFile}");
                File.WriteAllLines(failedLogFile, failedFiles.Distinct());
            }

            File.WriteAllText(inputFile, "<!-- Paste the AI response XML here -->\n");
            Console.WriteLine("✅ Cleared ai-response.xml to prevent accidental re-application.");
        }

        private static void CleanEmptyFolders(string rootPath)
        {
            foreach (var dir in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    Console.WriteLine($"Removed empty folder: {Path.GetRelativePath(rootPath, dir)}");
                }
            }
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");
        private static string TrimCDATA(string text) => Regex.Replace(Regex.Replace(text, @"^\r?\n", ""), @"\r?\n[ \t]*$", "");
    }
}
