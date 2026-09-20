
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

/// <summary>
/// Unconditionally creates or restores the ai-bridge workspace to a correct state.
/// Has no opinion on whether setup is needed — that is the caller's decision.
/// Never reads or writes state.xml; the caller stamps the version after setup completes.
/// </summary>
public class WorkspaceSetupService(IAIBridgeLogger logger, TemplateService templateService, IndexService indexService)
{
    public async Task SetupAsync(string projectRoot)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);

        await EnsureArtifactsFolderAsync(aiWorkspace);
        await EnsureInnerGitignoreAsync(aiWorkspace);
        await EnsureDockerignoreAsync(projectRoot);
        await EnsureAiIgnoreAsync(projectRoot);
        templateService.ExtractAll(aiWorkspace, projectRoot);

        logger.Info("Syncing index.xml...");
        await indexService.GenerateIndexAsync(projectRoot);
    }

    // ── Private helpers ────────────────────────────────────────────

    private async Task EnsureArtifactsFolderAsync(string aiWorkspace)
    {
        var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
        if (!Directory.Exists(artifactsDir))
            Directory.CreateDirectory(artifactsDir);

        var responseFilePath = Path.Combine(artifactsDir, FileNames.ResponseXml);
        if (!File.Exists(responseFilePath))
            await File.WriteAllTextAsync(responseFilePath, "<!-- Paste the AI response XML here -->\n");
    }

    private async Task EnsureInnerGitignoreAsync(string aiWorkspace)
    {
        var path = Path.Combine(aiWorkspace, ".gitignore");
        var content = $"# Ignore templates and artifacts to prevent Git conflicts\n" +
                      $"{FolderNames.Artifacts}/\n" +
                      $"{FolderNames.SimpleMode}/\n" +
                      $"{FolderNames.AdvancedMode}/\n" +
                      $"{FolderNames.AutoIndexMode}/\n" +
                      $"{FolderNames.Skills}/\n";
        // Always overwrite — this file is fully owned by AI Bridge, never edited by users.
        await File.WriteAllTextAsync(path, content);
    }

    private async Task EnsureDockerignoreAsync(string projectRoot)
    {
        var path = Path.Combine(projectRoot, ".dockerignore");
        if (!File.Exists(path)) return;

        var content = await File.ReadAllTextAsync(path);
        if (!content.Contains($"{FolderNames.AiBridge}/"))
        {
            await File.AppendAllTextAsync(path, $"\n# AI Bridge\n{FolderNames.AiBridge}/\n");
            logger.Success("✅ Patched .dockerignore to exclude AI Bridge workspace from Docker builds.");
        }
    }

    private async Task EnsureAiIgnoreAsync(string projectRoot)
    {
        var path = Path.Combine(projectRoot, FileNames.AiIgnore);
        var defaultRules = new[] { "TestResults/", "*.g.cs", "*.log", "*.tmp" };

        var header = "# =================================================================\n" +
                     "# AI BRIDGE IGNORE FILE\n" +
                     "# =================================================================\n" +
                     "# NOTE: Everything in your .gitignore is ALREADY ignored by AI Bridge!\n" +
                     "# Do not copy your .gitignore here.\n" +
                     "#\n" +
                     "# ONLY add files to this list if they are currently tracked by Git,\n" +
                     "# but you want to hide them from the AI to save tokens (e.g. huge\n" +
                     "# JSON test data, generated code) or to protect sensitive secrets.\n" +
                     "# =================================================================\n";

        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(path, header + string.Join("\n", defaultRules) + "\n");
            logger.Success("✅ Created default .aiignore file.");
            return;
        }

        // File exists — only append rules that are genuinely missing.
        // Never overwrite: this file contains user edits.
        var existingLines = (await File.ReadAllTextAsync(path))
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToHashSet();

        var missingRules = defaultRules.Where(r => !existingLines.Contains(r)).ToList();
        if (missingRules.Count > 0)
        {
            await File.AppendAllTextAsync(path, "\n# Auto-added by AI Bridge\n" + string.Join("\n", missingRules) + "\n");
            logger.Success($"✅ Appended {missingRules.Count} missing default rules to .aiignore.");
        }
        else
        {
            logger.Info("ℹ .aiignore is up to date.");
        }
    }
}
