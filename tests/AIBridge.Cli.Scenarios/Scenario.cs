namespace AIBridge.Cli.Scenarios;

public sealed record Scenario(string Name, Func<ScenarioContext, Task> Run);

public sealed class ScenarioContext(CliRunner cli, bool keepArtifacts)
{
    public CliRunner Cli { get; } = cli;
    public bool KeepArtifacts { get; } = keepArtifacts;

    public TestWorkspace CreateWorkspace(string scenarioName)
    {
        return TestWorkspace.Create(scenarioName, KeepArtifacts);
    }
}

public sealed record RunnerOptions(
    string? Filter,
    bool KeepArtifacts,
    bool ListOnly,
    bool ShowHelp,
    string Configuration)
{
    public static RunnerOptions Parse(string[] args)
    {
        string? filter = null;
        var keepArtifacts = false;
        var listOnly = false;
        var showHelp = false;
        var configuration = "Debug";

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--filter":
                    filter = ReadValue(args, ref i, "--filter");
                    break;
                case "--keep-artifacts":
                    keepArtifacts = true;
                    break;
                case "--list":
                    listOnly = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--configuration":
                    configuration = ReadValue(args, ref i, "--configuration");
                    break;
                default:
                    throw new ArgumentException($"Unknown runner argument: {args[i]}");
            }
        }

        return new RunnerOptions(filter, keepArtifacts, listOnly, showHelp, configuration);
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{optionName} requires a value.");

        index++;
        return args[index];
    }
}

public static class RepoRootFinder
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ai-bridge.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing ai-bridge.slnx.");
    }
}
