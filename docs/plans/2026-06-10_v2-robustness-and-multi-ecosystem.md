# AI Bridge v2 — Full Execution-Ready Implementation Plan

**Date:** 2026-06-10
**Status:** Pending Approval

---

## Goal

Enhance AI Bridge from a .NET-centric MVP into a robust, multi-ecosystem CLI tool with proper safety guardrails, better developer feedback, and resilient patching — while maintaining 100% backward compatibility.

---

## Summary of All Enhancements

| # | Enhancement | Description |
|---|-------------|-------------|
| 1 | Colorized Output | `Console.ForegroundColor` — green/yellow/red/cyan scheme |
| 2 | Better Error Handling | `⚠ Skipped: file (reason)` inline + final warnings summary during pack |
| 3 | Token Estimation | Show `(N files, X KB, ~Y tokens)` in pack output |
| 4 | Multi-Ecosystem Detection | Smart cascade: `.csproj` → `package.json` → manifests → folder fallback |
| 5 | System Prompt Delivery | Embed as resource, `init` writes to `aiSkills/`, committed to git, excluded from pack |
| 6 | CLI Flag Parsing | `--dry-run` and `--force` flags for `apply` command |
| 7 | Smart Safety Net | Check git status for conflicting uncommitted files before apply |
| 8 | Dry-Run Mode | `--dry-run` prints action summary without touching files |
| 9 | Failed Apply Retry | Keep failed `<patch>` blocks in `ai-response.xml`; only clear succeeded blocks |
| 10 | Fuzzy Patch Matching | Whitespace-normalized fallback when exact match fails |
| 11 | README Updates | Document new flags, aiSkills, multi-ecosystem, updated folder structure |

---

## Implementation Order & File Map

```
Step 1:  [NEW]    AIBridge/ConsoleHelper.cs
Step 2:  [MODIFY] AIBridge/Packer.cs          — colorize + error handling + token estimation
Step 3:  [MODIFY] AIBridge/Packer.cs          — multi-ecosystem detection
Step 4:  [NEW]    AIBridge/Resources/ai-system-prompt.md (embedded resource)
Step 5:  [MODIFY] AIBridge/AIBridge.csproj     — embed resource
Step 6:  [MODIFY] AIBridge/Packer.cs          — init writes system prompt to aiSkills/ + exclude aiSkills from pack
Step 7:  [MODIFY] AIBridge/Program.cs         — flag parsing
Step 8:  [MODIFY] AIBridge/Applier.cs         — colorize + safety net + dry-run + retry + fuzzy matching
Step 9:  [MODIFY] README.md                   — document all new features
```

---

## Step 1: ConsoleHelper.cs (NEW FILE)

Create `AIBridge/ConsoleHelper.cs` with this exact content:

```csharp
using System;

namespace AIBridge
{
    public static class ConsoleHelper
    {
        public static void Success(string message)
        {
            WriteColored(message, ConsoleColor.Green);
        }

        public static void Warning(string message)
        {
            WriteColored(message, ConsoleColor.Yellow);
        }

        public static void Error(string message)
        {
            WriteColored(message, ConsoleColor.Red);
        }

        public static void Info(string message)
        {
            WriteColored(message, ConsoleColor.Cyan);
        }

        public static void Default(string message)
        {
            Console.WriteLine(message);
        }

        private static void WriteColored(string message, ConsoleColor color)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = previous;
        }
    }
}
```

---

## Step 2: Packer.cs — Colorize + Error Handling + Token Estimation

### 2A. Replace all Console.WriteLine calls with ConsoleHelper calls

Find and replace these exact lines in `Packer.cs`:

| Line(s) | Current Code | Replacement |
|----------|-------------|-------------|
| 34 | `Console.WriteLine("✅ Patched .gitignore to ignore aiArtifacts/");` | `ConsoleHelper.Success("✅ Patched .gitignore to ignore aiArtifacts/");` |
| 43 | `Console.WriteLine("✅ Created default .aiignore file.");` | `ConsoleHelper.Success("✅ Created default .aiignore file.");` |
| 47 | `Console.WriteLine("ℹ .aiignore already exists.");` | `ConsoleHelper.Info("ℹ .aiignore already exists.");` |
| 98 | `Console.WriteLine(" Loading global ignore rules from .aiignore...");` | `ConsoleHelper.Info(" Loading global ignore rules from .aiignore...");` |

