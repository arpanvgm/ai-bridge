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
