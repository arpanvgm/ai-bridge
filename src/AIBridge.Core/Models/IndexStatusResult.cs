namespace AIBridge.Core.Models;

public record IndexStatusResult(
    bool IsSuccess,
    List<string>? Modified = null,
    List<string>? NewFiles = null,
    List<string>? Deleted = null,
    DateTime? LastUpdated = null,
    string? ErrorMessage = null);
