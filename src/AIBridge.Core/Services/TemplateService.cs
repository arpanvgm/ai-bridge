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

        // Fix .NET Embedded Resource name mangling for folders with numbers/hyphens
        dirPath = dirPath.Replace("_1_SimpleMode", "1-SimpleMode")
                         .Replace("_2_AdvancedMode", "2-AdvancedMode")
                         .Replace("Phase1_CreateIndex", "Phase1-CreateIndex")
                         .Replace("Phase2_DailyChat", "Phase2-DailyChat");

        return Path.Combine(dirPath, fileName);
    }
}
