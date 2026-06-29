using System;
using System.IO;

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
                    File.Exists(Path.Combine(currentDir.FullName, ".aiignore")))
                {
                    return currentDir.FullName;
                }
                currentDir = currentDir.Parent;
            }
            return Environment.CurrentDirectory;
        }

        public static string GetAiWorkspacePath(string projectRoot)
        {
            return Path.Combine(projectRoot, "ai-bridge");
        }

        public static string GetIndexFileName(string projectRoot)
        {
            return "index.xml";
        }
    }
}
