using System;
using System.IO;
using AIBridge.Helpers;

namespace AIBridge.Core
{
    public static class Initializer
    {
        public static void Init(bool force = false)
        {
            var projectPath = WorkspaceHelper.GetProjectRoot();
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);

            var artifactsDir = Path.Combine(aiWorkspace, "aiArtifacts");
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
                if (!content.Contains("ai-bridge-*/aiArtifacts/"))
                {
                    File.AppendAllText(gitignorePath, "\n# AI Bridge\nai-bridge-*/aiArtifacts/\n");
                    gitignoreChanged = true;
                }
                if (!content.Contains("ai-bridge-*/aiSkills/"))
                {
                    File.AppendAllText(gitignorePath, "ai-bridge-*/aiSkills/\n");
                    gitignoreChanged = true;
                }
                if (!content.Contains("ai-bridge-*/aiPrompts/"))
                {
                    File.AppendAllText(gitignorePath, "ai-bridge-*/aiPrompts/\n");
                    gitignoreChanged = true;
                }
                
                if (gitignoreChanged)
                {
                    ConsoleHelper.Success("✅ Patched .gitignore to ignore AI Bridge workspace contents.");
                }
            }

            var aiIgnorePath = Path.Combine(projectPath, ".aiignore");
            if (!File.Exists(aiIgnorePath))
            {
                var defaultIgnore = "# Additional ignore rules for AI Bridge packing (works alongside .gitignore)\n# Folders should end with /\nai-bridge-*/\nTestResults/\n*.g.cs\n*.log\n*.tmp\n";
                File.WriteAllText(aiIgnorePath, defaultIgnore);
                ConsoleHelper.Success("✅ Created default .aiignore file.");
            }
            else
            {
                ConsoleHelper.Info("ℹ .aiignore already exists.");
            }

            // Create aiSkills and aiPrompts folders and extract from source folders
            var skillsDir = Path.Combine(aiWorkspace, "aiSkills");
            var promptsDir = Path.Combine(aiWorkspace, "aiPrompts");

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

            ExtractDirectory(sourceSkillsDir, skillsDir, force, projectPath);
            ExtractDirectory(sourcePromptsDir, promptsDir, force, projectPath);

            VersionChecker.UpdateVersionFile();
        }

        private static void ExtractDirectory(string sourceDir, string targetDir, bool force, string projectPath)
        {
            if (!Directory.Exists(sourceDir)) return;
            
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            var relativeTargetDir = Path.GetRelativePath(projectPath, targetDir).Replace('\\', '/');

            foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                var destFile = Path.Combine(targetDir, relPath);
                
                if (!File.Exists(destFile) || force)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                    File.Copy(file, destFile, true);
                    ConsoleHelper.Success($"✅ Extracted {relativeTargetDir}/{relPath}");
                }
                else
                {
                    ConsoleHelper.Info($"ℹ Skipped {relativeTargetDir}/{relPath} (already exists, use 'ai-bridge update' to overwrite)");
                }
            }
        }
    }
}
