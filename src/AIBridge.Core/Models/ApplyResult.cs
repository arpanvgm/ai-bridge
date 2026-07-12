namespace AIBridge.Core.Models;

public record ApplyResult(
    bool IsSuccess,
    int Created = 0,
    int Patched = 0,
    int Deleted = 0,
    int PatchFailed = 0,
    List<string>? FailedFiles = null,
    string? ErrorMessage = null);
