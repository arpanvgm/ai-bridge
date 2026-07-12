namespace AIBridge.Cli.Abstractions;

public interface IInputProvider
{
    Task<string?> GetPrimaryInputAsync();
    Task<string?> GetFallbackInputAsync(string prompt);
    Task SetOutputContextAsync(string text);
}