### 2B. Improve error handling in the file-read catch block

Replace lines 145–163 (the try-catch inside the foreach loop) with:

```csharp
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
```

Also add `int warningCount = 0;` right after the `outputFileCounts` dictionary declaration (after line 112).

### 2C. Add token & size estimation to the output loop

Replace lines 166–174 (the output-writing foreach loop) with:

```csharp
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
```

---

## Step 3: Packer.cs — Multi-Ecosystem Detection

### 3A. Add a ProjectInfo record

Add this at the bottom of the `AIBridge` namespace (inside the namespace block, after the `Packer` class closing brace):

```csharp
    public record ProjectInfo(string Name, string DirectoryPrefix);
```

### 3B. Add ecosystem-specific extension whitelists

Add these as static fields at the top of the `Packer` class (before the `Init()` method):

```csharp
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
```

### 3C. Add the detection cascade method

Add this private method to the `Packer` class:

```csharp
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
```

### 3D. Update the `Run()` method to use the detection cascade

Replace the project detection and extension whitelist section in `Run()` (lines 58–81) with:

```csharp
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
```

Also update the file-inclusion check in the foreach loop. Replace lines 129–143 with:

```csharp
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
```

> **Note:** The `projects` variable type changes from an anonymous type list to `List<ProjectInfo>`. The property names `Name` and `DirectoryPrefix` remain the same, so the rest of the loop logic is unchanged.

---

## Step 4: Embed System Prompt as Resource

### 4A. Create the resource file

Copy the existing `ai-system-prompt.md` from the repo root into a new location:

```
AIBridge/Resources/ai-system-prompt.md
```

This is a copy of the file at the repo root. The content is identical.

### 4B. Update AIBridge.csproj to embed the resource

Add this `<ItemGroup>` to `AIBridge/AIBridge.csproj` (after the existing `<ItemGroup>`):

```xml
  <ItemGroup>
    <EmbeddedResource Include="Resources\ai-system-prompt.md">
      <LogicalName>AIBridge.Resources.ai-system-prompt.md</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
```

---

## Step 5: Update Packer.cs — Init writes system prompt to aiSkills/

### 5A. Add system prompt extraction to `Init()`

Add this block at the end of the `Init()` method (before the closing brace), after the `.aiignore` creation logic:

```csharp
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
```

### 5B. Add aiSkills/ to the hardcoded excluded folders list

In the `Run()` method, add `aiSkills` to the `excludeFolders` list. Add this entry to the existing list:

```csharp
                @"[\\/]aiSkills[\\/]",
                @"[\\/]aiArtifacts[\\/]",
```

> **Note:** `aiArtifacts` is currently excluded implicitly because the file iteration starts from `projectPath` and the artifacts folder is under it but files inside don't match the extension whitelist. However, it's safer to add it explicitly too. Both `aiSkills` and `aiArtifacts` should be in the hardcoded exclude list.

---

## Step 6: Program.cs — Flag Parsing

Replace the entire content of `AIBridge/Program.cs` with:

```csharp
using System;
using System.IO;
using System.Linq;

namespace AIBridge
{
    class Program
    {
        static void Main(string[] args)
        {
            var command = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            var flags = args.Skip(1).Select(a => a.ToLowerInvariant()).ToHashSet();

            switch (command)
            {
                case "init":
                    ConsoleHelper.Info("Initializing AI Bridge workspace...");
                    Packer.Init();
                    break;

                case "pack":
                    ConsoleHelper.Info("Packing AI context...");
                    Packer.Run();
                    break;

                case "apply":
                    ConsoleHelper.Info("Applying AI code changes...");
                    bool dryRun = flags.Contains("--dry-run");
                    bool force = flags.Contains("--force");
                    Applier.Run(dryRun, force);
                    break;

                default:
                    Console.WriteLine("Usage: ai-bridge [command]");
                    Console.WriteLine("Commands:");
                    Console.WriteLine("  init                - Creates default .aiignore, patches .gitignore, and sets up aiSkills/.");
                    Console.WriteLine("  pack                - Packs source files into text context for AI.");
                    Console.WriteLine("  apply [options]     - Applies ai-response.xml patches to the codebase.");
                    Console.WriteLine();
                    Console.WriteLine("Apply Options:");
                    Console.WriteLine("  --dry-run           - Preview changes without modifying files.");
                    Console.WriteLine("  --force             - Apply even if there are uncommitted changes in target files.");
                    break;
            }
        }
    }
}
```

