using System;
using System.IO;
using System.Reflection;

namespace AIBridge
{
    public static class VersionChecker
    {
        public static string GetCurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        public static bool EnsureUpToDate()
        {
            var projectPath = Environment.CurrentDirectory;
            var versionFile = Path.Combine(projectPath, "aiSkills", ".version");

            if (!File.Exists(versionFile))
            {
                // To be backward compatible or handle first run
                if (Directory.Exists(Path.Combine(projectPath, "aiSkills")))
                {
                    ConsoleHelper.Warning("Version mismatch! Your local aiSkills/ and aiPrompts/ are from an older version of AI Bridge.");
                    ConsoleHelper.Info("Please run 'ai-bridge update' to sync the templates with the latest tool implementation.");
                    return false;
                }
                
                ConsoleHelper.Warning("AI Bridge is not initialized in this project.");
                ConsoleHelper.Info("Please run 'ai-bridge init' first.");
                return false;
            }

            var localVersion = File.ReadAllText(versionFile).Trim();
            var currentVersion = GetCurrentVersion();

            if (localVersion != currentVersion)
            {
                ConsoleHelper.Warning($"Version mismatch! Tool version is {currentVersion}, but local templates are version {localVersion}.");
                ConsoleHelper.Info("Please run 'ai-bridge update' to sync the templates with the latest tool implementation.");
                ConsoleHelper.Info("Note: This will overwrite any custom changes in aiSkills/ and aiPrompts/ to ensure compatibility.");
                return false;
            }

            return true;
        }

        public static void UpdateVersionFile()
        {
            var projectPath = Environment.CurrentDirectory;
            var skillsDir = Path.Combine(projectPath, "aiSkills");
            
            if (!Directory.Exists(skillsDir))
            {
                Directory.CreateDirectory(skillsDir);
            }

            var versionFile = Path.Combine(skillsDir, ".version");
            File.WriteAllText(versionFile, GetCurrentVersion());
        }
    }
}
