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
                    Console.WriteLine("✅ Patched .gitignore to ignore aiArtifacts/");
                }
            }

            var aiIgnorePath = Path.Combine(projectPath, ".aiignore");
            if (!File.Exists(aiIgnorePath))
            {
                var defaultIgnore = "# Folders should end with /\nbin/\nobj/\n.vs/\n.git/\nnode_modules/\ndist/\nout/\nTestResults/\n*.g.cs\n*.log\n*.tmp\n";
                File.WriteAllText(aiIgnorePath, defaultIgnore);
                Console.WriteLine("✅ Created default .aiignore file.");
            }
            else
            {
                Console.WriteLine("ℹ .aiignore already exists.");
            }
        }

        public static void Run()
        {
            Init();
            var projectPath = Environment.CurrentDirectory;
            var artifactsDir = Path.Combine(projectPath, "aiArtifacts");
            var aiIgnorePath = Path.Combine(projectPath, ".aiignore");

            var rootFolderName = new DirectoryInfo(projectPath).Name;
            var projects = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
                .Select(p => new { Name = Path.GetFileNameWithoutExtension(p), DirectoryPrefix = Path.GetDirectoryName(p) + Path.DirectorySeparatorChar })
                .OrderByDescending(p => p.DirectoryPrefix.Length)
                .ToList();

            var includeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".csproj", ".props", ".targets", ".json", ".config", ".xml", ".xaml",
                ".cshtml", ".razor", ".html", ".css", ".scss", ".js", ".ts",
                ".fs", ".vb", ".resx", ".sql", ".ps1", ".cmd", ".sh", ".yml", ".yaml", ".ini", ".env", ".md"
            };

            var includeSpecificFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "appsettings.json", "appsettings.Development.json", "nuget.config", "Dockerfile", ".dockerignore"
            };

            var solutionIncludeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".sln", ".slnx", ".props", ".targets" };
            var solutionSpecificFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "global.json", "nuget.config", "Directory.Build.props", "Directory.Build.targets",
                ".editorconfig", "docker-compose.yml", "docker-compose.yaml", "docker-compose.dcproj"
            };

            var excludeFolders = new List<string>
            {
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
                Console.WriteLine(" Loading global ignore rules from .aiignore...");
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
                catch
                {
                    Console.WriteLine($"Warning: Could not read file {file}");
                }
            }

            foreach (var key in outputData.Keys)
            {
                var outName = key == "Solution" ? $"{rootFolderName}-Solution-context.txt" : $"{key}-context.txt";
                var outPath = Path.Combine(artifactsDir, outName);
                var finalContent = $"<project name=\"{key}\" files=\"{outputFileCounts[key]}\">\n{outputData[key]}\n</project>\n";

                File.WriteAllText(outPath, finalContent, Encoding.UTF8);
                Console.WriteLine($"SUCCESS: {key} codebase packed ({outputFileCounts[key]} files) into {outName}");
            }
        }
    }
}
