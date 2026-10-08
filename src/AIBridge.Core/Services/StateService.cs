
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public enum WorkspaceState
{
    NotInitialized,
    Outdated,
    UpToDate
}

/// <summary>
/// Tracks whether the templates extracted into ai-bridge/ match the templates embedded in this build.
/// The stamp is a machine-local, gitignored file holding a hash of the embedded templates, so there is
/// no version number to bump and a fresh clone (no stamp) is correctly treated as not initialized.
/// </summary>
public class StateService(string projectRoot)
{
    private string GetAiWorkspace() => WorkspaceHelper.GetAiWorkspacePath(projectRoot);

    private string GetStampFilePath() => Path.Combine(GetAiWorkspace(), FileNames.TemplateStamp);

    public WorkspaceState CheckState()
    {
        var stampFile = GetStampFilePath();
        if (!File.Exists(stampFile))
            return WorkspaceState.NotInitialized;

        string stamped;
        try
        {
            stamped = File.ReadAllText(stampFile).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WorkspaceState.Outdated;
        }

        // 1. Were the templates extracted by a build whose embedded templates match this one?
        if (!string.Equals(stamped, TemplateService.ComputeContentHash(), StringComparison.Ordinal))
            return WorkspaceState.Outdated;

        // 2. Are all expected template files still on disk? (existence only — no content reads)
        var aiWorkspace = GetAiWorkspace();
        foreach (var (_, relativePath) in TemplateService.GetEmbeddedTemplates())
        {
            if (!File.Exists(Path.Combine(aiWorkspace, relativePath)))
                return WorkspaceState.Outdated;
        }

        return WorkspaceState.UpToDate;
    }

    /// <summary>
    /// Writes the stamp for the templates embedded in this build. Call only after setup has completed
    /// successfully, so a failed setup never leaves a stamp vouching for a broken workspace.
    /// </summary>
    public void InitState()
    {
        Directory.CreateDirectory(GetAiWorkspace());
        File.WriteAllText(GetStampFilePath(), TemplateService.ComputeContentHash());
    }
}
