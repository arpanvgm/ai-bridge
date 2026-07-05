# AI Bridge — Implementation Plan

> **For AI coding agents.** Execute each phase in order. Every phase ends with a checkpoint — do not proceed until the checkpoint passes. All file paths are relative to `/home/arpanvgm/github/arpanvgm/ai-bridge`.

---

## Phase 1: Solution Restructure

### Step 1.1 — Create directory structure

```bash
cd /home/arpanvgm/github/arpanvgm/ai-bridge
mkdir -p src/AIBridge.Core/{Abstractions,Constants,Helpers,Models,Services,Templates}
mkdir -p src/AIBridge.Cli/{Commands,Helpers}
```

### Step 1.2 — Create `src/AIBridge.Core/AIBridge.Core.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>AIBridge.Core</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="Templates\**\*" />
  </ItemGroup>

</Project>
```

### Step 1.3 — Create `src/AIBridge.Cli/AIBridge.Cli.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>AIBridge.Cli</RootNamespace>

    <PackAsTool>true</PackAsTool>
    <ToolCommandName>ai-bridge</ToolCommandName>
    <PackageId>Tools.AIBridge</PackageId>
    <Version>1.0.7</Version>
    <Authors>Arpan</Authors>
    <Description>A lightweight, language-agnostic CLI tool for packing project source code into AI-readable context and applying AI-generated code changes back to your codebase.</Description>
    <PackageTags>ai;chatgpt;claude;gemini;cli;tool;pack;apply;context</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <RepositoryUrl>https://github.com/arpanvgm/ai-bridge</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AIBridge.Core\AIBridge.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  </ItemGroup>

