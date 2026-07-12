using System.Text;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class RequestService(IAIBridgeLogger logger, ProjectDetector projectDetector)
{
    public async Task<string> HandleAsync(XmlElement root, string projectPath)
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
            return string.Empty;
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

        return resultText;
    }
}
