
using System.Security.Cryptography;
using System.Text;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class TemplateService(IAIBridgeLogger logger)
{
    private const string ResourcePrefix = "AIBridge.Core.Templates.";

    /// <summary>
    /// Extracts all embedded templates into the workspace, always overwriting existing files.
    /// Call this during init / migrate where the goal is a guaranteed up-to-date state.
    /// Legacy folders (SimpleMode, AdvancedMode) are deleted from the user's machine during migration.
    /// AutoIndexMode is deleted and re-extracted cleanly so stale files cannot linger.
    /// </summary>
    public void ExtractAll(string targetDir, string projectPath)
    {
        DeleteTemplateFolders(targetDir);
        Extract(targetDir, projectPath, overwrite: true);
    }

    /// <summary>
    /// Lists every embedded template with the path it is extracted to,
    /// relative to the ai-bridge workspace folder. Memory only — no disk access.
    /// </summary>
    internal static List<(string ResourceName, string RelativePath)> GetEmbeddedTemplates()
    {
        var assembly = typeof(TemplateService).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(r => (ResourceName: r, RelativePath: ConvertResourceNameToPath(r[ResourcePrefix.Length..])))
            .ToList();
    }

    /// <summary>
    /// SHA-256 over the relative path and content of every embedded template.
    /// Reads only embedded (in-memory) resources, so its cost does not depend on the user's disk.
    /// Adding, removing, renaming or editing any template changes the result automatically.
    /// </summary>
    internal static string ComputeContentHash()
    {
        var assembly = typeof(TemplateService).Assembly;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[8192];

        foreach (var (resourceName, relativePath) in GetEmbeddedTemplates().OrderBy(t => t.RelativePath, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath.Replace('\\', '/') + "\n"));

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);

            hash.AppendData([0]);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // ── Private ────────────────────────────────────────────────────

    private void Extract(string targetDir, string projectPath, bool overwrite)
    {
        var assembly = typeof(TemplateService).Assembly;
        var relativeTargetDir = Path.GetRelativePath(projectPath, targetDir).Replace('\\', '/');

        foreach (var (resourceName, relPath) in GetEmbeddedTemplates())
        {
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

        return Path.Combine(dirPath, fileName);
    }
}
