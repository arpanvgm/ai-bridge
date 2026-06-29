using System;
using System.IO;
using AIBridge.Helpers;
using AIBridge.Core;

namespace AIBridge.Commands
{
    public static class InitCommand
    {
        public static void Init(bool force = false)
        {
            var projectPath = WorkspaceHelper.GetProjectRoot();
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);

            var artifactsDir = Path.Combine(aiWorkspace, "artifacts");
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
                if (!content.Contains("ai-bridge/artifacts/"))
                {
                    File.AppendAllText(gitignorePath, "\n# AI Bridge\nai-bridge/artifacts/\n");
                    gitignoreChanged = true;
                }
                if (!content.Contains("ai-bridge/1-SimpleMode/"))
                {
                    File.AppendAllText(gitignorePath, "ai-bridge/1-SimpleMode/\n");
                    gitignoreChanged = true;
                }
                if (!content.Contains("ai-bridge/2-AdvancedMode/"))
                {
                    File.AppendAllText(gitignorePath, "ai-bridge/2-AdvancedMode/\n");
                    gitignoreChanged = true;
                }
                
                if (gitignoreChanged)
                {
                    ConsoleHelper.Success("✅ Patched .gitignore to ignore AI Bridge workspace contents.");
                }
            }

            var dockerignorePath = Path.Combine(projectPath, ".dockerignore");
            if (File.Exists(dockerignorePath))
            {
                var content = File.ReadAllText(dockerignorePath);
                if (!content.Contains("ai-bridge/"))
                {
                    File.AppendAllText(dockerignorePath, "\n# AI Bridge\nai-bridge/\n");
                    ConsoleHelper.Success("✅ Patched .dockerignore to exclude AI Bridge workspace from Docker builds.");
                }
            }

            var aiIgnorePath = Path.Combine(projectPath, ".aiignore");
            if (!File.Exists(aiIgnorePath))
            {
                var defaultIgnore = "# Additional ignore rules for AI Bridge packing (works alongside .gitignore)\n# Folders should end with /\nai-bridge/\nTestResults/\n*.g.cs\n*.log\n*.tmp\n";
                File.WriteAllText(aiIgnorePath, defaultIgnore);
                ConsoleHelper.Success("✅ Created default .aiignore file.");
            }
            else
            {
                ConsoleHelper.Info("ℹ .aiignore already exists.");
            }

            var simpleModeDir = Path.Combine(aiWorkspace, "1-SimpleMode");
            var advancedModeDir = Path.Combine(aiWorkspace, "2-AdvancedMode");

            if (force)
            {
                if (Directory.Exists(simpleModeDir)) Directory.Delete(simpleModeDir, true);
                if (Directory.Exists(advancedModeDir)) Directory.Delete(advancedModeDir, true);
            }

            string baseDir = AppContext.BaseDirectory;
            string sourceTemplatesDir = Path.Combine(baseDir, "Templates");

            ExtractDirectory(sourceTemplatesDir, aiWorkspace, force, projectPath);

            StateManager.InitState();
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