</Project>
```

### Step 1.4 — Create solution and add projects

```bash
cd /home/arpanvgm/github/arpanvgm/ai-bridge
dotnet new sln --name ai-bridge
dotnet sln add src/AIBridge.Core/AIBridge.Core.csproj
dotnet sln add src/AIBridge.Cli/AIBridge.Cli.csproj
```

### Step 1.5 — Copy templates to Core

```bash
cp -r AIBridge/Templates/* src/AIBridge.Core/Templates/
```

### ✅ Checkpoint Phase 1

```bash
dotnet build ai-bridge.sln
```

---

## Phase 2: Create Core Abstractions & Models

### Step 2.1 — `src/AIBridge.Core/Abstractions/IAIBridgeLogger.cs`

```csharp
namespace AIBridge.Core.Abstractions;

public interface IAIBridgeLogger
{
    void Success(string message);
    void Warning(string message);
    void Error(string message);
    void Info(string message);
    void Output(string message);
}
```

### Step 2.2 — `src/AIBridge.Core/Abstractions/IInputProvider.cs`

```csharp
namespace AIBridge.Core.Abstractions;

public interface IInputProvider
{
    Task<string?> GetClipboardTextAsync();
    Task<string?> ReadFromStdinAsync(string prompt);
    Task SetClipboardTextAsync(string text);
}
```

### Step 2.3 — `src/AIBridge.Core/Models/ProjectInfo.cs`

```csharp
namespace AIBridge.Core.Models;

public record ProjectInfo(string Name, string DirectoryPrefix);
```

### Step 2.4 — `src/AIBridge.Core/Models/PackOptions.cs`

```csharp
namespace AIBridge.Core.Models;

public record PackOptions(
    bool Incremental = false);
```

### Step 2.5 — `src/AIBridge.Core/Models/ApplyOptions.cs`

```csharp
namespace AIBridge.Core.Models;

public record ApplyOptions(
    bool Watch = false,
    bool Paste = false,
    bool DryRun = false);
```

### Step 2.6 — `src/AIBridge.Core/Models/PackResult.cs`

```csharp
namespace AIBridge.Core.Models;

public record PackResult(
    bool IsSuccess,
    int FileCount = 0,
    long TotalSizeBytes = 0,
    int ApproxTokens = 0,
    List<string>? Warnings = null,
    string? ErrorMessage = null);
```

### Step 2.7 — `src/AIBridge.Core/Models/ApplyResult.cs`

```csharp
namespace AIBridge.Core.Models;

public record ApplyResult(
    bool IsSuccess,
    int Created = 0,
    int Patched = 0,
    int Deleted = 0,
    int PatchFailed = 0,
    List<string>? FailedFiles = null,
    string? ErrorMessage = null);
```

### Step 2.8 — `src/AIBridge.Core/Models/IndexStatusResult.cs`

```csharp
namespace AIBridge.Core.Models;

public record IndexStatusResult(
    bool IsSuccess,
    List<string>? Modified = null,
    List<string>? NewFiles = null,
    List<string>? Deleted = null,
    DateTime? LastUpdated = null,
    string? ErrorMessage = null);
```

### Step 2.9 — `src/AIBridge.Core/Models/InitResult.cs`

```csharp
namespace AIBridge.Core.Models;

public record InitResult(
    bool IsSuccess,
    List<string>? ExtractedFiles = null,
    List<string>? SkippedFiles = null,
    string? ErrorMessage = null);
```

### ✅ Checkpoint Phase 2

```bash
dotnet build ai-bridge.sln
```

---

## Phase 3: Create Core Constants & Helpers

### Step 3.1 — `src/AIBridge.Core/Constants/FileNames.cs`

```csharp
namespace AIBridge.Core.Constants;

public static class FileNames
{
    public const string AiIgnore = ".aiignore";
    public const string Index = "index.xml";
    public const string ResponseXml = "ai-response.xml";
    public const string FailedPatches = "failed-patches.txt";
    public const string IncrementalContext = "ai-incremental-context.txt";
    public const string RequestedContext = "ai-requested-context.txt";
}
```

### Step 3.2 — `src/AIBridge.Core/Constants/FolderNames.cs`

```csharp
namespace AIBridge.Core.Constants;

public static class FolderNames
{
    public const string AiBridge = "ai-bridge";
    public const string Artifacts = "artifacts";
    public const string SimpleMode = "1-SimpleMode";
    public const string AdvancedMode = "2-AdvancedMode";
}
```

### Step 3.3 — `src/AIBridge.Core/Constants/Timings.cs`

```csharp
namespace AIBridge.Core.Constants;

public static class Timings
{
    // Milliseconds to wait before accepting a new file change event
    public const int WatchDebounceMs = 1000;

    // Milliseconds to pause to allow file locks to release before reading
    public const int FileLockWaitMs = 500;
}
```

### Step 3.4 — `src/AIBridge.Core/Constants/XmlTags.cs`

```csharp
namespace AIBridge.Core.Constants;

public static class XmlTags
{
    public const string AiResponse = "ai-response";
    public const string AiRequest = "ai-request";
    public const string CreateIndex = "create-ai-bridge-index";
    public const string UpdateIndex = "update-ai-bridge-index";
    public const string AiEdits = "ai-edits";
    public const string File = "file";
    public const string Patch = "patch";
    public const string Delete = "delete";
    public const string Module = "module";
}
```

### Step 3.5 — `src/AIBridge.Core/Helpers/WorkspaceHelper.cs`

Only pure-logic methods. `GetProjectRoot()` stays in CLI.

```csharp
namespace AIBridge.Core.Helpers;

public static class WorkspaceHelper
{
    public static string GetAiWorkspacePath(string projectRoot)
    {
        return Path.Combine(projectRoot, Constants.FolderNames.AiBridge);
    }

    public static string GetIndexFileName(string projectRoot)
    {
        return Constants.FileNames.Index;
    }

    public static string SafeResolvePath(string projectRoot, string relativePath)
    {
        var resolved = Path.GetFullPath(Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var fullProjectRoot = Path.GetFullPath(projectRoot);

        if (!fullProjectRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            fullProjectRoot += Path.DirectorySeparatorChar;
        }

        if (!resolved.StartsWith(fullProjectRoot, StringComparison.OrdinalIgnoreCase) && resolved != Path.GetFullPath(projectRoot))
        {
            throw new System.Security.SecurityException($"Path '{relativePath}' resolves outside project root. Blocked.");
        }
        return resolved;
    }
}
```

### Step 3.6 — `src/AIBridge.Core/Helpers/FileFilterHelper.cs`

```csharp
using System.Text.RegularExpressions;

namespace AIBridge.Core.Helpers;

public static class FileFilterHelper
{
    // Binary/non-text extensions to always exclude from packing
    public static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
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
    public static readonly HashSet<string> ExcludeFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
        ".DS_Store", "Thumbs.db", ".gitignore", ".dockerignore", ".aiignore", "ai-bridge-index.xml"
    };

    public static (List<string> folders, List<string> files) LoadAiIgnoreRules(string aiIgnorePath)
    {
        List<string> folders = [];
        List<string> files = [];

        if (File.Exists(aiIgnorePath))
        {
            foreach (var line in File.ReadAllLines(aiIgnorePath)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")))
            {
                var rule = line.Replace("\\", "/");
                bool isFolder = rule.EndsWith("/");
                if (isFolder) rule = rule.TrimEnd('/');

                var regexRule = Regex.Escape(rule).Replace(@"\*", ".*").Replace(@"\?", ".");
                if (isFolder) folders.Add($@"[\\/]{regexRule}[\\/]");
                else files.Add($@"^{regexRule}$");
            }
        }

        return (folders, files);
    }

    public static bool IsAiIgnored(string relativePath, string fileName, List<string> aiIgnoreExcludeFolders, List<string> aiIgnoreExcludeFilePatterns)
    {
        if (aiIgnoreExcludeFolders.Count > 0 || aiIgnoreExcludeFilePatterns.Count > 0)
        {
            var paddedPath = "/" + relativePath + "/";
            if (aiIgnoreExcludeFolders.Any(f => Regex.IsMatch(paddedPath, f, RegexOptions.IgnoreCase))) return true;
            if (aiIgnoreExcludeFilePatterns.Any(p => Regex.IsMatch(fileName, p, RegexOptions.IgnoreCase))) return true;
        }
        return false;
    }
}
```

### ✅ Checkpoint Phase 3

```bash
dotnet build src/AIBridge.Core/AIBridge.Core.csproj
```

---

## Phase 4: Create Core Services

### Step 4.1 — `src/AIBridge.Core/Services/StateService.cs`

```csharp
using System.Reflection;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class StateService(string projectRoot, IAIBridgeLogger logger)
{
    public static string GetCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version
            ?? Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    private string GetStateFilePath()
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        return Path.Combine(aiWorkspace, "state.xml");
    }

    private XmlDocument LoadOrCreateState()
    {
        var stateFile = GetStateFilePath();
        var doc = new XmlDocument();

        if (File.Exists(stateFile))
        {
            try
            {
                doc.Load(stateFile);
                if (doc.DocumentElement != null && doc.DocumentElement.Name == "ai-bridge-state")
                    return doc;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"XML Parse Error: {ex.Message}");
            }
        }

        var root = doc.CreateElement("ai-bridge-state");
        doc.AppendChild(root);
        return doc;
    }

    private void SaveState(XmlDocument doc)
    {
        var stateFile = GetStateFilePath();
        var dir = Path.GetDirectoryName(stateFile);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        doc.Save(stateFile);
    }

    private static void SetAttribute(XmlDocument doc, string name, string value)
    {
        doc.DocumentElement?.SetAttribute(name, value);
    }

    public bool EnsureUpToDate()
    {
        var stateFile = GetStateFilePath();
        if (!File.Exists(stateFile))
        {
            logger.Warning("AI Bridge is not initialized in this project.");
            logger.Info("Please run 'ai-bridge init' first.");
            return false;
        }

        var stateDoc = LoadOrCreateState();
        var localVersion = stateDoc.DocumentElement?.GetAttribute("version") ?? "";
        var currentVersion = GetCurrentVersion();

        if (localVersion != currentVersion)
        {
            logger.Warning($"Version mismatch! Tool version is {currentVersion}, but local templates are version {localVersion}.");
            logger.Info("Please run 'ai-bridge update' to sync the templates with the latest tool implementation.");
            logger.Info("Note: This will overwrite any custom changes in the template directories to ensure compatibility.");
            return false;
        }

        return true;
    }

    public void InitState()
    {
        var doc = LoadOrCreateState();
        SetAttribute(doc, "version", GetCurrentVersion());
        var workspaceName = new DirectoryInfo(projectRoot).Name;
        SetAttribute(doc, "workspaceName", workspaceName);

        if (string.IsNullOrEmpty(doc.DocumentElement?.GetAttribute("initializedAt")))
            SetAttribute(doc, "initializedAt", DateTime.UtcNow.ToString("o"));

        SaveState(doc);
    }

    public void UpdateEcosystem(string ecosystem)
    {
        var doc = LoadOrCreateState();
        SetAttribute(doc, "ecosystem", ecosystem);
        SaveState(doc);
    }

    public void UpdateLastPacked()
    {
        var doc = LoadOrCreateState();
        SetAttribute(doc, "lastPackedAt", DateTime.UtcNow.ToString("o"));
        SaveState(doc);
    }
}
```

### Step 4.2 — `src/AIBridge.Core/Services/PatcherService.cs`

```csharp
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AIBridge.Core.Abstractions;

namespace AIBridge.Core.Services;

public class PatcherService(IAIBridgeLogger logger)
{
    public async Task<bool> ApplyPatchAsync(
        XmlNode node,
        string projectPath,
        List<string> failedFiles,
        List<XmlNode> failedPatchNodes)
    {
        var relPath = node.Attributes?["path"]?.Value?.Trim();
        if (string.IsNullOrEmpty(relPath))
        {
            logger.Error("Patch failed: missing 'path' attribute on <patch> tag.");
            failedPatchNodes.Add(node);
            return false;
        }

        var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));
        var searchNode = node.SelectSingleNode("search");
        var replaceNode = node.SelectSingleNode("replace");

        if (!File.Exists(absPath) || searchNode == null || replaceNode == null)
        {
            logger.Error($"Patch failed: File not found or invalid XML -> {relPath}");
            failedFiles.Add(relPath);
            failedPatchNodes.Add(node);
            return false;
        }

        var targetContent = Normalize(await File.ReadAllTextAsync(absPath));
        var search = TrimCDATA(Normalize(searchNode.InnerText));
        var replace = TrimCDATA(Normalize(replaceNode.InnerText));

        if (targetContent.Contains(search))
        {
            var index = targetContent.IndexOf(search, StringComparison.Ordinal);
            var updated = string.Concat(
                targetContent.AsSpan(0, index),
                replace,
                targetContent.AsSpan(index + search.Length));

            await File.WriteAllTextAsync(absPath, updated, Encoding.UTF8);
            logger.Success($"Patched: {relPath}");
            return true;
        }
        else if (TryFuzzyPatch(targetContent, search, replace, out var fuzzyResult))
        {
            await File.WriteAllTextAsync(absPath, fuzzyResult, Encoding.UTF8);
            logger.Warning($"Patched (fuzzy): {relPath}");
            return true;
        }
        else
        {
            logger.Error($"Patch failed: Match not found -> {relPath}");
            failedFiles.Add(relPath);
            failedPatchNodes.Add(node);
            return false;
        }
    }

    public static async Task RebuildResponseWithFailedPatchesAsync(string inputFile, List<XmlNode> failedPatchNodes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ai-response>");
        sb.AppendLine("<ai-edits>");
        sb.AppendLine();

        foreach (var node in failedPatchNodes)
        {
            sb.AppendLine(node.OuterXml);
            sb.AppendLine();
        }

        sb.AppendLine("</ai-edits>");
        sb.AppendLine("</ai-response>");
        await File.WriteAllTextAsync(inputFile, sb.ToString(), Encoding.UTF8);
    }

    internal static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");
    internal static string TrimCDATA(string text) => Regex.Replace(Regex.Replace(text, @"^\r?\n", ""), @"\r?\n[ \t]*$", "");

    private static string NormalizeWhitespace(string text) => Regex.Replace(text, @"[ \t]+", " ").Trim();
    private static string NormalizeLineWhitespace(string line) => Regex.Replace(line, @"[ \t]+", " ").Trim();

    private static bool TryFuzzyPatch(string fileContent, string search, string replace, out string result)
    {
        result = fileContent;

        var normalizedFile = NormalizeWhitespace(fileContent);
        var normalizedSearch = NormalizeWhitespace(search);

        if (!normalizedFile.Contains(normalizedSearch))
            return false;

        var fileLines = fileContent.Split('\n');
        var searchLines = search.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToArray();

        if (searchLines.Length == 0) return false;

        var normalizedSearchLines = searchLines.Select(NormalizeLineWhitespace).ToArray();

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
}
```

### Step 4.3 — `src/AIBridge.Core/Services/IndexService.cs`

```csharp
using System.Text;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class IndexService(IAIBridgeLogger logger)
{
    public void HandleCreate(XmlNode root, string projectPath)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
        var indexFile = Path.Combine(aiWorkspace, WorkspaceHelper.GetIndexFileName(projectPath));

        var doc = new XmlDocument();
        var indexRoot = doc.CreateElement("ai-bridge-index");
        indexRoot.SetAttribute("lastUpdated", DateTime.UtcNow.ToString("o"));

        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType == XmlNodeType.Element)
            {
                var importedNode = doc.ImportNode(node, true);
                indexRoot.AppendChild(importedNode);
            }
        }
        doc.AppendChild(indexRoot);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(false)
        };

        using (var writer = XmlWriter.Create(indexFile, settings))
        {
            doc.Save(writer);
        }

        logger.Success("✅ Generated new ai-bridge-index.xml successfully.");
    }

    public void HandleUpdate(XmlNode root, string projectPath)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
        var indexFileName = WorkspaceHelper.GetIndexFileName(projectPath);
        var indexFile = Path.Combine(aiWorkspace, indexFileName);

        if (!File.Exists(indexFile))
        {
            var emptyDoc = new XmlDocument();
            var emptyRoot = emptyDoc.CreateElement("ai-bridge-index");
            emptyDoc.AppendChild(emptyRoot);
            emptyDoc.Save(indexFile);
        }

        var xml = new XmlDocument();
        try { xml.Load(indexFile); }
        catch (Exception ex)
        {
            logger.Error($"Error parsing existing {indexFileName}: {ex.Message}");
            return;
        }

        var indexRoot = xml.DocumentElement;
        if (indexRoot == null || indexRoot.Name != "ai-bridge-index")
        {
            logger.Error($"Error: {indexFileName} is malformed (missing <ai-bridge-index> root).");
            return;
        }

        int updatedCount = 0, addedCount = 0, deletedCount = 0;

        var deleteNodes = root.SelectNodes($"//{XmlTags.Delete}");
        if (deleteNodes != null)
        {
            foreach (XmlNode delNode in deleteNodes)
            {
                var path = delNode.Attributes?["path"]?.Value;
                if (!string.IsNullOrEmpty(path))
                {
                    var targetNode = indexRoot.SelectSingleNode($"//file[@path='{path}']");
                    if (targetNode != null)
                    {
                        var moduleNode = targetNode.ParentNode;
                        moduleNode?.RemoveChild(targetNode);
                        deletedCount++;
                        if (moduleNode != null && moduleNode.SelectNodes(XmlTags.File)?.Count == 0)
                            indexRoot.RemoveChild(moduleNode);
                    }
                }
            }
        }

        var moduleNodes = root.SelectNodes(XmlTags.Module);
        if (moduleNodes != null)
        {
            foreach (XmlNode moduleNode in moduleNodes)
            {
                var moduleName = moduleNode.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(moduleName)) continue;

                var targetModule = indexRoot.SelectSingleNode($"{XmlTags.Module}[@name='{moduleName}']");
                if (targetModule == null)
                {
                    targetModule = xml.CreateElement(XmlTags.Module);
                    var nameAttr = xml.CreateAttribute("name");
                    nameAttr.Value = moduleName;
                    targetModule.Attributes?.Append(nameAttr);
                    indexRoot.AppendChild(targetModule);
                }

                foreach (XmlNode fileNode in moduleNode.SelectNodes(XmlTags.File)!)
                {
                    var path = fileNode.Attributes?["path"]?.Value;
                    if (string.IsNullOrEmpty(path)) continue;

                    var purpose = fileNode.Attributes?["purpose"]?.Value;
                    if (string.IsNullOrEmpty(purpose))
                        purpose = fileNode.InnerText.Trim();

                    var targetFile = targetModule?.SelectSingleNode($"file[@path='{path}']") as XmlElement;
                    if (targetFile != null)
                    {
                        targetFile.SetAttribute("purpose", purpose);
                        updatedCount++;
                    }
                    else
                    {
                        targetFile = xml.CreateElement("file");
                        targetFile.SetAttribute("path", path);
                        targetFile.SetAttribute("purpose", purpose);
                        targetModule?.AppendChild(targetFile);
                        addedCount++;
                    }
                }
            }
        }

        var floatingFiles = root.SelectNodes(XmlTags.File);
        if (floatingFiles != null)
        {
            foreach (XmlNode fileNode in floatingFiles)
            {
                var path = fileNode.Attributes?["path"]?.Value;
                if (string.IsNullOrEmpty(path)) continue;
                var purpose = fileNode.Attributes?["purpose"]?.Value;
                if (string.IsNullOrEmpty(purpose)) purpose = fileNode.InnerText.Trim();

                var targetFile = indexRoot.SelectSingleNode($"//file[@path='{path}']") as XmlElement;
                if (targetFile != null)
                {
                    targetFile.SetAttribute("purpose", purpose);
                    updatedCount++;
                }
                else
                {
                    logger.Warning($"Cannot add '{path}' without a <module>. Please wrap new files in a <module>.");
                }
            }
        }

        indexRoot.SetAttribute("lastUpdated", DateTime.UtcNow.ToString("o"));

        var settings = new XmlWriterSettings
        {
            Indent = true, IndentChars = "  ",
            OmitXmlDeclaration = false, Encoding = new UTF8Encoding(false)
        };
        using (var writer = XmlWriter.Create(indexFile, settings))
        {
            xml.Save(writer);
        }

        logger.Success($"✅ Updated {indexFileName}: {addedCount} added, {updatedCount} updated, {deletedCount} deleted.");
    }
}
```

### Step 4.4 — `src/AIBridge.Core/Services/InputService.cs`

```csharp
using System.Text;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;

namespace AIBridge.Core.Services;

public class InputService(IAIBridgeLogger logger, IInputProvider inputProvider)
{
    public async Task<bool> ResolveAsync(string inputFile, bool paste)
    {
        var artifactsDir = Path.GetDirectoryName(inputFile)!;
        if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

        if (!paste)
        {
            if (File.Exists(inputFile))
            {
                logger.Info($"Reading AI response from {FileNames.ResponseXml}.");
                return true;
            }

            logger.Error($"File not found: {FileNames.ResponseXml}");
            logger.Info($"Paste content into the file, or use 'ai-bridge apply --paste'.");
            return false;
        }

        string? content = null;
        try
        {
            content = await inputProvider.GetClipboardTextAsync();
        }
        catch
        {
            // Suppress clipboard errors — falls back to stdin
        }

        if (!string.IsNullOrWhiteSpace(content))
        {
            await File.WriteAllTextAsync(inputFile, content, Encoding.UTF8);
            logger.Info($"Read AI response from clipboard → saved to {FileNames.ResponseXml}.");
            return true;
        }

        content = await inputProvider.ReadFromStdinAsync("Paste your entire AI response XML below and then press Enter:");

        if (!string.IsNullOrWhiteSpace(content))
        {
            await File.WriteAllTextAsync(inputFile, content, Encoding.UTF8);
            logger.Info($"Read AI response from stdin → saved to {FileNames.ResponseXml}.");
            return true;
        }

        logger.Error("Error: No content received.");
        logger.Info($"Save the AI response to '{FileNames.ResponseXml}' and run 'ai-bridge apply'.");
        return false;
    }

    public async Task ResetInputFileAsync(string inputFile)
    {
        var content = "<!-- Paste the AI response XML here -->\n";
        await File.WriteAllTextAsync(inputFile, content);
        logger.Info($"\nReset {FileNames.ResponseXml} for the next prompt.");
    }
}
```

### Step 4.5 — `src/AIBridge.Core/Services/ProjectDetector.cs`

```csharp
using AIBridge.Core.Abstractions;
using AIBridge.Core.Models;

namespace AIBridge.Core.Services;

public class ProjectDetector(IAIBridgeLogger logger)
{
    private static List<ProjectInfo>? TryDetect(string projectPath, string fileName, Func<string, string> nameSelector, bool excludeRoot = false)
    {
        var files = Directory.GetFiles(projectPath, fileName, SearchOption.AllDirectories);
        if (excludeRoot)
            files = files.Where(p => Path.GetDirectoryName(p) != projectPath).ToArray();

        if (files.Length == 0) return null;
        return files
            .Select(p => new ProjectInfo(
                nameSelector(p),
                Path.GetDirectoryName(p)! + Path.DirectorySeparatorChar))
            .OrderByDescending(p => p.DirectoryPrefix.Length)
            .ToList();
    }

    public (List<ProjectInfo> projects, string ecosystem) DetectProjects(string projectPath)
    {
        var dotnetProjects = TryDetect(projectPath, "*.csproj", p => Path.GetFileNameWithoutExtension(p));
        if (dotnetProjects != null)
        {
            logger.Info("Detected ecosystem: .NET (found .csproj files)");
            return (dotnetProjects, "dotnet");
        }

        var nodeProjects = TryDetect(projectPath, "package.json", p => new DirectoryInfo(Path.GetDirectoryName(p)!).Name, excludeRoot: true);
        if (nodeProjects != null)
        {
            logger.Info("Detected ecosystem: Node.js (found package.json in subfolders)");
            return (nodeProjects, "node");
        }

        var pythonProjects = TryDetect(projectPath, "pyproject.toml", p => new DirectoryInfo(Path.GetDirectoryName(p)!).Name);
        if (pythonProjects != null)
        {
            logger.Info("Detected ecosystem: Python (found pyproject.toml)");
            return (pythonProjects, "python");
        }

        var goProjects = TryDetect(projectPath, "go.mod", p => new DirectoryInfo(Path.GetDirectoryName(p)!).Name);
        if (goProjects != null)
        {
            logger.Info("Detected ecosystem: Go (found go.mod)");
            return (goProjects, "go");
        }

        var rustProjects = TryDetect(projectPath, "Cargo.toml", p => new DirectoryInfo(Path.GetDirectoryName(p)!).Name);
        if (rustProjects != null)
        {
            logger.Info("Detected ecosystem: Rust (found Cargo.toml)");
            return (rustProjects, "rust");
        }

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

        logger.Info("No specific ecosystem detected — grouping by top-level folders.");
        return (topLevelDirs, "generic");
    }
}
```

### Step 4.6 — `src/AIBridge.Core/Services/RequestService.cs`

```csharp
using System.Text;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class RequestService(IAIBridgeLogger logger, IInputProvider inputProvider, ProjectDetector projectDetector, InputService inputService)
{
    public async Task HandleAsync(XmlElement root, string projectPath, bool paste)
    {
        List<string> requestedFiles = [];
        foreach (XmlNode node in root.SelectNodes($"//{XmlTags.File}")!)
        {
            var p = node.Attributes?["path"]?.Value.Trim();
            if (!string.IsNullOrEmpty(p)) requestedFiles.Add(p.Replace('\\', '/'));
        }

        if (requestedFiles.Count == 0)
        {
            logger.Warning("No valid <file path=\"...\"> tags found in <ai-request>.");
            return;
        }

        var rootFolderName = new DirectoryInfo(projectPath).Name;
        var (projects, _) = projectDetector.DetectProjects(projectPath);

        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
        var aiIgnorePath = Path.Combine(projectPath, FileNames.AiIgnore);
        var (aiIgnoreFolders, aiIgnoreFiles) = FileFilterHelper.LoadAiIgnoreRules(aiIgnorePath);

        var moduleToFiles = new Dictionary<string, List<(string relativePath, string content)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var relPath in requestedFiles)
        {
            string moduleName = rootFolderName;
            var absPath = WorkspaceHelper.SafeResolvePath(projectPath, relPath);

            foreach (var proj in projects)
            {
                if (absPath.StartsWith(proj.DirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    moduleName = proj.Name;
                    break;
                }
            }

            if (!moduleToFiles.TryGetValue(moduleName, out var moduleList))
            {
                moduleList = [];
                moduleToFiles[moduleName] = moduleList;
            }

            string fileContent;
            if (FileFilterHelper.IsAiIgnored(relPath, Path.GetFileName(absPath), aiIgnoreFolders, aiIgnoreFiles))
            {
                fileContent = "// ACCESS DENIED: File is excluded by .aiignore rules.";
                logger.Warning($"Blocked AI request for ignored file: {relPath}");
            }
            else if (File.Exists(absPath))
            {
                fileContent = (await File.ReadAllTextAsync(absPath)).TrimEnd();
            }
            else
            {
                fileContent = "// File not found on disk";
            }

            moduleList.Add((relPath, fileContent));
        }

        var sb = new StringBuilder();
        foreach (var module in moduleToFiles)
        {
            sb.AppendLine($"<module name=\"{module.Key}\" files=\"{module.Value.Count}\">");
            foreach (var file in module.Value)
            {
                var lines = file.content.Count(c => c == '\n') + 1;
                sb.AppendLine($"<file path=\"{file.relativePath}\" lines=\"{lines}\">");
                sb.AppendLine(file.content);
                sb.AppendLine("</file>");
            }
            sb.AppendLine("</module>");
            sb.AppendLine();
        }

        var resultText = sb.ToString().TrimEnd();

        var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
        if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

        var outputFile = Path.Combine(artifactsDir, FileNames.RequestedContext);
        await File.WriteAllTextAsync(outputFile, resultText, Encoding.UTF8);

        logger.Success($"\nSuccess! Generated requested context for {requestedFiles.Count} files.");
        logger.Info($"File saved to: {outputFile}");

        try
        {
            await inputProvider.SetClipboardTextAsync(resultText);
            logger.Info("The requested context has also been copied to your clipboard!");
        }
        catch { /* Suppress clipboard errors */ }

        var inputFile = Path.Combine(artifactsDir, FileNames.ResponseXml);
        await inputService.ResetInputFileAsync(inputFile);
    }
}
```

### Step 4.7 — `src/AIBridge.Core/Services/TemplateService.cs`

```csharp
using AIBridge.Core.Abstractions;

