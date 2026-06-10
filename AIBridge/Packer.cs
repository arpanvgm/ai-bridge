using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AIBridge
{
    public static class Packer
    {
        private static readonly HashSet<string> DotNetExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".props", ".targets", ".json", ".config", ".xml", ".xaml",
            ".cshtml", ".razor", ".html", ".css", ".scss", ".js", ".ts",
            ".fs", ".vb", ".resx", ".sql", ".ps1", ".cmd", ".sh", ".yml", ".yaml", ".ini", ".env", ".md"
        };

        private static readonly HashSet<string> NodeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs", ".json", ".html", ".css", ".scss", ".sass", ".less",
            ".vue", ".svelte", ".astro", ".yaml", ".yml", ".md", ".env", ".graphql", ".gql"
        };

        private static readonly HashSet<string> PythonExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".py", ".pyi", ".pyx", ".toml", ".cfg", ".ini", ".yaml", ".yml", ".md", ".env",
            ".txt", ".json", ".html", ".css", ".js"
        };

        private static readonly HashSet<string> GoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".go", ".mod", ".sum", ".yaml", ".yml", ".md", ".env", ".json", ".toml"
        };

        private static readonly HashSet<string> RustExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".rs", ".toml", ".yaml", ".yml", ".md", ".env", ".json"
        };

        private static readonly HashSet<string> FallbackExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".props", ".targets", ".json", ".config", ".xml", ".xaml",
            ".cshtml", ".razor", ".html", ".css", ".scss", ".sass", ".less",
            ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs", ".vue", ".svelte",
            ".py", ".pyi", ".go", ".mod", ".rs", ".toml",
            ".fs", ".vb", ".resx", ".sql", ".ps1", ".cmd", ".sh",
            ".yml", ".yaml", ".ini", ".env", ".md", ".txt",
            ".graphql", ".gql", ".astro"
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
                var defaultIgnore = "# Folders should end with /\nbin/\nobj/\n.vs/\n.git/\nnode_modules/\ndist/\nout/\nTestResults/\n*.g.cs\n*.log\n*.tmp\n";
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

            var systemPromptPath = Path.Combine(skillsDir, "ai-system-prompt.md");
            if (!File.Exists(systemPromptPath))
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("AIBridge.Resources.ai-system-prompt.md");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var promptContent = reader.ReadToEnd();
                    File.WriteAllText(systemPromptPath, promptContent, Encoding.UTF8);
                    ConsoleHelper.Success("✅ Created aiSkills/ai-system-prompt.md (system prompt for your AI).");
                }
                else
                {
                    ConsoleHelper.Warning("⚠ Could not extract embedded system prompt resource.");
                }
            }
            else
            {
                ConsoleHelper.Info("ℹ aiSkills/ai-system-prompt.md already exists.");
            }
        }

        private static (List<ProjectInfo> projects, HashSet<string> extensions, string ecosystem) DetectProjects(string projectPath)
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
                return (projects, DotNetExtensions, "dotnet");
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
                return (projects, NodeExtensions, "node");
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
                return (projects, PythonExtensions, "python");
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
                return (projects, GoExtensions, "go");
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
                return (projects, RustExtensions, "rust");
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
            return (topLevelDirs, FallbackExtensions, "generic");
        }

        public static void Run()
        {
            Init();
            var projectPath = Environment.CurrentDirectory;
            var artifactsDir = Path.Combine(projectPath, "aiArtifacts");
            var aiIgnorePath = Path.Combine(projectPath, ".aiignore");

            var rootFolderName = new DirectoryInfo(projectPath).Name;
            var (detectedProjects, includeExtensions, ecosystem) = DetectProjects(projectPath);

            // Convert to the format used by the rest of the method
            var projects = detectedProjects;

            var includeSpecificFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "appsettings.json", "appsettings.Development.json", "nuget.config", "Dockerfile", ".dockerignore",
                "package.json", "tsconfig.json", "vite.config.ts", "vite.config.js", "next.config.js", "next.config.mjs",
                "webpack.config.js", "babel.config.js", ".babelrc", "jest.config.js", "jest.config.ts",
                "pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "Pipfile",
                "go.mod", "go.sum", "Cargo.toml", "Cargo.lock",
                "Makefile", "Rakefile", "Gemfile", "pom.xml", "build.gradle"
            };

            var solutionIncludeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".sln", ".slnx", ".props", ".targets"
            };
            var solutionSpecificFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "global.json", "nuget.config", "Directory.Build.props", "Directory.Build.targets",
                ".editorconfig", "docker-compose.yml", "docker-compose.yaml", "docker-compose.dcproj",
                "package.json", "tsconfig.json", "pyproject.toml", "go.mod", "Cargo.toml",
                "Makefile", "Dockerfile", ".dockerignore", "README.md", "LICENSE"
            };

            var excludeFolders = new List<string>
            {
                @"[\\\/]aiSkills[\\\/]",
                @"[\\\/]aiArtifacts[\\\/]",
                @"[\\\/]bin[\\\/]", @"[\\\/]obj[\\\/]", @"[\\\/]\.vs[\\\/]", @"[\\\/]\.git[\\\/]",
                @"[\\\/]packages[\\\/]", @"[\\\/]node_modules[\\\/]", @"[\\\/]TestResults[\\\/]",
                @"[\\\/]\.idea[\\\/]", @"[\\\/]dist[\\\/]", @"[\\\/]out[\\\/]", @"[\\\/]build[\\\/]"
            };

            var excludeFilePatterns = new List<string>
            {
                @"\.g\.cs$", @"\.g\.i\.cs$", @"\.designer\.cs$", @"AssemblyInfo\.cs$",
                @"\.user$", @"\.suo$", @"\.log$", @"\.tmp$", @"package-lock\.json$", @"yarn\.lock$"
            };

            if (File.Exists(aiIgnorePath))
            {
                ConsoleHelper.Info(" Loading global ignore rules from .aiignore...");
                foreach (var line in File.ReadAllLines(aiIgnorePath).Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")))
                {
                    var rule = line.Replace("\\", "/");
                    bool isFolder = rule.EndsWith("/");
                    if (isFolder) rule = rule.TrimEnd('/');

                    var regexRule = Regex.Escape(rule).Replace(@"\*", ".*").Replace(@"\?", ".");
                    if (isFolder) excludeFolders.Add($@"[\\\/]{regexRule}[\\\/]");
                    else excludeFilePatterns.Add($@"^{regexRule}$");
                }
            }

            var outputData = new Dictionary<string, StringBuilder>();
            var outputFileCounts = new Dictionary<string, int>();
            int warningCount = 0;

            var allFiles = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles.OrderBy(f => f))
            {
                var relativePath = Path.GetRelativePath(projectPath, file).Replace("\\", "/");
                var fileName = Path.GetFileName(file);
                var extension = Path.GetExtension(file);
                var fileWithPaddedSlashes = "/" + relativePath + "/";

                if (excludeFolders.Any(f => Regex.IsMatch(fileWithPaddedSlashes, f, RegexOptions.IgnoreCase))) continue;
                if (fileName == ".gitignore") continue;
                if (excludeFilePatterns.Any(p => Regex.IsMatch(fileName, p, RegexOptions.IgnoreCase))) continue;

                string projectName = "Solution";
                bool isProjectFile = false;

                foreach (var proj in projects)
                {
                    if (file.StartsWith(proj.DirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        projectName = proj.Name;
                        isProjectFile = true;
                        break;
                    }
                }

                bool include = isProjectFile
                    ? (includeExtensions.Contains(extension) || includeSpecificFiles.Contains(fileName))
                    : (solutionIncludeExtensions.Contains(extension) || solutionSpecificFiles.Contains(fileName));

                if (!include) continue;

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
                }
                catch (Exception ex)
                {
                    ConsoleHelper.Warning($"⚠ Skipped: {relativePath} ({ex.Message})");
                    warningCount++;
                }
            }

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
