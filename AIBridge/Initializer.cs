using System;
using System.IO;

namespace AIBridge
{
    public static class Initializer
    {
        public static void Init(bool force = false)
        {
            var projectPath = Environment.CurrentDirectory;
            var artifactsDir = Path.Combine(projectPath, "aiArtifacts");
            if (!Directory.Exists(artifactsDir))
            {
                Directory.CreateDirectory(artifactsDir);
            }

            var responseFilePath = Path.Combine(artifactsDir, "ai-response.xml");
            if (!File.Exists(responseFilePath))
            {
                File.WriteAllText(responseFilePath, "<!-- Paste the AI response XML here -->\n");
            }

            var gitignorePath = Path.Combine(projectPath, ".gitignore");
            if (File.Exists(gitignorePath))
            {
                var content = File.ReadAllText(gitignorePath);
                bool gitignoreChanged = false;
                if (!content.Contains("aiArtifacts/"))
                {
                    File.AppendAllText(gitignorePath, "\n# AI Bridge\naiArtifacts/\n");
                    gitignoreChanged = true;
                }
                if (!content.Contains("aiSkills/"))
                {
                    if (gitignoreChanged) File.AppendAllText(gitignorePath, "aiSkills/\n");
                    else File.AppendAllText(gitignorePath, "\n# AI Bridge\naiSkills/\n");
                    gitignoreChanged = true;
                }
                if (!content.Contains("aiPrompts/"))
                {
                    if (gitignoreChanged) File.AppendAllText(gitignorePath, "aiPrompts/\n");
                    else File.AppendAllText(gitignorePath, "\n# AI Bridge\naiPrompts/\n");
                    gitignoreChanged = true;
                }
                if (gitignoreChanged)
                {
                    ConsoleHelper.Success("✅ Patched .gitignore to ignore AI Bridge folders.");
                }
            }

            var aiIgnorePath = Path.Combine(projectPath, ".aiignore");
            if (!File.Exists(aiIgnorePath))
            {
                var defaultIgnore = "# Additional ignore rules for AI Bridge packing (works alongside .gitignore)\n# Folders should end with /\naiSkills/\naiPrompts/\nTestResults/\n*.g.cs\n*.log\n*.tmp\nai-bridge-index.xml\n";
                File.WriteAllText(aiIgnorePath, defaultIgnore);
                ConsoleHelper.Success("✅ Created default .aiignore file.");
            }
            else
            {
                ConsoleHelper.Info("ℹ .aiignore already exists.");
            }

            // Create aiSkills and aiPrompts folders and extract from source folders
            var skillsDir = Path.Combine(projectPath, "aiSkills");
            var promptsDir = Path.Combine(projectPath, "aiPrompts");

            if (force)
            {
                if (Directory.Exists(skillsDir))
                {
                    foreach (var existingFile in Directory.GetFiles(skillsDir)) File.Delete(existingFile);
                }
                if (Directory.Exists(promptsDir))
                {
                    foreach (var existingFile in Directory.GetFiles(promptsDir)) File.Delete(existingFile);
                }
            }

            string baseDir = AppContext.BaseDirectory;
            string sourceSkillsDir = Path.Combine(baseDir, "aiSkillSources");
            string sourcePromptsDir = Path.Combine(baseDir, "aiPromptSources");

            ExtractDirectory(sourceSkillsDir, skillsDir, force, "aiSkills");
            ExtractDirectory(sourcePromptsDir, promptsDir, force, "aiPrompts");

            VersionChecker.UpdateVersionFile();
        }

        private static void ExtractDirectory(string sourceDir, string targetDir, bool force, string displayName)
        {
            if (!Directory.Exists(sourceDir)) return;
            
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(sourceDir, file);
                var destFile = Path.Combine(targetDir, relPath);
                
                if (!File.Exists(destFile) || force)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                    File.Copy(file, destFile, true);
                    ConsoleHelper.Success($"✅ Extracted {displayName}/{relPath.Replace('\\', '/')}");
                }
                else
                {
                    ConsoleHelper.Info($"ℹ Skipped {displayName}/{relPath.Replace('\\', '/')} (already exists, use 'ai-bridge update' to overwrite)");
                }
            }
        }
    }
}