namespace AIBridge.Core.Services;

public class TemplateService(IAIBridgeLogger logger)
{
    public void ExtractTemplates(string targetDir, bool force, string projectPath)
    {
        var assembly = typeof(TemplateService).Assembly;
        var prefix = "AIBridge.Core.Templates.";
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(prefix))
            .ToList();

        var relativeTargetDir = Path.GetRelativePath(projectPath, targetDir).Replace('\\', '/');

        foreach (var resourceName in resourceNames)
        {
            var relativePart = resourceName[prefix.Length..];
            var relPath = ConvertResourceNameToPath(relativePart);
            var destFile = Path.Combine(targetDir, relPath);

            if (!File.Exists(destFile) || force)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                using var stream = assembly.GetManifestResourceStream(resourceName)!;
                using var fileStream = File.Create(destFile);
                stream.CopyTo(fileStream);

                logger.Success($"✅ Extracted {relativeTargetDir}/{relPath}");
            }
            else
            {
                logger.Info($"ℹ Skipped {relativeTargetDir}/{relPath} (already exists, use 'ai-bridge update' to overwrite)");
            }
        }
    }

    /// <summary>
    /// Converts embedded resource name segments back to file path.
    /// The last two dot-segments form the filename (e.g. "ai-response-skill" + "md").
    /// Everything before is directory segments.
    /// </summary>
    private static string ConvertResourceNameToPath(string resourceName)
    {
        var parts = resourceName.Split('.');
        if (parts.Length < 2) return resourceName;

        var ext = parts[^1];
        var fileNameBase = parts[^2];
        var fileName = $"{fileNameBase}.{ext}";
        var dirParts = parts[..^2];
        var dirPath = Path.Combine(dirParts);

        return Path.Combine(dirPath, fileName);
    }
}
```

### Step 4.8 — `src/AIBridge.Core/Services/IndexStatusService.cs`

```csharp
using System.Diagnostics;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class IndexStatusService(IAIBridgeLogger logger)
{
    public void Display(string projectRoot)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var indexFileName = WorkspaceHelper.GetIndexFileName(projectRoot);
        var indexFile = Path.Combine(aiWorkspace, indexFileName);

        if (!File.Exists(indexFile))
        {
            logger.Error($"Error: {indexFileName} not found. Run 'ai-bridge init' and create your index first.");
            return;
        }

        var xml = new XmlDocument();
        try { xml.Load(indexFile); }
        catch (Exception ex)
        {
            logger.Error($"Error parsing ai-bridge-index.xml: {ex.Message}");
            return;
        }

        var indexRoot = xml.DocumentElement;
        if (indexRoot == null) { logger.Error("Error: ai-bridge-index.xml is malformed."); return; }

        var lastUpdated = indexRoot.GetAttribute("lastUpdated");
        if (string.IsNullOrEmpty(lastUpdated)) lastUpdated = "unknown";

        logger.Info($"📋 ai-bridge-index.xml  (Last updated: {lastUpdated})");

        int moduleCount = 0, totalFileCount = 0;
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

                logger.Info($"\nModule: {moduleName} ({fileCount} files)");

                if (files != null)
                {
                    foreach (XmlElement file in files)
                    {
                        var path = file.GetAttribute("path");
                        var purpose = file.GetAttribute("purpose");
                        logger.Output($"  • {path}  — {purpose}");
                    }
                }
            }
        }

        logger.Info($"\nTotal: {moduleCount} module(s), {totalFileCount} file(s)");
    }

    public async Task<(List<string> modified, List<string> newFiles, List<string> deleted, DateTime lastUpdated)> GetChangedFilesAsync(string projectRoot)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var indexFileName = WorkspaceHelper.GetIndexFileName(projectRoot);
        var indexFile = Path.Combine(aiWorkspace, indexFileName);

        List<string> modifiedFiles = [], newFiles = [], deletedFiles = [];

        if (!File.Exists(indexFile))
            throw new Exception($"Error: {indexFileName} not found. Run 'ai-bridge init' and create your index first.");

        var xml = new XmlDocument();
        try { xml.Load(indexFile); }
        catch (Exception ex) { throw new Exception($"Error parsing {indexFileName}: {ex.Message}"); }

        var indexRoot = xml.DocumentElement;
        if (indexRoot == null) throw new Exception($"Error: {indexFileName} is malformed.");

        var lastUpdatedStr = indexRoot.GetAttribute("lastUpdated");
        if (string.IsNullOrEmpty(lastUpdatedStr) || !DateTime.TryParse(lastUpdatedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime lastUpdated))
            throw new Exception($"Warning: No 'lastUpdated' attribute found on {indexFileName}. Cannot determine status.");
        lastUpdated = lastUpdated.ToUniversalTime();

        var indexedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNodes = indexRoot.SelectNodes("//file[@path]");
        if (fileNodes != null)
        {
            foreach (XmlElement fileNode in fileNodes)
            {
                var path = fileNode.GetAttribute("path");
                if (!string.IsNullOrEmpty(path)) indexedPaths.Add(path);
            }
        }

        foreach (var relativePath in indexedPaths)
        {
            var absolutePath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(absolutePath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(absolutePath);
                if (lastWrite > lastUpdated) modifiedFiles.Add(relativePath);
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
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    var gitFiles = output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var gitFile in gitFiles)
                    {
                        var relativePath = gitFile.Replace('\\', '/');
                        if (relativePath.StartsWith("ai-bridge-", StringComparison.OrdinalIgnoreCase)) continue;
                        var fileName = Path.GetFileName(relativePath);
                        var ext = Path.GetExtension(relativePath);
                        if (FileFilterHelper.BinaryExtensions.Contains(ext)) continue;
                        if (FileFilterHelper.ExcludeFileNames.Contains(fileName)) continue;
                        if (FileFilterHelper.IsAiIgnored(relativePath, fileName, aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns)) continue;
                        if (!indexedPaths.Contains(relativePath)) newFiles.Add(relativePath);
                    }
                }
            }
        }
        catch (Exception ex) { logger.Warning($"Warning: Could not run git. Skipping new file detection. ({ex.Message})"); }

        return (modifiedFiles, newFiles, deletedFiles, lastUpdated);
    }

    public async Task StatusAsync(string projectRoot)
    {
        var indexFileName = WorkspaceHelper.GetIndexFileName(projectRoot);

        List<string> modifiedFiles, newFilesList, deletedFiles;
        DateTime lastUpdated;

        try { (modifiedFiles, newFilesList, deletedFiles, lastUpdated) = await GetChangedFilesAsync(projectRoot); }
        catch (Exception ex) { logger.Error(ex.Message); return; }

        logger.Info($"📋 {indexFileName}  (Last updated: {lastUpdated:yyyy-MM-dd HH:mm:ss UTC})");

        if (modifiedFiles.Count == 0 && newFilesList.Count == 0 && deletedFiles.Count == 0)
        {
            logger.Success("✅ Index is up to date. No changes detected.");
            return;
        }

        if (modifiedFiles.Count > 0)
        {
            logger.Warning($"⚠ {modifiedFiles.Count} file(s) modified since last index update:");
            foreach (var path in modifiedFiles)
            {
                var absolutePath = Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar));
                var modified = File.GetLastWriteTimeUtc(absolutePath);
                logger.Output($"  • {path}  (modified {modified:yyyy-MM-dd HH:mm:ss UTC})");
            }
        }

        if (newFilesList.Count > 0)
        {
            logger.Warning($"➕ {newFilesList.Count} new file(s) not in index:");
            foreach (var path in newFilesList) logger.Output($"  • {path}");
        }

        if (deletedFiles.Count > 0)
        {
            logger.Warning($"🗑️ {deletedFiles.Count} file(s) in index no longer exist on disk:");
            foreach (var path in deletedFiles) logger.Output($"  • {path}  (deleted)");
        }

        int totalChanges = modifiedFiles.Count + newFilesList.Count + deletedFiles.Count;
        logger.Info($"\nSummary: {modifiedFiles.Count} modified, {newFilesList.Count} new, {deletedFiles.Count} deleted ({totalChanges} total change(s))");
    }
}
```

### Step 4.9 — `src/AIBridge.Core/Services/PackerService.cs`

```csharp
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;
using AIBridge.Core.Models;

