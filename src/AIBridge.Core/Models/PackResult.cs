namespace AIBridge.Core.Models;

public record PackResult(
    bool IsSuccess,
    int FileCount = 0,
    long TotalSizeBytes = 0,
    int ApproxTokens = 0,
    List<string>? Warnings = null,
    string? ErrorMessage = null);
