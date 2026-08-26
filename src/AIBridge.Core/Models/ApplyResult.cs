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
    /// Partial operation errors that did not abort the entire run
    /// (e.g. a &lt;file&gt; or &lt;delete&gt; node with a missing path attribute).
    /// Non-null when at least one such error occurred; IsSuccess is false in that case.
    /// </summary>
    List<string>? Errors = null);