namespace AIBridge.Core.Services;

public class PackerService(IAIBridgeLogger logger, ProjectDetector projectDetector, StateService stateService)
{
    private static readonly string[] AlwaysExcludePrefixes = [$"{FolderNames.AiBridge}-"];

    private static readonly List<string> FallbackExcludeFolders =
    [
        @"[\\/]\.git[\\/]", @"[\\/]\.vs[\\/]", @"[\\/]\.idea[\\/]", @"[\\/]\.vscode[\\/]",
        @"[\\/]bin[\\/]", @"[\\/]obj[\\/]", @"[\\/]node_modules[\\/]",
        @"[\\/]dist[\\/]", @"[\\/]out[\\/]", @"[\\/]build[\\/]",
        @"[\\/]packages[\\/]", @"[\\/]TestResults[\\/]",
        @"[\\/]ai-bridge-[^\\/]+[\\/]",
        @"[\\/]__pycache__[\\/]", @"[\\/]\.mypy_cache[\\/]",
        @"[\\/]target[\\/]", @"[\\/]vendor[\\/]"
    ];

    private static readonly List<string> FallbackExcludeFilePatterns =
    [
        @"\.g\.cs$", @"\.g\.i\.cs$", @"\.designer\.cs$", @"AssemblyInfo\.cs$",
        @"\.user$", @"\.suo$", @"\.log$", @"\.tmp$"
    ];