---

## Step 7: Applier.cs — Complete Rewrite

Replace the entire content of `AIBridge/Applier.cs` with:

```csharp
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
        public static void Run(bool dryRun = false, bool force = false)
        {
            var projectPath = Environment.CurrentDirectory;
            var artifactsDir = Path.Combine(projectPath, "aiArtifacts");
            var inputFile = Path.Combine(artifactsDir, "ai-response.xml");
            var failedLogFile = Path.Combine(artifactsDir, "failed-patches.txt");

            if (!File.Exists(inputFile))
            {
                ConsoleHelper.Error($"Error: Cannot find '{inputFile}'.");
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
                ConsoleHelper.Error($"Error: '{inputFile}' is not valid XML. {ex.Message}");
                return;
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
                var p = node.Attributes?["file"]?.Value.Trim();
                if (!string.IsNullOrEmpty(p)) targetFiles.Add(p.Replace('/', Path.DirectorySeparatorChar));
            }
            foreach (XmlNode node in xml.SelectNodes("//delete")!)
            {
                var p = node.Attributes?["path"]?.Value.Trim();
                if (!string.IsNullOrEmpty(p)) targetFiles.Add(p.Replace('/', Path.DirectorySeparatorChar));
            }

            // Smart safety net: check for uncommitted changes in target files only
            if (!force && !dryRun)
            {
                var conflicts = GetUncommittedConflicts(projectPath, targetFiles);
                if (conflicts.Count > 0)
                {
                    ConsoleHelper.Warning("⚠ These files have uncommitted changes and will be overwritten:");
                    foreach (var conflict in conflicts)
                    {
                        ConsoleHelper.Warning($"   - {conflict}");
                    }
                    ConsoleHelper.Warning("\nRun 'ai-bridge apply --force' to apply anyway, or commit/stash your changes first.");
                    return;
                }
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
                var relPath = node.Attributes?["file"]?.Value.Trim();
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
                    File.WriteAllText(inputFile, "<!-- Paste the AI response XML here -->\n");
                    ConsoleHelper.Success("✅ Cleared ai-response.xml to prevent accidental re-application.");
                }
            }
        }

        private static List<string> GetUncommittedConflicts(string projectPath, HashSet<string> targetFiles)
        {
            var conflicts = new List<string>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "status --porcelain",
                    WorkingDirectory = projectPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return conflicts;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0) return conflicts;

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    // git status --porcelain format: "XY filename" (first 3 chars are status + space)
                    if (line.Length < 4) continue;
                    var filePath = line.Substring(3).Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);

                    if (targetFiles.Contains(filePath))
                    {
                        conflicts.Add(filePath);
                    }
                }
            }
            catch
            {
                // If git is not available, skip the check silently
            }

            return conflicts;
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
```

---

## Step 8: README.md Updates

The following sections need to be added or modified in `README.md`:

### 8A. Update "The Big Picture" diagram

Add `aiSkills/` to the flow. Update the diagram (lines 11–32) — no structural change needed, just add a note about `aiSkills/` in the prerequisites or Step 0.

### 8B. Update Step 0 (Init) section

Replace the content of Step 0 (lines 67–75) with:

~~~markdown
## Step 0 — Initialize AI Workspace (Optional)

If you want to configure which files to hide from the AI *before* you pack your code, run the init command:

```bash
ai-bridge init
```

This automatically:
- Creates a default `.aiignore` file (controls which files are excluded from packing).
- Patches your `.gitignore` so AI working files never get committed.
- Creates `aiSkills/ai-system-prompt.md` — the system prompt you paste into your browser AI.

> **Tip:** The `aiSkills/` folder is designed to be committed to your repo so your team shares the same AI instructions.
~~~

