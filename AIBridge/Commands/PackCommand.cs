using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AIBridge.Core;
using AIBridge.Helpers;
using AIBridge.Constants;

namespace AIBridge.Commands
{
    public static class PackCommand
    {


        // AI Bridge folders that should never be packed (regardless of git or fallback)
        private static readonly string[] AlwaysExcludePrefixes = new[]
        {
            $"{FolderNames.AiBridge}-"
        };

        // Hardcoded folder patterns for fallback when git is not available
        private static readonly List<string> FallbackExcludeFolders = new()
        {
            @"[\\/]\.git[\\/]", @"[\\/]\.vs[\\/]", @"[\\/]\.idea[\\/]", @"[\\/]\.vscode[\\/]",
            @"[\\/]bin[\\/]", @"[\\/]obj[\\/]", @"[\\/]node_modules[\\/]",
            @"[\\/]dist[\\/]", @"[\\/]out[\\/]", @"[\\/]build[\\/]",
            @"[\\/]packages[\\/]", @"[\\/]TestResults[\\/]",
            @"[\\/]ai-bridge-[^\/]+[\\/]",
            @"[\\/]__pycache__[\\/]", @"[\\/]\.mypy_cache[\\/]",
            @"[\\/]target[\\/]", @"[\\/]vendor[\\/]"
        };

        // Hardcoded file patterns for fallback when git is not available
        private static readonly List<string> FallbackExcludeFilePatterns = new()
        {
            @"\.g\.cs$", @"\.g\.i\.cs$", @"\.designer\.cs$", @"AssemblyInfo\.cs$",
            @"\.user$", @"\.suo$", @"\.log$", @"\.tmp$"
        };

        public static (List<ProjectInfo> projects, string ecosystem) DetectProjects(string projectPath)
        {
            // 1. Try .NET (.csproj)
            var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0)
            {
                var projects = csprojFiles
                    .Select(p => new ProjectInfo(
                        Path.GetFileNameWithoutExtension(p),
                        Path.GetDirectoryName(p)! + Path.DirectorySeparatorChar))
                    .OrderByDescending(p => p.DirectoryPrefix.Length)
                    .ToList();
                ConsoleHelper.Info("Detected ecosystem: .NET (found .csproj files)");
                return (projects, "dotnet");
            }

            // 2. Try Node.js (package.json in subfolders)
            var packageJsonFiles = Directory.GetFiles(projectPath, "package.json", SearchOption.AllDirectories)
                .Where(p => Path.GetDirectoryName(p) != projectPath) // exclude root package.json
                .ToList();
            if (packageJsonFiles.Count > 0)
            {
                var projects = packageJsonFiles
                    .Select(p => new ProjectInfo(
                        new DirectoryInfo(Path.GetDirectoryName(p)!).Name,
                        Path.GetDirectoryName(p)! + Path.DirectorySeparatorChar))
                    .OrderByDescending(p => p.DirectoryPrefix.Length)
                    .ToList();
                ConsoleHelper.Info("Detected ecosystem: Node.js (found package.json in subfolders)");
                return (projects, "node");
            }

            // 3. Try Python (pyproject.toml)
            var pyprojectFiles = Directory.GetFiles(projectPath, "pyproject.toml", SearchOption.AllDirectories);
            if (pyprojectFiles.Length > 0)
            {
                var projects = pyprojectFiles
                    .Select(p => new ProjectInfo(
                        new DirectoryInfo(Path.GetDirectoryName(p)!).Name,
                        Path.GetDirectoryName(p)! + Path.DirectorySeparatorChar))
                    .OrderByDescending(p => p.DirectoryPrefix.Length)
                    .ToList();
                ConsoleHelper.Info("Detected ecosystem: Python (found pyproject.toml)");
                return (projects, "python");
            }

            // 4. Try Go (go.mod)
            var goModFiles = Directory.GetFiles(projectPath, "go.mod", SearchOption.AllDirectories);
            if (goModFiles.Length > 0)
            {
                var projects = goModFiles
                    .Select(p => new ProjectInfo(
                        new DirectoryInfo(Path.GetDirectoryName(p)!).Name,
                        Path.GetDirectoryName(p)! + Path.DirectorySeparatorChar))
                    .OrderByDescending(p => p.DirectoryPrefix.Length)
                    .ToList();
                ConsoleHelper.Info("Detected ecosystem: Go (found go.mod)");
                return (projects, "go");
            }

