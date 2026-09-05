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
                var idxStatusSvc = new IndexService(logger, projectDetector);
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

        var allFiles = await FileFilterHelper.GetTrackedFilesAsync(projectRoot, logger);

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

            if (FileFilterHelper.AlwaysExcludePrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
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


        return new PackResult(
            IsSuccess: true, FileCount: totalFileCount,
            TotalSizeBytes: totalSizeBytes, ApproxTokens: (int)(totalSizeBytes / 4),
            Warnings: warnings.Count > 0 ? warnings : null);
    }
}