    private static async Task<List<string>?> GetGitTrackedFiles(string projectPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "ls-files --cached --others --exclude-standard",
                WorkingDirectory = projectPath,
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) return null;

            return output
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(f => Path.GetFullPath(Path.Combine(projectPath, f)))
                .ToList();
        }
        catch { return null; }
    }

    public async Task<PackResult> PackAsync(string projectRoot, PackOptions options)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
        var aiIgnorePath = Path.Combine(projectRoot, FileNames.AiIgnore);

        if (!Directory.Exists(artifactsDir) || !Directory.Exists(Path.Combine(aiWorkspace, FolderNames.SimpleMode)))
            return new PackResult(false, ErrorMessage: "Project not initialized for AI Bridge. Please run 'ai-bridge init' first.");

        var rootFolderName = new DirectoryInfo(projectRoot).Name;
        var (detectedProjects, ecosystem) = projectDetector.DetectProjects(projectRoot);
        var warnings = new List<string>();

        HashSet<string>? incrementalFiles = null;
        if (options.Incremental)
        {
            try
            {
                var idxStatusSvc = new IndexStatusService(logger);
                var (modified, newFiles, _, _) = await idxStatusSvc.GetChangedFilesAsync(projectRoot);
                incrementalFiles = new HashSet<string>(modified.Concat(newFiles), StringComparer.OrdinalIgnoreCase);

                if (incrementalFiles.Count == 0)
                {
                    logger.Success("✅ No files changed since last index update. Nothing to pack.");
                    return new PackResult(true);
                }
                logger.Info($"Found {incrementalFiles.Count} modified/new file(s) to pack incrementally.");
            }
            catch (Exception ex) { return new PackResult(false, ErrorMessage: ex.Message); }
        }

        var gitFiles = await GetGitTrackedFiles(projectRoot);
        string[] allFiles;

        if (gitFiles != null)
        {
            logger.Info("Using git to determine file list (respects .gitignore)...");
            allFiles = gitFiles.ToArray();
        }
        else
        {
            logger.Warning("⚠ Git not available — using built-in exclusion rules...");
            allFiles = Directory.GetFiles(projectRoot, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var paddedPath = "/" + Path.GetRelativePath(projectRoot, f).Replace("\\", "/") + "/";
                    return !FallbackExcludeFolders.Any(pattern => Regex.IsMatch(paddedPath, pattern, RegexOptions.IgnoreCase));
                })
                .Where(f =>
                {
                    var fileName = Path.GetFileName(f);
                    return !FallbackExcludeFilePatterns.Any(pattern => Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase));
                })
                .ToArray();
        }

        var (aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns) = FileFilterHelper.LoadAiIgnoreRules(aiIgnorePath);

        var outputData = new Dictionary<string, StringBuilder>();
        var outputFileCounts = new Dictionary<string, int>();
        int totalFileCount = 0;
        long totalSizeBytes = 0;

        foreach (var file in allFiles.OrderBy(f => f))
        {
            var relativePath = Path.GetRelativePath(projectRoot, file).Replace("\\", "/");
            var fileName = Path.GetFileName(file);
            var extension = Path.GetExtension(file);

            if (AlwaysExcludePrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (FileFilterHelper.BinaryExtensions.Contains(extension)) continue;
            if (FileFilterHelper.ExcludeFileNames.Contains(fileName)) continue;
            if (FileFilterHelper.IsAiIgnored(relativePath, fileName, aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns)) continue;
            if (options.Incremental && incrementalFiles != null && !incrementalFiles.Contains(relativePath)) continue;

            string projectName = rootFolderName;
            foreach (var proj in detectedProjects)
            {
                if (file.StartsWith(proj.DirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    projectName = proj.Name;
                    break;
                }
            }

            try
            {
                var content = (await File.ReadAllTextAsync(file)).TrimEnd();
                var lineCount = content.AsSpan().Count('\n') + 1;
                var block = $"<file path=\"{relativePath}\" lines=\"{lineCount}\">\n{content}\n</file>\n";

                if (!outputData.TryGetValue(projectName, out var sb))
                {
                    sb = new StringBuilder();
                    outputData[projectName] = sb;
                    outputFileCounts[projectName] = 0;
                }

                sb.Append(block);
                outputFileCounts[projectName]++;
                totalFileCount++;
                logger.Info($"  Packed: {relativePath}");
            }
            catch (Exception ex)
            {
                warnings.Add($"{relativePath} ({ex.Message})");
                logger.Warning($"⚠ Skipped: {relativePath} ({ex.Message})");
            }
        }

        if (options.Incremental)
        {
            var sb = new StringBuilder();
            foreach (var key in outputData.Keys)
            {
                sb.AppendLine($"<{XmlTags.Module} name=\"{key}\" files=\"{outputFileCounts[key]}\">");
                sb.AppendLine(outputData[key].ToString());
                sb.AppendLine($"</{XmlTags.Module}>");
            }
            var outPath = Path.Combine(artifactsDir, FileNames.IncrementalContext);
            await File.WriteAllTextAsync(outPath, sb.ToString(), Encoding.UTF8);
            totalSizeBytes = new FileInfo(outPath).Length;
            var fileSizeKB = Math.Round(totalSizeBytes / 1024.0, 1);
            var approxTokens = sb.Length / 4;
            logger.Success($"SUCCESS: Incremental context packed ({totalFileCount} files, {fileSizeKB} KB, ~{approxTokens:N0} tokens) into ai-incremental-context.txt");
        }
        else
        {
            foreach (var key in outputData.Keys)
            {
                var outName = key == rootFolderName ? $"{key}-root-context.txt" : $"{key}-context.txt";
                var outPath = Path.Combine(artifactsDir, outName);
                var finalContent = $"<{XmlTags.Module} name=\"{key}\" files=\"{outputFileCounts[key]}\">\n{outputData[key]}\n</{XmlTags.Module}>\n";
                await File.WriteAllTextAsync(outPath, finalContent, Encoding.UTF8);
                totalSizeBytes += new FileInfo(outPath).Length;
                var fileSizeKB = Math.Round(new FileInfo(outPath).Length / 1024.0, 1);
                var approxTokens = finalContent.Length / 4;
                logger.Success($"SUCCESS: {key} codebase packed ({outputFileCounts[key]} files, {fileSizeKB} KB, ~{approxTokens:N0} tokens) into {outName}");
            }
        }

        if (warnings.Count > 0)
            logger.Warning($"\nCompleted with {warnings.Count} warning(s) (see above).");

        stateService.UpdateEcosystem(ecosystem);
        stateService.UpdateLastPacked();

        return new PackResult(
            IsSuccess: true, FileCount: totalFileCount,
            TotalSizeBytes: totalSizeBytes, ApproxTokens: (int)(totalSizeBytes / 4),
            Warnings: warnings.Count > 0 ? warnings : null);
    }
}
```

### ✅ Checkpoint Phase 4

```bash
dotnet build src/AIBridge.Core/AIBridge.Core.csproj
```

---

## Phase 5: Create CLI Layer

### Step 5.1 — `src/AIBridge.Cli/ConsoleLogger.cs`

```csharp
using AIBridge.Core.Abstractions;

