namespace AIBridge.Cli.Scenarios;

public sealed record CliResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public string CombinedOutput => StandardOutput + StandardError;
}