### 8C. Add new section: Apply Options (after Step 4)

Add after the "After the run" bullet (after line 148):

~~~markdown
### Apply Options

| Flag | Description |
|------|-------------|
| `--dry-run` | Preview what changes would be made without modifying any files. |
| `--force` | Apply even if target files have uncommitted changes. |

**Dry-run example:**
```bash
ai-bridge apply --dry-run
```
Output:
```text
  CREATE: MyApp/Services/NewService.cs
  OVERWRITE: MyApp/Controllers/OrderController.cs
  PATCH: MyApp/Models/Order.cs
  DELETE: MyApp/Services/OldService.cs

Dry run complete: 2 file(s), 1 patch(es), 1 delete(s).
No files were modified. Run 'ai-bridge apply' to apply for real.
```

**Safety check:** If any target files have uncommitted changes, `apply` will warn you and abort. Use `--force` to override:
```bash
ai-bridge apply --force
```
~~~

### 8D. Update "What it generates" folder structure (lines 168–178)

Replace with:

~~~markdown
## What it generates in your project

When you run `ai-bridge init` or `ai-bridge pack`, it sets up two folders:

```text
YourProjectRoot\
├── aiSkills\                   ← Committed to git (team-shared)
│   └── ai-system-prompt.md    ← System prompt for your browser AI
└── aiArtifacts\                ← Auto-created, gitignored
    ├── *-context.txt           ← Output of ai-bridge pack
    ├── ai-response.xml         ← AI response you paste/download here
    └── failed-patches.txt      ← Created only when patches fail
```
~~~

### 8E. Add "Multi-Ecosystem Support" section (after Prerequisites)

Add a new section:

~~~markdown
## Supported Ecosystems

AI Bridge automatically detects your project type and groups files intelligently:

| Ecosystem | Detected By | Grouping |
|-----------|-------------|----------|
| .NET | `.csproj` files | One context file per project |
| Node.js | `package.json` in subfolders | One context file per package |
| Python | `pyproject.toml` | One context file per package |
| Go | `go.mod` | One context file per module |
| Rust | `Cargo.toml` | One context file per crate |
| Other | (fallback) | One context file per top-level folder |

Root-level files (e.g., `docker-compose.yml`, `README.md`) are always packed into a `*-Solution-context.txt` file.
~~~

---

## Verification Plan

### Manual Verification Checklist

1. **Build the project**
   ```bash
   cd AIBridge
   dotnet build
   ```
   Must compile with zero errors.

2. **Test `ai-bridge init`**
   - Run in a temp folder → confirm `aiArtifacts/`, `aiSkills/ai-system-prompt.md`, `.aiignore` are created
   - Run again → confirm "already exists" messages appear (no duplicates)

3. **Test `ai-bridge pack` on a .NET project**
   - Run on this repo itself → should detect .NET, show token/KB counts, green success output

4. **Test `ai-bridge pack` on a non-.NET folder**
   - Create a temp folder with subfolders and random files → should use folder fallback

5. **Test `ai-bridge apply --dry-run`**
   - Create a sample `ai-response.xml` → run with `--dry-run` → confirm no files modified, summary printed in cyan

6. **Test safety net**
   - Modify a file the AI response targets, don't commit → `ai-bridge apply` should warn in yellow and abort
   - `ai-bridge apply --force` → should apply anyway

7. **Test failed apply retry**
   - Create response with valid `<file>` + invalid `<patch>` → apply → confirm only failed patch remains in `ai-response.xml`

8. **Test fuzzy matching**
   - Create patch with slightly different whitespace in `<search>` → confirm "Patched (fuzzy)" in yellow

9. **Test colorized output**
   - Confirm green, yellow, red, cyan colors render correctly in PowerShell / Windows Terminal

---

## Breaking Changes

**None.** All enhancements are additive:
- Existing `init`, `pack`, `apply` commands work exactly as before
- New flags (`--dry-run`, `--force`) are opt-in
- Multi-ecosystem detection falls through to existing .NET logic when `.csproj` files are present
- Fuzzy matching only activates when exact matching already fails
- `aiSkills/` is new and doesn't affect existing workflows