namespace AIBridge.Cli;

public class ConsoleLogger : IAIBridgeLogger
{
    public void Success(string message) => WriteToStderr(message, ConsoleColor.Green);
    public void Warning(string message) => WriteToStderr(message, ConsoleColor.Yellow);
    public void Error(string message) => WriteToStderr(message, ConsoleColor.Red);
    public void Info(string message) => WriteToStderr(message, ConsoleColor.Cyan);

    public void Output(string message)
    {
        Console.WriteLine(message);
    }

    private static void WriteToStderr(string message, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = previous;
    }
}
```

### Step 5.2 — `src/AIBridge.Cli/ConsoleInputProvider.cs`

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AIBridge.Core.Abstractions;

namespace AIBridge.Cli;

public class ConsoleInputProvider : IInputProvider
{
    private enum Platform { Windows, MacOS, Wsl2, LinuxWayland, LinuxX11, Unsupported }

    private static readonly Platform CurrentPlatform = DetectPlatform();

    private static Platform DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Platform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return Platform.MacOS;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop")) return Platform.Wsl2;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return Platform.LinuxWayland;
            return Platform.LinuxX11;
        }

        return Platform.Unsupported;
    }

    public Task<string?> GetClipboardTextAsync()
    {
        var (fileName, clipArgs) = CurrentPlatform switch
        {
            Platform.Windows => ("powershell.exe", "-NoProfile -Command Get-Clipboard"),
            Platform.MacOS => ("pbpaste", ""),
            Platform.Wsl2 => ("powershell.exe", "-NoProfile -Command Get-Clipboard"),
            Platform.LinuxWayland => ("wl-paste", "--no-newline"),
            Platform.LinuxX11 => ("xclip", "-selection clipboard -o"),
            _ => throw new PlatformNotSupportedException("Clipboard access is not supported on this platform.")
        };
        return Task.FromResult(RunProcess(fileName, clipArgs));
    }

    public Task<string?> ReadFromStdinAsync(string prompt)
    {
        Console.WriteLine();
        Console.WriteLine(prompt);
        Console.WriteLine();

        var sb = new StringBuilder();
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            sb.AppendLine(line);
            var trimmed = line.Trim();
            if (trimmed.Equals("</ai-response>", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("</ai-request>", StringComparison.OrdinalIgnoreCase))
                break;
        }
        var result = sb.ToString();
        return Task.FromResult<string?>(string.IsNullOrWhiteSpace(result) ? null : result);
    }

    public Task SetClipboardTextAsync(string text)
    {
        var (fileName, clipArgs) = CurrentPlatform switch
        {
            Platform.Windows => ("clip.exe", ""),
            Platform.MacOS => ("pbcopy", ""),
            Platform.Wsl2 => ("clip.exe", ""),
            Platform.LinuxWayland => ("wl-copy", ""),
            Platform.LinuxX11 => ("xclip", "-selection clipboard"),
            _ => throw new PlatformNotSupportedException("Clipboard access is not supported on this platform.")
        };
        WriteToProcess(fileName, clipArgs, text);
        return Task.CompletedTask;
    }

    private static string? RunProcess(string fileName, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName, Arguments = args,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new Exception($"Could not start '{fileName}'.");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            var error = proc.StandardError.ReadToEnd().Trim();
            throw new Exception($"Clipboard read failed via '{fileName}' (exit code {proc.ExitCode}): {error}");
        }
        return string.IsNullOrEmpty(output) ? null : output.TrimEnd('\r', '\n');
    }

    private static void WriteToProcess(string fileName, string args, string text)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName, Arguments = args,
            RedirectStandardInput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new Exception($"Could not start '{fileName}'.");
        proc.StandardInput.Write(text);
        proc.StandardInput.Close();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            var error = proc.StandardError.ReadToEnd().Trim();
            throw new Exception($"Clipboard write failed via '{fileName}' (exit code {proc.ExitCode}): {error}");
        }
    }
}
```

### Step 5.3 — `src/AIBridge.Cli/Helpers/WorkspaceHelper.cs`

```csharp
using AIBridge.Core.Constants;

namespace AIBridge.Cli.Helpers;

public static class WorkspaceHelper
{
    public static string GetProjectRoot()
    {
        var currentDir = new DirectoryInfo(Environment.CurrentDirectory);
        while (currentDir != null)
        {
            if (Directory.Exists(Path.Combine(currentDir.FullName, ".git")) ||
                File.Exists(Path.Combine(currentDir.FullName, FileNames.AiIgnore)))
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }
        return Environment.CurrentDirectory;
    }
}
```

### Step 5.4 — `src/AIBridge.Cli/Program.cs`

