using AIBridge.Core;
using AIBridge.Helpers;
using AIBridge.Constants;

namespace AIBridge.Commands;

/// <summary>
/// Handles the initialization and scaffolding of the AI Bridge workspace.
/// </summary>
public class InitCommand
{
    private readonly string _projectRoot;

    public InitCommand(string projectRoot)
    {
        _projectRoot = projectRoot;
    }

    /// <summary>
    /// Initializes the AI Bridge environment in the target project.
    /// </summary>
    public async Task InitAsync(bool force = false)
    {
        var projectPath = _projectRoot;
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);

        var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
        if (!Directory.Exists(artifactsDir))
        {
            Directory.CreateDirectory(artifactsDir);
        }

        var responseFilePath = Path.Combine(artifactsDir, FileNames.ResponseXml);
        if (!File.Exists(responseFilePath))
        {
            await File.WriteAllTextAsync(responseFilePath, "<!-- Paste the AI response XML here -->\n");
        }

        var gitignorePath = Path.Combine(projectPath, ".gitignore");
        if (File.Exists(gitignorePath))
        {
            var content = await File.ReadAllTextAsync(gitignorePath);
            bool gitignoreChanged = false;
            if (!content.Contains($"{FolderNames.AiBridge}/{FolderNames.Artifacts}/"))
            {
                await File.AppendAllTextAsync(gitignorePath, $"\n# AI Bridge\n{FolderNames.AiBridge}/{FolderNames.Artifacts}/\n");
                gitignoreChanged = true;
            }
            if (!content.Contains($"{FolderNames.AiBridge}/{FolderNames.SimpleMode}/"))
            {
                await File.AppendAllTextAsync(gitignorePath, $"{FolderNames.AiBridge}/{FolderNames.SimpleMode}/\n");
                gitignoreChanged = true;
            }
            if (!content.Contains($"{FolderNames.AiBridge}/{FolderNames.AdvancedMode}/"))
            {
                await File.AppendAllTextAsync(gitignorePath, $"{FolderNames.AiBridge}/{FolderNames.AdvancedMode}/\n");
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
            var content = await File.ReadAllTextAsync(dockerignorePath);
            if (!content.Contains($"{FolderNames.AiBridge}/"))
            {
                await File.AppendAllTextAsync(dockerignorePath, $"\n# AI Bridge\n{FolderNames.AiBridge}/\n");
                ConsoleHelper.Success("✅ Patched .dockerignore to exclude AI Bridge workspace from Docker builds.");
            }
        }

        var aiIgnorePath = Path.Combine(projectPath, FileNames.AiIgnore);
        if (!File.Exists(aiIgnorePath))
        {
            var defaultIgnore = $"# Additional ignore rules for AI Bridge packing (works alongside .gitignore)\n# Folders should end with /\n{FolderNames.AiBridge}/\nTestResults/\n*.g.cs\n*.log\n*.tmp\n";
            await File.WriteAllTextAsync(aiIgnorePath, defaultIgnore);
            ConsoleHelper.Success("✅ Created default .aiignore file.");
        }
        else
        {
            ConsoleHelper.Info("ℹ .aiignore already exists.");
        }

        var simpleModeDir = Path.Combine(aiWorkspace, FolderNames.SimpleMode);
        var advancedModeDir = Path.Combine(aiWorkspace, FolderNames.AdvancedMode);

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