            // 5. Try Rust (Cargo.toml)
            var cargoFiles = Directory.GetFiles(projectPath, "Cargo.toml", SearchOption.AllDirectories);
            if (cargoFiles.Length > 0)
            {
                var projects = cargoFiles
                    .Select(p => new ProjectInfo(
                        new DirectoryInfo(Path.GetDirectoryName(p)!).Name,
                        Path.GetDirectoryName(p)! + Path.DirectorySeparatorChar))
                    .OrderByDescending(p => p.DirectoryPrefix.Length)
                    .ToList();
                ConsoleHelper.Info("Detected ecosystem: Rust (found Cargo.toml)");
                return (projects, "rust");
            }

            // 6. Fallback: group by top-level folders
            var topLevelDirs = Directory.GetDirectories(projectPath)
                .Where(d =>
                {
                    var name = new DirectoryInfo(d).Name;
                    return !name.StartsWith(".") && !name.StartsWith("ai-bridge-")
                        && name != "bin" && name != "obj" && name != "node_modules";
                })
                .Select(d => new ProjectInfo(
                    new DirectoryInfo(d).Name,
                    d + Path.DirectorySeparatorChar))
                .OrderByDescending(p => p.DirectoryPrefix.Length)
                .ToList();

            ConsoleHelper.Info("No specific ecosystem detected — grouping by top-level folders.");
            return (topLevelDirs, "generic");
        }

        /// <summary>
        /// Uses git to get the list of all tracked and untracked-but-not-ignored files.
        /// Returns null if git is not available or the directory is not a git repository.
        /// </summary>
        private static List<string>? GetGitTrackedFiles(string projectPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "ls-files --cached --others --exclude-standard",
                    WorkingDirectory = projectPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0) return null;

