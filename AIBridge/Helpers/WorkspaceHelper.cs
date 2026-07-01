using System;
using System.IO;
using AIBridge.Constants;

namespace AIBridge.Helpers
{
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
    }
}
