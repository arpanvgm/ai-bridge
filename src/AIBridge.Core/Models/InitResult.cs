namespace AIBridge.Core.Models;

public record InitResult(
    bool IsSuccess,
    List<string>? ExtractedFiles = null,
    List<string>? SkippedFiles = null,
    string? ErrorMessage = null);