                return output
                    .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(f => Path.GetFullPath(Path.Combine(projectPath, f)))
                    .ToList();
            }
            catch
            {
                return null;
            }
        }

        public static void Run(bool incremental = false)
        {
            var projectPath = WorkspaceHelper.GetProjectRoot();
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
            var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
            var aiIgnorePath = Path.Combine(projectPath, FileNames.AiIgnore);

            if (!Directory.Exists(artifactsDir) || !Directory.Exists(Path.Combine(aiWorkspace, "aiSkills")))
            {
                ConsoleHelper.Error("Error: Project not initialized for AI Bridge.");
                ConsoleHelper.Info("Please run 'ai-bridge init' first to set up the necessary skills and ignore files for this codebase.");
                return;
            }

            var rootFolderName = new DirectoryInfo(projectPath).Name;
            var (detectedProjects, ecosystem) = DetectProjects(projectPath);
            var projects = detectedProjects;

            HashSet<string>? incrementalFiles = null;
            if (incremental)
            {
                try
                {
                    var (modified, newFiles, _, _) = IndexCommand.GetChangedFiles();
                    incrementalFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in modified) incrementalFiles.Add(f);
                    foreach (var f in newFiles) incrementalFiles.Add(f);

                    if (incrementalFiles.Count == 0)
                    {
                        ConsoleHelper.Success("✅ No files changed since last index update. Nothing to pack.");
                        return;
                    }
                    ConsoleHelper.Info($"Found {incrementalFiles.Count} modified/new file(s) to pack incrementally.");
                }
                catch (Exception ex)
                {
                    ConsoleHelper.Error(ex.Message);
                    return;
                }
            }

            // --- Step 1: Get file list (git-aware or fallback) ---
            var gitFiles = GetGitTrackedFiles(projectPath);
            string[] allFiles;

            if (gitFiles != null)
            {
                ConsoleHelper.Info("Using git to determine file list (respects .gitignore)...");
                allFiles = gitFiles.ToArray();
            }
            else
            {
                ConsoleHelper.Warning("⚠ Git not available — using built-in exclusion rules...");
                var rawFiles = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories);

                allFiles = rawFiles
                    .Where(f =>
                    {
                        var paddedPath = "/" + Path.GetRelativePath(projectPath, f).Replace("\\", "/") + "/";
                        return !FallbackExcludeFolders.Any(pattern =>
                            Regex.IsMatch(paddedPath, pattern, RegexOptions.IgnoreCase));
                    })
                    .Where(f =>
                    {
                        var fileName = Path.GetFileName(f);
                        return !FallbackExcludeFilePatterns.Any(pattern =>
                            Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase));
                    })
                    .ToArray();
            }

            var (aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns) = FileFilterHelper.LoadAiIgnoreRules(aiIgnorePath);

            // --- Step 3: Filter and pack files ---
            var outputData = new Dictionary<string, StringBuilder>();
            var outputFileCounts = new Dictionary<string, int>();
            int warningCount = 0;

            foreach (var file in allFiles.OrderBy(f => f))
            {
                var relativePath = Path.GetRelativePath(projectPath, file).Replace("\\", "/");
                var fileName = Path.GetFileName(file);
                var extension = Path.GetExtension(file);

                // Always exclude AI Bridge's own folders
                if (AlwaysExcludePrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Skip binary/non-text files
                if (FileFilterHelper.BinaryExtensions.Contains(extension)) continue;

                // Skip excluded file names
                if (FileFilterHelper.ExcludeFileNames.Contains(fileName)) continue;

                // Apply .aiignore rules
                if (FileFilterHelper.IsAiIgnored(relativePath, fileName, aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns)) continue;

                // Skip if incremental and file hasn't changed
                if (incremental && incrementalFiles != null && !incrementalFiles.Contains(relativePath))
                    continue;

                // Determine project grouping
                string projectName = rootFolderName;
                foreach (var proj in projects)
                {
                    if (file.StartsWith(proj.DirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        projectName = proj.Name;
                        break;
                    }
                }

                try
                {
                    var content = File.ReadAllText(file).TrimEnd();
                    var lineCount = File.ReadLines(file).Count();
                    var block = $"<file path=\"{relativePath}\" lines=\"{lineCount}\">\n{content}\n</file>\n";

                    if (!outputData.ContainsKey(projectName))
                    {
                        outputData[projectName] = new StringBuilder();
                        outputFileCounts[projectName] = 0;
                    }

                    outputData[projectName].Append(block);
                    outputFileCounts[projectName]++;

                    ConsoleHelper.WriteColored($"  Packed: {relativePath}", ConsoleColor.Blue);
                }
                catch (Exception ex)
                {
                    ConsoleHelper.Warning($"⚠ Skipped: {relativePath} ({ex.Message})");
                    warningCount++;
                }
            }

            // --- Step 4: Write output files ---
            if (incremental)
            {
                var sb = new StringBuilder();
                int totalFiles = 0;
                foreach (var key in outputData.Keys)
                {
                    sb.AppendLine($"<{XmlTags.Module} name=\"{key}\" files=\"{outputFileCounts[key]}\">");
                    sb.AppendLine(outputData[key].ToString());
                    sb.AppendLine($"</{XmlTags.Module}>");
                    totalFiles += outputFileCounts[key];
                }
                var outPath = Path.Combine(artifactsDir, FileNames.IncrementalContext);
                File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);

                var fileSizeKB = Math.Round(new FileInfo(outPath).Length / 1024.0, 1);
                var approxTokens = sb.Length / 4;
                ConsoleHelper.Success($"SUCCESS: Incremental context packed ({totalFiles} files, {fileSizeKB} KB, ~{approxTokens:N0} tokens) into ai-incremental-context.txt");
            }
            else
            {
                foreach (var key in outputData.Keys)
                {
                    var outName = key == rootFolderName ? $"{key}-root-context.txt" : $"{key}-context.txt";
                    var outPath = Path.Combine(artifactsDir, outName);
                    var finalContent = $"<{XmlTags.Module} name=\"{key}\" files=\"{outputFileCounts[key]}\">\n{outputData[key]}\n</{XmlTags.Module}>\n";

                    File.WriteAllText(outPath, finalContent, Encoding.UTF8);

                    var fileSizeKB = Math.Round(new FileInfo(outPath).Length / 1024.0, 1);
                    var approxTokens = finalContent.Length / 4;
                    ConsoleHelper.Success($"SUCCESS: {key} codebase packed ({outputFileCounts[key]} files, {fileSizeKB} KB, ~{approxTokens:N0} tokens) into {outName}");
                }
            }

            if (warningCount > 0)
            {
                ConsoleHelper.Warning($"\nCompleted with {warningCount} warning(s) (see above).");
            }
            
            StateManager.UpdateEcosystem(ecosystem);
            StateManager.UpdateLastPacked();
        }
    }

    public record ProjectInfo(string Name, string DirectoryPrefix);
}
