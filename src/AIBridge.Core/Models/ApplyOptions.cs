namespace AIBridge.Core.Models;

public record ApplyOptions(
    bool Watch = false,
    bool Paste = false,
    bool DryRun = false);