```csharp
using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AIBridge.Cli;
using AIBridge.Cli.Helpers;
using AIBridge.Core.Constants;
using AIBridge.Core.Models;
using AIBridge.Core.Services;

var logger = new ConsoleLogger();
var inputProvider = new ConsoleInputProvider();
var projectRoot = WorkspaceHelper.GetProjectRoot();
var stateService = new StateService(projectRoot, logger);
var projectDetector = new ProjectDetector(logger);
var inputService = new InputService(logger, inputProvider);
var patcherService = new PatcherService(logger);
var indexService = new IndexService(logger);
var requestService = new RequestService(logger, inputProvider, projectDetector, inputService);
var templateService = new TemplateService(logger);
var packerService = new PackerService(logger, projectDetector, stateService);
var indexStatusService = new IndexStatusService(logger);

var rootCommand = new RootCommand("AI Bridge - Connects your local codebase to AI chatbots.");

// ── Pack ──
var packCommand = new Command("pack", "Packs source files into text context for AI.");
var incrementalOption = new Option<bool>("--incremental", "Pack only files modified or added since the last index update.");
packCommand.AddOption(incrementalOption);
packCommand.SetHandler(async (bool incremental) =>
{
    if (!stateService.EnsureUpToDate()) { Environment.ExitCode = 1; return; }
    logger.Info(incremental ? "Packing incremental AI context..." : "Packing full AI context...");
    var result = await packerService.PackAsync(projectRoot, new PackOptions(Incremental: incremental));
    if (!result.IsSuccess) { logger.Error(result.ErrorMessage ?? "Pack failed."); Environment.ExitCode = 1; }
}, incrementalOption);

// ── Apply ──
var applyCommand = new Command("apply", "Applies ai-response.xml patches to the codebase.");
var watchOption = new Option<bool>("--watch", "Keep running and auto-apply when ai-response.xml is saved.");
var pasteOption = new Option<bool>("--paste", "Read directly from clipboard.");
var dryRunOption = new Option<bool>("--dry-run", "Show what would change without applying.");
applyCommand.AddOption(watchOption);
applyCommand.AddOption(pasteOption);
applyCommand.AddOption(dryRunOption);
applyCommand.SetHandler(async (bool watch, bool paste, bool dryRun) =>
{
    if (!stateService.EnsureUpToDate()) { Environment.ExitCode = 1; return; }
    logger.Info("Applying AI code changes...");

    if (watch)
    {
        if (paste) { logger.Warning("Ignoring --watch flag because --paste was used."); await RunApplyAsync(paste, dryRun); return; }

        logger.Info("Starting watch mode for ai-response.xml...");
        await RunApplyAsync(paste, dryRun);

        var aiWorkspace = AIBridge.Core.Helpers.WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var watchDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
        if (!Directory.Exists(watchDir)) Directory.CreateDirectory(watchDir);

        using var watcher = new FileSystemWatcher(watchDir)
        {
            Filter = FileNames.ResponseXml,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        DateTime lastRun = DateTime.MinValue;
        async void OnChanged(object s, FileSystemEventArgs e)
        {
            if ((DateTime.Now - lastRun).TotalMilliseconds < Timings.WatchDebounceMs) return;
            lastRun = DateTime.Now;
            await Task.Delay(Timings.FileLockWaitMs);
            Console.WriteLine();
            logger.Info("Change detected in ai-response.xml. Applying...");
            await RunApplyAsync(paste, dryRun);
            logger.Info("\nWaiting for next change... (Press Ctrl+C to exit)");
        }

        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        logger.Info("\nWaiting for next change... (Press Ctrl+C to exit)");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
        try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (TaskCanceledException) { }
    }
    else
    {
        await RunApplyAsync(paste, dryRun);
    }
}, watchOption, pasteOption, dryRunOption);

// ── Init ──
var initCommand = new Command("init", $"Scaffolds {FileNames.AiIgnore}, {FolderNames.SimpleMode}/, and {FolderNames.AdvancedMode}/ for a new project.");
initCommand.SetHandler(async () =>
{
    logger.Info("Initializing AI Bridge for this project...");
    await RunInitAsync(force: false);
});

// ── Update ──
var updateCommand = new Command("update", $"Syncs {FolderNames.SimpleMode}/ and {FolderNames.AdvancedMode}/ to match the currently installed tool version.");
updateCommand.SetHandler(async () =>
{
    logger.Info("Updating AI Bridge default templates...");
    await RunInitAsync(force: true);
});

// ── Index ──
var indexCommand = new Command("index", "Displays the contents of the index XML file.");
var statusCommand = new Command("status", "Shows files changed since the last index update.");
indexCommand.AddCommand(statusCommand);
indexCommand.SetHandler(() => { indexStatusService.Display(projectRoot); });
statusCommand.SetHandler(async () => { await indexStatusService.StatusAsync(projectRoot); });

rootCommand.AddCommand(packCommand);
rootCommand.AddCommand(applyCommand);
rootCommand.AddCommand(initCommand);
rootCommand.AddCommand(updateCommand);
rootCommand.AddCommand(indexCommand);

try { return await rootCommand.InvokeAsync(args); }
catch (Exception ex) { logger.Error($"Fatal error: {ex.Message}"); return 2; }

// ═══════════════════════════════════════════════════════════
// Local functions
// ═══════════════════════════════════════════════════════════

async Task RunInitAsync(bool force)
{
    var aiWorkspace = AIBridge.Core.Helpers.WorkspaceHelper.GetAiWorkspacePath(projectRoot);
    var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
    if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

    var responseFilePath = Path.Combine(artifactsDir, FileNames.ResponseXml);
    if (!File.Exists(responseFilePath))
        await File.WriteAllTextAsync(responseFilePath, "<!-- Paste the AI response XML here -->\n");

    var gitignorePath = Path.Combine(projectRoot, ".gitignore");
    if (File.Exists(gitignorePath))
    {
        var content = await File.ReadAllTextAsync(gitignorePath);
        bool changed = false;
        if (!content.Contains($"{FolderNames.AiBridge}/{FolderNames.Artifacts}/"))
        { await File.AppendAllTextAsync(gitignorePath, $"\n# AI Bridge\n{FolderNames.AiBridge}/{FolderNames.Artifacts}/\n"); changed = true; }
        if (!content.Contains($"{FolderNames.AiBridge}/{FolderNames.SimpleMode}/"))
        { await File.AppendAllTextAsync(gitignorePath, $"{FolderNames.AiBridge}/{FolderNames.SimpleMode}/\n"); changed = true; }
        if (!content.Contains($"{FolderNames.AiBridge}/{FolderNames.AdvancedMode}/"))
        { await File.AppendAllTextAsync(gitignorePath, $"{FolderNames.AiBridge}/{FolderNames.AdvancedMode}/\n"); changed = true; }
        if (changed) logger.Success("✅ Patched .gitignore to ignore AI Bridge workspace contents.");
    }

    var dockerignorePath = Path.Combine(projectRoot, ".dockerignore");
    if (File.Exists(dockerignorePath))
    {
        var content = await File.ReadAllTextAsync(dockerignorePath);
        if (!content.Contains($"{FolderNames.AiBridge}/"))
        {
            await File.AppendAllTextAsync(dockerignorePath, $"\n# AI Bridge\n{FolderNames.AiBridge}/\n");
            logger.Success("✅ Patched .dockerignore to exclude AI Bridge workspace from Docker builds.");
        }
    }

    var aiIgnorePath = Path.Combine(projectRoot, FileNames.AiIgnore);
    if (!File.Exists(aiIgnorePath))
    {
        var defaultIgnore = $"# Additional ignore rules for AI Bridge packing (works alongside .gitignore)\n# Folders should end with /\n{FolderNames.AiBridge}/\nTestResults/\n*.g.cs\n*.log\n*.tmp\n";
        await File.WriteAllTextAsync(aiIgnorePath, defaultIgnore);
        logger.Success("✅ Created default .aiignore file.");
    }
    else { logger.Info("ℹ .aiignore already exists."); }

    var simpleModeDir = Path.Combine(aiWorkspace, FolderNames.SimpleMode);
    var advancedModeDir = Path.Combine(aiWorkspace, FolderNames.AdvancedMode);
    if (force)
    {
        if (Directory.Exists(simpleModeDir)) Directory.Delete(simpleModeDir, true);
        if (Directory.Exists(advancedModeDir)) Directory.Delete(advancedModeDir, true);
    }

    templateService.ExtractTemplates(aiWorkspace, force, projectRoot);
    stateService.InitState();
}

async Task RunApplyAsync(bool paste, bool dryRun)
{
    var aiWorkspace = AIBridge.Core.Helpers.WorkspaceHelper.GetAiWorkspacePath(projectRoot);
    var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
    var inputFile = Path.Combine(artifactsDir, FileNames.ResponseXml);
    var failedLogFile = Path.Combine(artifactsDir, FileNames.FailedPatches);

    if (!await inputService.ResolveAsync(inputFile, paste)) return;

    var rawContent = await File.ReadAllTextAsync(inputFile);
    if (File.Exists(failedLogFile)) File.Delete(failedLogFile);

    rawContent = Regex.Replace(rawContent, @"(?m)^```[a-zA-Z]*\s*$", "");
    rawContent = Regex.Replace(rawContent, @"(?m)^```\s*$", "");

    var xml = new XmlDocument();
    try { xml.LoadXml(rawContent); }
    catch (Exception ex)
    {
        logger.Error($"Error: Provided xml content is not valid XML. {ex.Message}");
        logger.Error("The entire transaction was aborted. No partial changes were applied.");
        return;
    }

    var root = xml.DocumentElement;
    if (root == null) { logger.Error("Error: No XML content found."); return; }
    if (root.Name is not (XmlTags.AiResponse or XmlTags.AiRequest or XmlTags.CreateIndex or XmlTags.UpdateIndex))
    {
        logger.Error($"Error: Root element must be <{XmlTags.AiResponse}>, <{XmlTags.AiRequest}>, <{XmlTags.CreateIndex}>, or <{XmlTags.UpdateIndex}>, found <{root.Name}>.");
        return;
    }

    if (root.Name == XmlTags.AiRequest) { await requestService.HandleAsync((XmlElement)root, projectRoot, paste); return; }
    if (root.Name == XmlTags.CreateIndex) { indexService.HandleCreate(root, projectRoot); await inputService.ResetInputFileAsync(inputFile); return; }
    if (root.Name == XmlTags.UpdateIndex) { indexService.HandleUpdate(root, projectRoot); await inputService.ResetInputFileAsync(inputFile); return; }

    var aiEditsNode = root.SelectSingleNode(XmlTags.AiEdits);
    var indexUpdateNode = root.SelectSingleNode(XmlTags.UpdateIndex);

    if (aiEditsNode != null)
    {
        var indexFileName = AIBridge.Core.Helpers.WorkspaceHelper.GetIndexFileName(projectRoot);
        var idxFile = Path.Combine(aiWorkspace, indexFileName);
        bool isAdvancedMode = File.Exists(idxFile) || indexUpdateNode != null;

        if (isAdvancedMode && indexUpdateNode == null)
        {
            logger.Error("Error: AI provided <ai-edits> but completely forgot to provide an <update-ai-bridge-index> block.");
            logger.Info("Please ask the AI to regenerate the response and include the mandatory index update block.");
            return;
        }

        var hasDeletes = aiEditsNode.SelectNodes(XmlTags.Delete)?.Count > 0;
        bool actualCreates = false;
        var fileNodes = aiEditsNode.SelectNodes(XmlTags.File);
        if (fileNodes != null)
        {
            foreach (XmlNode fileNode in fileNodes)
            {
                var relPath = fileNode.Attributes?["path"]?.Value?.Trim();
                if (!string.IsNullOrEmpty(relPath))
                {
                    var absPath = AIBridge.Core.Helpers.WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
                    if (!File.Exists(absPath)) { actualCreates = true; break; }
                }
            }
        }

        if (isAdvancedMode && (actualCreates || hasDeletes))
        {
            var hasIndexChanges = indexUpdateNode?.SelectNodes(".//file | .//delete")?.Count > 0;
            if (hasIndexChanges != true)
            {
                logger.Error("Error: AI created or deleted files in <ai-edits>, but sent an empty <update-ai-bridge-index> block.");
                logger.Info("The index must be structurally updated when files are added or removed.");
                return;
            }
        }
    }

    foreach (XmlNode node in root.ChildNodes)
    {
        if (node.NodeType == XmlNodeType.Element && node.Name != XmlTags.AiEdits && node.Name != XmlTags.UpdateIndex)
        {
            logger.Error($"Error: Unknown element '<{node.Name}>' found. Only <{XmlTags.AiEdits}> and <{XmlTags.UpdateIndex}> are allowed.");
            return;
        }
    }

    int countFullFiles = 0, countPatchOk = 0, countPatchFailed = 0, countDeleted = 0;
    var failedFiles = new List<string>();
    var failedPatchNodes = new List<XmlNode>();

    foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.File}")!)
    {
        var relPath = node.Attributes?["path"]?.Value?.Trim();
        if (string.IsNullOrEmpty(relPath)) { logger.Error("File creation failed: missing 'path' attribute."); continue; }
        var absPath = AIBridge.Core.Helpers.WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
        if (dryRun) { logger.Info($"[dry-run] Would create/overwrite: {relPath}"); countFullFiles++; continue; }
        Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
        var newContent = node.InnerText.TrimEnd('\r', '\n') + Environment.NewLine;
        await File.WriteAllTextAsync(absPath, newContent, Encoding.UTF8);
        logger.Success($"Created/Overwritten: {relPath}");
        countFullFiles++;
    }

    foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.Patch}")!)
    {
        if (dryRun) { logger.Info($"[dry-run] Would patch: {node.Attributes?["path"]?.Value?.Trim()}"); countPatchOk++; continue; }
        if (await patcherService.ApplyPatchAsync(node, projectRoot, failedFiles, failedPatchNodes)) countPatchOk++;
        else countPatchFailed++;
    }

    var deletedFileDirs = new HashSet<string>();
    foreach (XmlNode node in root.SelectNodes($"{XmlTags.AiEdits}/{XmlTags.Delete}")!)
    {
        var relPath = node.Attributes?["path"]?.Value?.Trim();
        if (string.IsNullOrEmpty(relPath)) { logger.Error("Delete failed: missing 'path' attribute."); continue; }
        var absPath = AIBridge.Core.Helpers.WorkspaceHelper.SafeResolvePath(projectRoot, relPath);
        if (File.Exists(absPath))
        {
            if (dryRun) { logger.Info($"[dry-run] Would delete: {relPath}"); countDeleted++; continue; }
            File.Delete(absPath);
            deletedFileDirs.Add(Path.GetDirectoryName(absPath)!);
            logger.Success($"Deleted: {relPath}");
            countDeleted++;
        }
    }

    if (countPatchFailed == 0 && indexUpdateNode is XmlElement indexUpdateElement)
        indexService.HandleUpdate(indexUpdateElement, projectRoot);

    if (countDeleted > 0 && !dryRun)
        CleanEmptyFolders(deletedFileDirs, projectRoot);

    logger.Info($"\nSummary: {countFullFiles} written, {countPatchOk} patched, {countDeleted} deleted.");

    if (countPatchFailed > 0)
    {
        logger.Error($"Failed patches: {countPatchFailed}. Check {failedLogFile}");
        await File.WriteAllLinesAsync(failedLogFile, failedFiles.Distinct());
        await PatcherService.RebuildResponseWithFailedPatchesAsync(inputFile, failedPatchNodes);
        logger.Warning($"⚠ ai-response.xml now contains only the {countPatchFailed} failed patch(es). Fix and re-run 'ai-bridge apply'.");
    }
    else
    {
        await inputService.ResetInputFileAsync(inputFile);
    }
}

