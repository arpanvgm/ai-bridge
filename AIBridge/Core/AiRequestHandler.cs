using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using AIBridge.Commands;
using AIBridge.Helpers;
using AIBridge.Constants;

namespace AIBridge.Core;

/// <summary>
/// Handles the &lt;ai-request&gt; flow: gathers requested file contents,
/// writes them as structured context, and optionally copies to clipboard.
/// </summary>
public static class AiRequestHandler
{
    public static async Task HandleAsync(XmlElement root, string projectPath, bool paste)
    {
        List<string> requestedFiles = [];
        foreach (XmlNode node in root.SelectNodes($"//{XmlTags.File}")!)
        {
            var p = node.Attributes?["path"]?.Value.Trim();
            if (!string.IsNullOrEmpty(p)) requestedFiles.Add(p.Replace('\\', '/'));
        }

        if (requestedFiles.Count == 0)
        {
            ConsoleHelper.Warning("No valid <file path=\"...\"> tags found in <ai-request>.");
            return;
        }

        var rootFolderName = new DirectoryInfo(projectPath).Name;
        var (projects, _) = PackCommand.DetectProjects(projectPath);

        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
        var aiIgnorePath = Path.Combine(projectPath, FileNames.AiIgnore);
        var (aiIgnoreFolders, aiIgnoreFiles) = FileFilterHelper.LoadAiIgnoreRules(aiIgnorePath);

        var moduleToFiles = new Dictionary<string, List<(string relativePath, string content)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var relPath in requestedFiles)
        {
            string moduleName = rootFolderName;
            var absPath = WorkspaceHelper.SafeResolvePath(projectPath, relPath);

            // Use the exact matching logic from Packer.cs
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
                ConsoleHelper.Warning($"Blocked AI request for ignored file: {relPath}");
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
            var totalFiles = module.Value.Count;
            sb.AppendLine($"<module name=\"{module.Key}\" files=\"{totalFiles}\">");

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

        ConsoleHelper.Success($"\nSuccess! Generated requested context for {requestedFiles.Count} files.");
        ConsoleHelper.Info($"File saved to: {outputFile}");

        // Try to copy to clipboard for convenience
        try
        {
            ClipboardHelper.SetText(resultText);
            ConsoleHelper.Info("The requested context has also been copied to your clipboard!");
        }
        catch
        {
            // Suppress clipboard errors in isolated environments (e.g. WSL without interop).
            // The context is already safely saved to disk.
        }

        var inputFile = Path.Combine(artifactsDir, FileNames.ResponseXml);
        await InputResolver.ResetInputFileAsync(inputFile);
    }
}
