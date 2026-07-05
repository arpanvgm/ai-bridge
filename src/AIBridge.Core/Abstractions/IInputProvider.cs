namespace AIBridge.Core.Abstractions;

public interface IInputProvider
{
    Task<string?> GetClipboardTextAsync();
    Task<string?> ReadFromStdinAsync(string prompt);
    Task SetClipboardTextAsync(string text);
}
