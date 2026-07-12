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
    private static readonly string[] AlwaysExcludePrefixes = [$"{FolderNames.AiBridge}/", $"{FolderNames.AiBridge}-"];

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


        return new PackResult(
            IsSuccess: true, FileCount: totalFileCount,
            TotalSizeBytes: totalSizeBytes, ApproxTokens: (int)(totalSizeBytes / 4),
            Warnings: warnings.Count > 0 ? warnings : null);
    }
}
