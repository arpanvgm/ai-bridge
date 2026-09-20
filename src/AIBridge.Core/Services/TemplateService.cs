
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class TemplateService(IAIBridgeLogger logger)
{
    /// <summary>
    /// Extracts all embedded templates into the workspace, always overwriting existing files.
    /// Call this during init / migrate where the goal is a guaranteed up-to-date state.
    /// Template folders (SimpleMode, AdvancedMode, AutoIndexMode) are deleted and recreated
    /// so stale files from older versions cannot linger.
    /// </summary>
    public void ExtractAll(string targetDir, string projectPath)
    {
        DeleteTemplateFolders(targetDir);
        Extract(targetDir, projectPath, overwrite: true);
    }

    // ── Private ────────────────────────────────────────────────────

    private void Extract(string targetDir, string projectPath, bool overwrite)
    {
        var assembly = typeof(TemplateService).Assembly;
        const string prefix = "AIBridge.Core.Templates.";
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(prefix))
            .ToList();

        var relativeTargetDir = Path.GetRelativePath(projectPath, targetDir).Replace('\\', '/');

        foreach (var resourceName in resourceNames)
        {
            var relativePart = resourceName[prefix.Length..];
            var relPath = ConvertResourceNameToPath(relativePart);
            var destFile = Path.Combine(targetDir, relPath);

            if (!File.Exists(destFile) || overwrite)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                using var stream = assembly.GetManifestResourceStream(resourceName)!;
                using var fileStream = File.Create(destFile);
                stream.CopyTo(fileStream);

                logger.Success($"✅ Extracted {relativeTargetDir}/{relPath}");
            }
        }
    }

    private static void DeleteTemplateFolders(string targetDir)
    {
        var foldersToDelete = new[]
        {
            FolderNames.SimpleMode,
            FolderNames.AdvancedMode,
            FolderNames.AutoIndexMode
        };

        foreach (var folder in foldersToDelete)
        {
            var path = Path.Combine(targetDir, folder);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    /// Converts an embedded resource name back to a relative file path.
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

        // Fix .NET Embedded Resource name mangling for folders with numbers/hyphens.
        dirPath = dirPath.Replace("_1_SimpleMode", "1-SimpleMode")
                         .Replace("_2_AdvancedMode", "2-AdvancedMode")
                         .Replace("Phase1_CreateIndex", "Phase1-CreateIndex")
                         .Replace("Phase2_DailyChat", "Phase2-DailyChat");

        return Path.Combine(dirPath, fileName);
    }
}
