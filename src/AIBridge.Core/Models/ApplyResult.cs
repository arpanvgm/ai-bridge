using System.Xml;

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
    /// <summary>Carries requested file contents when processing an ai-request XML.</summary>
    string? ContextPayload = null,
    /// <summary>Holds failed patch XmlNodes so CLI can rebuild ai-response.xml with only failures.</summary>
    List<XmlNode>? FailedPatchNodes = null);
