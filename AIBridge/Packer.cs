using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AIBridge
{
    public static class Packer
    {
        // Binary/non-text extensions to always exclude from packing
        private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp", ".tiff", ".tif", ".raw",
            // Fonts
            ".woff", ".woff2", ".ttf", ".eot", ".otf",
            // Compiled/binary
            ".exe", ".dll", ".pdb", ".so", ".dylib", ".o", ".a", ".lib",
            ".class", ".jar", ".war", ".pyc", ".pyo", ".wasm",
            // Archives
            ".zip", ".tar", ".gz", ".rar", ".7z", ".bz2", ".xz", ".nupkg",
            // Media
            ".mp3", ".mp4", ".avi", ".mov", ".wav", ".flac", ".ogg", ".webm", ".mkv",
            // Documents (binary formats)
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            // Database
            ".db", ".sqlite", ".sqlite3", ".mdb",
            // Certificates & keys
            ".snk", ".pfx", ".p12", ".cer", ".pem",
            // Other binary
            ".bin", ".dat", ".cache", ".coverage"
        };

        // Specific filenames to always exclude (large or not useful for AI context)
        private static readonly HashSet<string> ExcludeFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
            ".DS_Store", "Thumbs.db", ".gitignore", ".aiignore"
        };

        // AI Bridge folders that should never be packed (regardless of git or fallback)
        private static readonly string[] AlwaysExcludePrefixes = new[]
        {
            "aiSkills/", "aiArtifacts/"
        };

        // Hardcoded folder patterns for fallback when git is not available
        private static readonly List<string> FallbackExcludeFolders = new()
        {
            @"[\\/]\.git[\\/]", @"[\\/]\.vs[\\/]", @"[\\/]\.idea[\\/]", @"[\\/]\.vscode[\\/]",
            @"[\\/]bin[\\/]", @"[\\/]obj[\\/]", @"[\\/]node_modules[\\/]",
            @"[\\/]dist[\\/]", @"[\\/]out[\\/]", @"[\\/]build[\\/]",
            @"[\\/]packages[\\/]", @"[\\/]TestResults[\\/]",
            @"[\\/]aiSkills[\\/]", @"[\\/]aiArtifacts[\\/]",
            @"[\\/]__pycache__[\\/]", @"[\\/]\.mypy_cache[\\/]",
            @"[\\/]target[\\/]", @"[\\/]vendor[\\/]"
        };

        // Hardcoded file patterns for fallback when git is not available
        private static readonly List<string> FallbackExcludeFilePatterns = new()
        {
            @"\.g\.cs$", @"\.g\.i\.cs$", @"\.designer\.cs$", @"AssemblyInfo\.cs$",
            @"\.user$", @"\.suo$", @"\.log$", @"\.tmp$"
        };

        public static void Init()
        {
            var projectPath = Environment.CurrentDirectory;
            var artifactsDir = Path.Combine(projectPath, "aiArtifacts");
            if (!Directory.Exists(artifactsDir))
            {
                Directory.CreateDirectory(artifactsDir);
            }

            var responseFilePath = Path.Combine(artifactsDir, "ai-response.xml");
            if (!File.Exists(responseFilePath))
            {
                File.WriteAllText(responseFilePath, "<!-- Paste the AI response XML here -->\n");
            }

            var gitignorePath = Path.Combine(projectPath, ".gitignore");
            if (File.Exists(gitignorePath))
            {
                var content = File.ReadAllText(gitignorePath);
                if (!content.Contains("aiArtifacts/"))
                {
                    File.AppendAllText(gitignorePath, "\n# AI Bridge Artifacts\naiArtifacts/\n");
                    ConsoleHelper.Success("✅ Patched .gitignore to ignore aiArtifacts/");
                }
            }

            var aiIgnorePath = Path.Combine(projectPath, ".aiignore");
            if (!File.Exists(aiIgnorePath))
            {
                var defaultIgnore = "# Additional ignore rules for AI Bridge packing (works alongside .gitignore)\n# Folders should end with /\naiSkills/\nTestResults/\n*.g.cs\n*.log\n*.tmp\n";
                File.WriteAllText(aiIgnorePath, defaultIgnore);
                ConsoleHelper.Success("✅ Created default .aiignore file.");
            }
            else
            {
                ConsoleHelper.Info("ℹ .aiignore already exists.");
            }

            // Create aiSkills folder and write system prompt
            var skillsDir = Path.Combine(projectPath, "aiSkills");
            if (!Directory.Exists(skillsDir))
            {
                Directory.CreateDirectory(skillsDir);
            }

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            string[] skillFiles = { "ai-system-prompt.md", "ai-response-skill.md" };

            foreach (var file in skillFiles)
            {
                var filePath = Path.Combine(skillsDir, file);
                using var stream = assembly.GetManifestResourceStream($"AIBridge.Resources.{file}");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();
                    
                    bool existed = File.Exists(filePath);
                    File.WriteAllText(filePath, content, Encoding.UTF8);
                    
                    if (existed)
                    {
                        ConsoleHelper.Success($"✅ Updated aiSkills/{file} to latest version.");
                    }
                    else
                    {
                        ConsoleHelper.Success($"✅ Created aiSkills/{file}.");
                    }
                }
                else
                {
                    ConsoleHelper.Warning($"⚠ Could not extract embedded resource: {file}");
                }
            }
        }

        private static (List<ProjectInfo> projects, string ecosystem) DetectProjects(string projectPath)
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
                    return !name.StartsWith(".") && name != "aiArtifacts" && name != "aiSkills"
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

        public static void Run()
        {
            Init();
            var projectPath = Environment.CurrentDirectory;
            var artifactsDir = Path.Combine(projectPath, "aiArtifacts");
            var aiIgnorePath = Path.Combine(projectPath, ".aiignore");

            var rootFolderName = new DirectoryInfo(projectPath).Name;
            var (detectedProjects, ecosystem) = DetectProjects(projectPath);
            var projects = detectedProjects;

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

            // --- Step 2: Build .aiignore rules ---
            var aiIgnoreExcludeFolders = new List<string>();
            var aiIgnoreExcludeFilePatterns = new List<string>();

            if (File.Exists(aiIgnorePath))
            {
                ConsoleHelper.Info("Loading additional ignore rules from .aiignore...");
                foreach (var line in File.ReadAllLines(aiIgnorePath)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")))
                {
                    var rule = line.Replace("\\", "/");
                    bool isFolder = rule.EndsWith("/");
                    if (isFolder) rule = rule.TrimEnd('/');

                    var regexRule = Regex.Escape(rule).Replace(@"\*", ".*").Replace(@"\?", ".");
                    if (isFolder) aiIgnoreExcludeFolders.Add($@"[\\/]{regexRule}[\\/]");
                    else aiIgnoreExcludeFilePatterns.Add($@"^{regexRule}$");
                }
            }

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
                if (BinaryExtensions.Contains(extension)) continue;

                // Skip excluded file names
                if (ExcludeFileNames.Contains(fileName)) continue;

                // Apply .aiignore rules
                if (aiIgnoreExcludeFolders.Count > 0 || aiIgnoreExcludeFilePatterns.Count > 0)
                {
                    var paddedPath = "/" + relativePath + "/";
                    if (aiIgnoreExcludeFolders.Any(f => Regex.IsMatch(paddedPath, f, RegexOptions.IgnoreCase))) continue;
                    if (aiIgnoreExcludeFilePatterns.Any(p => Regex.IsMatch(fileName, p, RegexOptions.IgnoreCase))) continue;
                }

                // Determine project grouping
                string projectName = "Solution";
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
            foreach (var key in outputData.Keys)
            {
                var outName = key == "Solution" ? $"{rootFolderName}-Solution-context.txt" : $"{key}-context.txt";
                var outPath = Path.Combine(artifactsDir, outName);
                var finalContent = $"<project name=\"{key}\" files=\"{outputFileCounts[key]}\">\n{outputData[key]}\n</project>\n";

                File.WriteAllText(outPath, finalContent, Encoding.UTF8);

                var fileSizeKB = Math.Round(new FileInfo(outPath).Length / 1024.0, 1);
                var approxTokens = finalContent.Length / 4;
                ConsoleHelper.Success($"SUCCESS: {key} codebase packed ({outputFileCounts[key]} files, {fileSizeKB} KB, ~{approxTokens:N0} tokens) into {outName}");
            }

            if (warningCount > 0)
            {
                ConsoleHelper.Warning($"\nCompleted with {warningCount} warning(s) (see above).");
            }
        }
    }

    public record ProjectInfo(string Name, string DirectoryPrefix);
}
