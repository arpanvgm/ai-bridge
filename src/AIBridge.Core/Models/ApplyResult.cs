namespace AIBridge.Core.Models;

/// <summary>
/// Represents the outcome of applying an AI-generated XML response.
/// Used by both CLI (console output) and MCP (tool response) workflows.
/// </summary>
public record ApplyResult(
    bool IsSuccess,
    int Created = 0,
    int Patched = 0,
    int Deleted = 0,
    int PatchFailed = 0,
    List<string>? FailedFiles = null,
    string? ErrorMessage = null,
    /// <summary>Carries requested file contents when processing an &lt;ai-request&gt; XML.</summary>
    string? ContextPayload = null,
    /// <summary>
    /// Serialised outer XML of each patch that failed to apply.
    /// Callers can use this to reconstruct the response file with only the failures.
    /// </summary>
    List<string>? FailedPatchesXml = null);
