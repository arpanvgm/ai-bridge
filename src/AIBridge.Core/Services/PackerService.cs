using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;
using AIBridge.Core.Models;

namespace AIBridge.Core.Services;

public class PackerService(IAIBridgeLogger logger, ProjectDetector projectDetector)
{
    public async Task<PackResult> PackAsync(string projectRoot)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
        var aiIgnorePath = Path.Combine(projectRoot, FileNames.AiIgnore);

        var rootFolderName = new DirectoryInfo(projectRoot).Name;
        var (detectedProjects, ecosystem) = projectDetector.DetectProjects(projectRoot);
        var warnings = new List<string>();

        var allFiles = await FileFilterHelper.GetTrackedFilesAsync(projectRoot, logger);

        var (aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns, aiIgnoreRootFilePatterns) = FileFilterHelper.LoadAiIgnoreRules(aiIgnorePath);

        var outputData = new Dictionary<string, StringBuilder>();
        var outputFileCounts = new Dictionary<string, int>();
        int totalFileCount = 0;
        long totalSizeBytes = 0;

        foreach (var file in allFiles.OrderBy(f => f))
        {
            var relativePath = Path.GetRelativePath(projectRoot, file).Replace("\\", "/");
            var fileName = Path.GetFileName(file);
            var extension = Path.GetExtension(file);

            if (FileFilterHelper.AlwaysExcludePrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (FileFilterHelper.BinaryExtensions.Contains(extension)) continue;
            if (FileFilterHelper.ExcludeFileNames.Contains(fileName)) continue;
            if (FileFilterHelper.IsAiIgnored(relativePath, fileName, aiIgnoreExcludeFolders, aiIgnoreExcludeFilePatterns, aiIgnoreRootFilePatterns)) continue;

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

        if (warnings.Count > 0)
            logger.Warning($"\nCompleted with {warnings.Count} warning(s) (see above).");


        return new PackResult(
            IsSuccess: true, FileCount: totalFileCount,
            TotalSizeBytes: totalSizeBytes, ApproxTokens: (int)(totalSizeBytes / 4),
            Warnings: warnings.Count > 0 ? warnings : null);
    }
}