void CleanEmptyFolders(IEnumerable<string> dirs, string rootPath)
{
    var dirsToCheck = new HashSet<string>(dirs);
    bool removedAny;
    do
    {
        removedAny = false;
        var currentDirs = dirsToCheck.ToList();
        dirsToCheck.Clear();
        foreach (var dir in currentDirs)
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                logger.Info($"Removed empty folder: {Path.GetRelativePath(rootPath, dir)}");
                removedAny = true;
                var parent = Directory.GetParent(dir)?.FullName;
                if (parent != null && parent.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) && parent != rootPath)
                    dirsToCheck.Add(parent);
            }
        }
    } while (removedAny);
}
```

### ✅ Checkpoint Phase 5

```bash
cd /home/arpanvgm/github/arpanvgm/ai-bridge
dotnet build ai-bridge.sln
```

---

## Phase 6: Validate & Clean Up

### Step 6.1 — Run all commands

```bash
cd /home/arpanvgm/github/arpanvgm/ai-bridge

# Build
dotnet build ai-bridge.sln

# Test init
dotnet run --project src/AIBridge.Cli -- init

# Test pack
dotnet run --project src/AIBridge.Cli -- pack

# Test dry-run
dotnet run --project src/AIBridge.Cli -- apply --dry-run

# Test index
dotnet run --project src/AIBridge.Cli -- index

# Test NuGet packaging
dotnet pack src/AIBridge.Cli/AIBridge.Cli.csproj
```

### Step 6.2 — Remove old project (only after ALL Step 6.1 commands pass)

```bash
# Only run after Phase 6 Step 6.1 passes completely
# rm -rf AIBridge/
```
