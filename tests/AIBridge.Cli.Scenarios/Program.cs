using AIBridge.Cli.Scenarios;
using AIBridge.Cli.Scenarios.Scenarios;

RunnerOptions options;
try
{
    options = RunnerOptions.Parse(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine("""
    AI Bridge CLI Scenario Runner

    Usage:
      dotnet run --project tests/AIBridge.Cli.Scenarios -- [options]

    Options:
      --filter <text>       Run scenarios whose name contains text.
      --keep-artifacts      Keep temp dummy projects after execution.
      --list                List scenarios without running them.
      --configuration <cfg> Build configuration. Default: Debug.
      --help                Show help.
    """);
    return 0;
}

var scenarios = ScenarioCatalog.All
    .Where(s => options.Filter is null || s.Name.Contains(options.Filter, StringComparison.OrdinalIgnoreCase))
    .ToList();

if (options.ListOnly)
{
    foreach (var scenario in scenarios)
        Console.WriteLine(scenario.Name);

    return 0;
}

if (scenarios.Count == 0)
{
    Console.Error.WriteLine("No scenarios matched the supplied filter.");
    return 2;
}

var repoRoot = RepoRootFinder.Find();
var cliRunner = await CliRunner.CreateAsync(repoRoot, options.Configuration);
var context = new ScenarioContext(cliRunner, options.KeepArtifacts);

var passed = 0;
var failed = 0;

foreach (var scenario in scenarios)
{
    try
    {
        await scenario.Run(context);
        Console.WriteLine($"PASS {scenario.Name}");
        passed++;
    }
    catch (ScenarioFailureException ex)
    {
        Console.WriteLine($"FAIL {scenario.Name}");
        Console.WriteLine($"     {ex.Message.ReplaceLineEndings(Environment.NewLine + "     ")}");
        failed++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {scenario.Name}");
        Console.WriteLine($"     Unexpected error: {ex}");
        failed++;
    }
}

Console.WriteLine();
Console.WriteLine($"Result: {passed} passed, {failed} failed");

return failed == 0 ? 0 : 1;
