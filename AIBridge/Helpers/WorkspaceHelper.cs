using System;
using System.IO;
using AIBridge.Constants;

namespace AIBridge.Helpers;

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

    public static string GetAiWorkspacePath(string projectRoot)
    {
        return Path.Combine(projectRoot, FolderNames.AiBridge);
    }

    public static string GetIndexFileName(string projectRoot)
    {
        return FileNames.Index;
    }

    public static string SafeResolvePath(string projectRoot, string relativePath)
    {
        var resolved = Path.GetFullPath(Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var fullProjectRoot = Path.GetFullPath(projectRoot);
        
        // Ensure the path always ends with a separator for the prefix check
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
