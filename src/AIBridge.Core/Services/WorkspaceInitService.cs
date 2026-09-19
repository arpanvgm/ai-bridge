
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class WorkspaceInitService(IAIBridgeLogger logger, TemplateService templateService, IndexService indexService, StateService stateService)
{
    public async Task EnsureWorkspaceReadyAsync(string projectRoot, string updateCommand, bool autoInit = false)
    {
        var state = stateService.CheckState();
        
        if (state == WorkspaceState.NotInitialized)
        {
            if (autoInit)
            {
                logger.Info("Initializing AI Bridge for this codebase...");
                await InitializeAsync(projectRoot, force: false);
                stateService.InitState();
            }
            else
            {
                throw new Exception($"AI Bridge workspace is not initialized. Please run '{updateCommand}' first.");
            }
        }
        else if (state == WorkspaceState.Outdated)
        {
            throw new Exception($"AI Bridge version mismatch. The local setup for this codebase is outdated. Please run '{updateCommand}' to update them.");
        }
        else
        {
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
            if (templateService.AreAnyTemplatesMissing(aiWorkspace))
            {
                logger.Info("Detected missing AI instructions. Restoring them...");
                templateService.ExtractTemplates(aiWorkspace, force: false, projectRoot);
            }
        }
    }

    public async Task InitializeAsync(string projectRoot, bool force)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
        if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

        var responseFilePath = Path.Combine(artifactsDir, FileNames.ResponseXml);
        if (!File.Exists(responseFilePath))
            await File.WriteAllTextAsync(responseFilePath, "<!-- Paste the AI response XML here -->\n");

        var innerGitignorePath = Path.Combine(aiWorkspace, ".gitignore");
        var innerGitignoreContent = $"# Ignore templates and artifacts to prevent Git conflicts\n{FolderNames.Artifacts}/\n{FolderNames.SimpleMode}/\n{FolderNames.AdvancedMode}/\n{FolderNames.AutoIndexMode}/\n{FolderNames.Skills}/\n";
        await File.WriteAllTextAsync(innerGitignorePath, innerGitignoreContent);

        var dockerignorePath = Path.Combine(projectRoot, ".dockerignore");
        if (File.Exists(dockerignorePath))
        {
            var content = await File.ReadAllTextAsync(dockerignorePath);
            if (!content.Contains($"{FolderNames.AiBridge}/"))
            {
                await File.AppendAllTextAsync(dockerignorePath, $"\n# AI Bridge\n{FolderNames.AiBridge}/\n");
                logger.Success("✅ Patched .dockerignore to exclude AI Bridge workspace from Docker builds.");
            }
        }

        var aiIgnorePath = Path.Combine(projectRoot, FileNames.AiIgnore);
        var defaultRules = new[] { "TestResults/", "*.g.cs", "*.log", "*.tmp" };
        var header = $"# =================================================================\n" +
                     $"# AI BRIDGE IGNORE FILE\n" +
                     $"# =================================================================\n" +
                     $"# NOTE: Everything in your .gitignore is ALREADY ignored by AI Bridge!\n" +
                     $"# Do not copy your .gitignore here.\n" +
                     $"#\n" +
                     $"# ONLY add files to this list if they are currently tracked by Git,\n" +
                     $"# but you want to hide them from the AI to save tokens (e.g. huge\n" +
                     $"# JSON test data, generated code) or to protect sensitive secrets.\n" +
                     $"# =================================================================\n";

        if (!File.Exists(aiIgnorePath))
        {
            var defaultIgnore = header + string.Join("\n", defaultRules) + "\n";
            await File.WriteAllTextAsync(aiIgnorePath, defaultIgnore);
            logger.Success("✅ Created default .aiignore file.");
        }
        else
        {
            var existingContent = await File.ReadAllTextAsync(aiIgnorePath);
            var existingLines = existingContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                                               .Select(l => l.Trim())
                                               .ToHashSet();
            
            var missingRules = defaultRules.Where(r => !existingLines.Contains(r)).ToList();
            if (missingRules.Count > 0)
            {
                var appendContent = "\n# Auto-added by AI Bridge\n" + string.Join("\n", missingRules) + "\n";
                await File.AppendAllTextAsync(aiIgnorePath, appendContent);
                logger.Success($"✅ Appended {missingRules.Count} missing default rules to .aiignore.");
            }
            else
            {
                logger.Info("ℹ .aiignore is up to date.");
            }
        }

        var simpleModeDir = Path.Combine(aiWorkspace, FolderNames.SimpleMode);
        var advancedModeDir = Path.Combine(aiWorkspace, FolderNames.AdvancedMode);
        var autoIndexModeDir = Path.Combine(aiWorkspace, FolderNames.AutoIndexMode);
        if (force)
        {
            if (Directory.Exists(simpleModeDir)) Directory.Delete(simpleModeDir, true);
            if (Directory.Exists(advancedModeDir)) Directory.Delete(advancedModeDir, true);
            if (Directory.Exists(autoIndexModeDir)) Directory.Delete(autoIndexModeDir, true);
        }

        templateService.ExtractTemplates(aiWorkspace, force, projectRoot);
        
        logger.Info("Generating initial index.xml...");
        await indexService.GenerateIndexAsync(projectRoot);
    }
}
