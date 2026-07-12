using AIBridge.Core.Constants;

namespace AIBridge.Cli.Helpers;

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
}
