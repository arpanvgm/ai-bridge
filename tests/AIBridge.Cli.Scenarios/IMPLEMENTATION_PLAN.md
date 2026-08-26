# AIBridge.Cli Scenario Test Project Implementation Plan

This plan adds a separate process-level scenario test project for `AIBridge.Cli`.
It does not add unit tests and does not call production services directly.

The scenario runner builds the real CLI, creates disposable dummy projects under
the system temp directory, runs real CLI commands through `dotnet AIBridge.Cli.dll`,
and verifies exit codes, stdout, stderr, and filesystem side effects.

## Target Structure

Create these files:

```text
tests/
└── AIBridge.Cli.Scenarios/
    ├── AIBridge.Cli.Scenarios.csproj
    ├── README.md
    ├── Program.cs
    ├── CliResult.cs
    ├── CliRunner.cs
    ├── Scenario.cs
    ├── ScenarioAssert.cs
    ├── TestWorkspace.cs
    └── Scenarios/
        └── ScenarioCatalog.cs
```

Update:

```text
ai-bridge.slnx
```

## Step 1: Create Project File

Create `tests/AIBridge.Cli.Scenarios/AIBridge.Cli.Scenarios.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <RootNamespace>AIBridge.Cli.Scenarios</RootNamespace>
  </PropertyGroup>
</Project>
```

## Step 2: Update Solution

Replace `ai-bridge.slnx` with:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/AIBridge.Cli/AIBridge.Cli.csproj" />
    <Project Path="src/AIBridge.Core/AIBridge.Core.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/AIBridge.Cli.Scenarios/AIBridge.Cli.Scenarios.csproj" />
  </Folder>
</Solution>
```

## Step 3: Add Program Entry Point

Create `tests/AIBridge.Cli.Scenarios/Program.cs`:

```csharp
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
```

## Step 4: Add Scenario Types

Create `tests/AIBridge.Cli.Scenarios/Scenario.cs`:

```csharp
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
```

Create `tests/AIBridge.Cli.Scenarios/CliResult.cs`:

```csharp
namespace AIBridge.Cli.Scenarios;

public sealed record CliResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public string CombinedOutput => StandardOutput + StandardError;
}
```

Create `tests/AIBridge.Cli.Scenarios/ScenarioAssert.cs`:

```csharp
namespace AIBridge.Cli.Scenarios;

public sealed class ScenarioFailureException(string message) : Exception(message);

public static class ScenarioAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new ScenarioFailureException(message);
    }

    public static void False(bool condition, string message)
    {
        if (condition)
            throw new ScenarioFailureException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new ScenarioFailureException(
                $"{message}{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual: {actual}");
        }
    }

    public static void NotEqual<T>(T unexpected, T actual, string message)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            throw new ScenarioFailureException($"{message}{Environment.NewLine}Unexpected: {unexpected}");
    }

    public static void Contains(string expected, string actual, string message)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioFailureException(
                $"{message}{Environment.NewLine}Expected to contain: {expected}{Environment.NewLine}Actual:{Environment.NewLine}{actual}");
        }
    }

    public static void DoesNotContain(string unexpected, string actual, string message)
    {
        if (actual.Contains(unexpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioFailureException(
                $"{message}{Environment.NewLine}Did not expect: {unexpected}{Environment.NewLine}Actual:{Environment.NewLine}{actual}");
        }
    }

    public static void FileExists(string path)
    {
        True(File.Exists(path), $"Expected file to exist: {path}");
    }

    public static void DirectoryExists(string path)
    {
        True(Directory.Exists(path), $"Expected directory to exist: {path}");
    }

    public static void FileDoesNotExist(string path)
    {
        False(File.Exists(path), $"Expected file to not exist: {path}");
    }
}
```

## Step 5: Add CLI Process Runner

Create `tests/AIBridge.Cli.Scenarios/CliRunner.cs`:

```csharp
using System.Diagnostics;
using System.Text;

namespace AIBridge.Cli.Scenarios;

public sealed class CliRunner(string dotnetPath, string cliDllPath)
{
    public static async Task<CliRunner> CreateAsync(string repoRoot, string configuration)
    {
        var dotnetPath = Environment.ProcessPath ?? "dotnet";
        var cliProject = Path.Combine(repoRoot, "src", "AIBridge.Cli", "AIBridge.Cli.csproj");

        var build = await RunProcessAsync(
            dotnetPath,
            ["build", cliProject, "--configuration", configuration, "--nologo"],
            repoRoot,
            stdin: null,
            environment: null,
            timeout: TimeSpan.FromMinutes(2));

        ScenarioAssert.Equal(0, build.ExitCode, $"CLI project must build before scenarios run.{Environment.NewLine}{build.CombinedOutput}");

        var cliDll = Path.Combine(
            repoRoot,
            "src",
            "AIBridge.Cli",
            "bin",
            configuration,
            "net10.0",
            "AIBridge.Cli.dll");

        ScenarioAssert.FileExists(cliDll);

        return new CliRunner(dotnetPath, cliDll);
    }

    public Task<CliResult> RunAsync(TestWorkspace workspace, params string[] args)
    {
        return RunAsync(workspace.RootPath, stdin: null, environment: null, args);
    }

    public Task<CliResult> RunAsync(
        string workingDirectory,
        string? stdin,
        IReadOnlyDictionary<string, string?>? environment,
        params string[] args)
    {
        var allArgs = new List<string> { cliDllPath };
        allArgs.AddRange(args);

        return RunProcessAsync(
            dotnetPath,
            allArgs,
            workingDirectory,
            stdin,
            environment,
            timeout: TimeSpan.FromSeconds(30));
    }

    public RunningCliProcess Start(TestWorkspace workspace, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetPath,
            WorkingDirectory = workspace.RootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(cliDllPath);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start CLI process.");
        return new RunningCliProcess(process);
    }

    private static async Task<CliResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        string? stdin,
        IReadOnlyDictionary<string, string?>? environment,
        TimeSpan timeout)
    {
        using var process = new Process();

        process.StartInfo.FileName = fileName;
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardInput = stdin is not null;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        if (environment is not null)
        {
            foreach (var pair in environment)
                process.StartInfo.Environment[pair.Key] = pair.Value;
        }

        process.Start();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(timeout);

        var completed = await Task.WhenAny(exitTask, timeoutTask);
        if (completed == timeoutTask)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException($"Process timed out: {fileName} {string.Join(' ', args)}");
        }

        await exitTask;

        return new CliResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }
}

public sealed class RunningCliProcess(Process process) : IAsyncDisposable
{
    private readonly StringBuilder stdout = new();
    private readonly StringBuilder stderr = new();

    public string CombinedOutput => stdout.ToString() + stderr;

    public void BeginCapture()
    {
        _ = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
                stdout.AppendLine(line);
        });

        _ = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                stderr.AppendLine(line);
        });
    }

    public async Task WaitForOutputAsync(string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (CombinedOutput.Contains(expected, StringComparison.OrdinalIgnoreCase))
                return;

            await Task.Delay(100);
        }

        throw new ScenarioFailureException($"Timed out waiting for output: {expected}{Environment.NewLine}{CombinedOutput}");
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);

        await process.WaitForExitAsync();
        process.Dispose();
    }
}
```

## Step 6: Add Dummy Workspace Builder

Create `tests/AIBridge.Cli.Scenarios/TestWorkspace.cs`:

```csharp
using System.Diagnostics;
using System.Text;

namespace AIBridge.Cli.Scenarios;

public sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string rootPath, bool keepArtifacts)
    {
        RootPath = rootPath;
        KeepArtifacts = keepArtifacts;
    }

    public string RootPath { get; }
    public bool KeepArtifacts { get; }

    public static TestWorkspace Create(string scenarioName, bool keepArtifacts)
    {
        var safeName = string.Concat(
            scenarioName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-'));

        var root = Path.Combine(
            Path.GetTempPath(),
            "ai-bridge-scenarios",
            $"{safeName}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(root);
        return new TestWorkspace(root, keepArtifacts);
    }

    public async Task CreateDotNetDummyProjectAsync()
    {
        await RunGitAsync("init");

        WriteText(".gitignore", """
        bin/
        obj/
        ignored-by-git.txt
        """);

        WriteText(".dockerignore", """
        bin/
        obj/
        """);

        WriteText("DummyApp.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """);

        WriteText("Program.cs", """
        using DummyApp.Services;

        Console.WriteLine(new GreetingService().GetGreeting("World"));
        """);

        WriteText("Services/GreetingService.cs", """
        namespace DummyApp.Services;

        public sealed class GreetingService
        {
            public string GetGreeting(string name) => $"Hello, {name}!";
        }
        """);

        WriteText("docs/notes.md", "# Notes");
        WriteText("ignored-data/sample.json", "{ \"large\": true }");
        WriteText("ignored-by-git.txt", "git ignored");
        WriteBytes("logo.png", [(byte)0x89, 0x50, 0x4E, 0x47]);
    }

    public string PathFor(string relativePath)
    {
        return Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public void WriteText(string relativePath, string content)
    {
        var path = PathFor(relativePath);
        var directory = Path.GetDirectoryName(path) ?? RootPath;
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, content.ReplaceLineEndings(Environment.NewLine), Encoding.UTF8);
    }

    public void WriteBytes(string relativePath, byte[] content)
    {
        var path = PathFor(relativePath);
        var directory = Path.GetDirectoryName(path) ?? RootPath;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, content);
    }

    public string ReadText(string relativePath)
    {
        return File.ReadAllText(PathFor(relativePath));
    }

    public string ReadAllContextFiles()
    {
        var artifacts = PathFor("ai-bridge/artifacts");

        return string.Join(
            Environment.NewLine,
            Directory.GetFiles(artifacts, "*context.txt").OrderBy(path => path).Select(File.ReadAllText));
    }

    public async Task RunGitAsync(params string[] args)
    {
        using var process = new Process();

        process.StartInfo.FileName = "git";
        process.StartInfo.WorkingDirectory = RootPath;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new ScenarioFailureException(
                $"git {string.Join(' ', args)} failed.{Environment.NewLine}{stdout}{stderr}");
        }
    }

    public void Dispose()
    {
        if (!KeepArtifacts && Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
            return;
        }

        Console.WriteLine($"     Workspace kept: {RootPath}");
    }
}
```

## Step 7: Add Scenario Catalog

Create directory:

```text
tests/AIBridge.Cli.Scenarios/Scenarios
```

Create `tests/AIBridge.Cli.Scenarios/Scenarios/ScenarioCatalog.cs`:

```csharp
using System.Xml.Linq;

namespace AIBridge.Cli.Scenarios.Scenarios;

public static class ScenarioCatalog
{
    public static IReadOnlyList<Scenario> All { get; } =
    [
        new("help shows root usage", HelpRootAsync),
        new("help shows command usage", HelpCommandAsync),
        new("unknown command returns non zero", UnknownCommandAsync),
        new("pack fails before init", PackFailsBeforeInitAsync),
        new("init creates workspace and templates", InitCreatesWorkspaceAsync),
        new("init is idempotent and update refreshes templates", InitAndUpdateAsync),
        new("pack creates full context", PackCreatesFullContextAsync),
        new("pack respects aiignore gitignore and binaries", PackRespectsIgnoresAsync),
        new("apply creates patches deletes and resets response", ApplyCreatePatchDeleteAsync),
        new("apply dry run leaves files unchanged", ApplyDryRunAsync),
        new("apply rejects invalid xml", ApplyInvalidXmlAsync),
        new("apply blocks file path traversal", ApplyBlocksFileTraversalAsync),
        new("apply records failed patches", ApplyFailedPatchAsync),
        new("request creates requested context", RequestCreatesContextAsync),
        new("create index writes index xml", CreateIndexAsync),
        new("update index changes index xml", UpdateIndexAsync),
        new("index status detects modified new deleted files", IndexStatusAsync),
        new("pack incremental includes changed files only", IncrementalPackAsync),
        new("advanced edits require index update", AdvancedRequiresIndexUpdateAsync),
        new("tracker create and update works", TrackerAsync),
        new("apply paste falls back to stdin", PasteFallbackAsync),
        new("apply watch applies saved response", WatchAsync)
    ];

    private static async Task HelpRootAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("help root");
        var result = await context.Cli.RunAsync(workspace, "--help");

        ScenarioAssert.Equal(0, result.ExitCode, "Root help should succeed.");
        ScenarioAssert.Contains("AI Bridge", result.CombinedOutput, "Root help should mention AI Bridge.");
        ScenarioAssert.Contains("pack", result.CombinedOutput, "Root help should list commands.");
    }

    private static async Task HelpCommandAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("help command");
        var result = await context.Cli.RunAsync(workspace, "apply", "--help");

        ScenarioAssert.Equal(0, result.ExitCode, "Command help should succeed.");
        ScenarioAssert.Contains("--dry-run", result.CombinedOutput, "Apply help should include dry-run.");
        ScenarioAssert.Contains("--watch", result.CombinedOutput, "Apply help should include watch.");
    }

    private static async Task UnknownCommandAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("unknown command");
        var result = await context.Cli.RunAsync(workspace, "does-not-exist");

        ScenarioAssert.NotEqual(0, result.ExitCode, "Unknown command should fail.");
        ScenarioAssert.Contains("Unrecognized", result.CombinedOutput, "Unknown command should explain parse failure.");
    }

    private static async Task PackFailsBeforeInitAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("pack before init");
        await workspace.CreateDotNetDummyProjectAsync();

        var result = await context.Cli.RunAsync(workspace, "pack");

        ScenarioAssert.NotEqual(0, result.ExitCode, "Pack before init should fail.");
        ScenarioAssert.Contains("Please run 'ai-bridge init' first", result.CombinedOutput, "Pack should explain missing init.");
    }

    private static async Task InitCreatesWorkspaceAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("init creates");
        await workspace.CreateDotNetDummyProjectAsync();

        var result = await context.Cli.RunAsync(workspace, "init");

        ScenarioAssert.Equal(0, result.ExitCode, "Init should succeed.");
        ScenarioAssert.FileExists(workspace.PathFor(".aiignore"));
        ScenarioAssert.FileExists(workspace.PathFor("ai-bridge/state.xml"));
        ScenarioAssert.FileExists(workspace.PathFor("ai-bridge/.gitignore"));
        ScenarioAssert.FileExists(workspace.PathFor("ai-bridge/artifacts/ai-response.xml"));
        ScenarioAssert.DirectoryExists(workspace.PathFor("ai-bridge/1-SimpleMode"));
        ScenarioAssert.DirectoryExists(workspace.PathFor("ai-bridge/2-AdvancedMode"));
        ScenarioAssert.Contains("ai-bridge/", workspace.ReadText(".dockerignore"), "Init should patch dockerignore.");
    }

    private static async Task InitAndUpdateAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("init update");
        await workspace.CreateDotNetDummyProjectAsync();

        ScenarioAssert.Equal(0, (await context.Cli.RunAsync(workspace, "init")).ExitCode, "Initial init should pass.");

        const string template = "ai-bridge/1-SimpleMode/ai-system-prompt.md";
        workspace.WriteText(template, "custom local edit");

        ScenarioAssert.Equal(0, (await context.Cli.RunAsync(workspace, "init")).ExitCode, "Second init should pass.");
        ScenarioAssert.Contains("custom local edit", workspace.ReadText(template), "Init should not overwrite existing templates.");

        ScenarioAssert.Equal(0, (await context.Cli.RunAsync(workspace, "update")).ExitCode, "Update should pass.");
        ScenarioAssert.DoesNotContain("custom local edit", workspace.ReadText(template), "Update should refresh templates.");
    }

    private static async Task PackCreatesFullContextAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("pack full");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        var result = await context.Cli.RunAsync(workspace, "pack");

        ScenarioAssert.Equal(0, result.ExitCode, "Pack should succeed.");
        var contextText = workspace.ReadAllContextFiles();

        ScenarioAssert.Contains("<module", contextText, "Context should contain module XML.");
        ScenarioAssert.Contains("Program.cs", contextText, "Context should include Program.cs.");
        ScenarioAssert.Contains("GreetingService.cs", contextText, "Context should include service file.");
    }

    private static async Task PackRespectsIgnoresAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("pack ignores");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        File.AppendAllText(
            workspace.PathFor(".aiignore"),
            $"{Environment.NewLine}ignored-data/{Environment.NewLine}notes.md{Environment.NewLine}");

        var result = await context.Cli.RunAsync(workspace, "pack");
        var contextText = workspace.ReadAllContextFiles();

        ScenarioAssert.Equal(0, result.ExitCode, "Pack should succeed.");
        ScenarioAssert.DoesNotContain("sample.json", contextText, "Pack should exclude aiignored folder.");
        ScenarioAssert.DoesNotContain("notes.md", contextText, "Pack should exclude aiignored filename.");
        ScenarioAssert.DoesNotContain("ignored-by-git.txt", contextText, "Pack should respect gitignore.");
        ScenarioAssert.DoesNotContain("logo.png", contextText, "Pack should exclude binary file.");
        ScenarioAssert.DoesNotContain("ai-bridge/state.xml", contextText, "Pack should exclude AI Bridge workspace.");
    }

    private static async Task ApplyCreatePatchDeleteAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("apply edits");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-response>
          <ai-edits>
            <file path="Generated/Feature.cs"><![CDATA[
        namespace DummyApp.Generated;

        public static class Feature
        {
            public static string Name => "AI Bridge";
        }
        ]]></file>
            <patch path="Program.cs">
              <search><![CDATA[Console.WriteLine(new GreetingService().GetGreeting("World"));]]></search>
              <replace><![CDATA[Console.WriteLine(new GreetingService().GetGreeting("AI Bridge"));]]></replace>
            </patch>
            <delete path="docs/notes.md" />
          </ai-edits>
        </ai-response>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");

        ScenarioAssert.Equal(0, result.ExitCode, "Apply should succeed.");
        ScenarioAssert.FileExists(workspace.PathFor("Generated/Feature.cs"));
        ScenarioAssert.Contains("AI Bridge", workspace.ReadText("Program.cs"), "Patch should modify Program.cs.");
        ScenarioAssert.FileDoesNotExist(workspace.PathFor("docs/notes.md"));
        ScenarioAssert.Contains(
            "Paste the AI response XML here",
            workspace.ReadText("ai-bridge/artifacts/ai-response.xml"),
            "Response file should reset.");
    }

    private static async Task ApplyDryRunAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("apply dry run");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        var before = workspace.ReadText("Program.cs");

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-response>
          <ai-edits>
            <file path="Generated/DryRun.cs">public class DryRun { }</file>
            <patch path="Program.cs">
              <search>World</search>
              <replace>DryRun</replace>
            </patch>
            <delete path="docs/notes.md" />
          </ai-edits>
        </ai-response>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply", "--dry-run");

        ScenarioAssert.Equal(0, result.ExitCode, "Dry run should succeed.");
        ScenarioAssert.FileDoesNotExist(workspace.PathFor("Generated/DryRun.cs"));
        ScenarioAssert.Equal(before, workspace.ReadText("Program.cs"), "Dry run should not patch files.");
        ScenarioAssert.FileExists(workspace.PathFor("docs/notes.md"));
        ScenarioAssert.Contains("[dry-run]", result.CombinedOutput, "Dry run should report planned changes.");
    }

    private static async Task ApplyInvalidXmlAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("invalid xml");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", "<ai-response>");

        var result = await context.Cli.RunAsync(workspace, "apply");

        ScenarioAssert.Contains("not valid XML", result.CombinedOutput, "Invalid XML should be reported.");
        ScenarioAssert.Contains(
            "<ai-response>",
            workspace.ReadText("ai-bridge/artifacts/ai-response.xml"),
            "Invalid response should remain for correction.");
    }

    private static async Task ApplyBlocksFileTraversalAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("file traversal");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        var parent = Directory.GetParent(workspace.RootPath)
            ?? throw new ScenarioFailureException("Workspace parent directory was not found.");
        var outside = Path.Combine(parent.FullName, "outside.txt");

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-response>
          <ai-edits>
            <file path="../outside.txt">blocked</file>
          </ai-edits>
        </ai-response>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");

        ScenarioAssert.NotEqual(0, result.ExitCode, "Path traversal should fail.");
        ScenarioAssert.FileDoesNotExist(outside);
        ScenarioAssert.Contains("resolves outside project root", result.CombinedOutput, "Traversal should be explained.");
    }

    private static async Task ApplyFailedPatchAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("failed patch");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-response>
          <ai-edits>
            <patch path="Program.cs">
              <search>text that does not exist</search>
              <replace>replacement</replace>
            </patch>
          </ai-edits>
        </ai-response>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");

        ScenarioAssert.Contains("Failed patches", result.CombinedOutput, "Failed patch should be reported.");
        ScenarioAssert.FileExists(workspace.PathFor("ai-bridge/artifacts/failed-patches.txt"));
        ScenarioAssert.Contains(
            "<patch",
            workspace.ReadText("ai-bridge/artifacts/ai-response.xml"),
            "Response should be rebuilt with failed patch.");
    }

    private static async Task RequestCreatesContextAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("request context");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        File.AppendAllText(workspace.PathFor(".aiignore"), $"{Environment.NewLine}ignored-data/{Environment.NewLine}");

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-request>
          <file path="Program.cs" />
          <file path="missing.txt" />
          <file path="ignored-data/sample.json" />
        </ai-request>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");
        var requestedContext = workspace.ReadText("ai-bridge/artifacts/ai-requested-context.txt");

        ScenarioAssert.Equal(0, result.ExitCode, "Request should succeed.");
        ScenarioAssert.Contains("Program.cs", requestedContext, "Requested context should include real file.");
        ScenarioAssert.Contains("File not found on disk", requestedContext, "Requested context should include missing marker.");
        ScenarioAssert.Contains("ACCESS DENIED", requestedContext, "Requested context should block aiignored file.");
    }

    private static async Task CreateIndexAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("create index");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <create-ai-bridge-index>
          <module name="DummyApp">
            <file path="Program.cs" purpose="Entry point" />
          </module>
        </create-ai-bridge-index>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");

        ScenarioAssert.Equal(0, result.ExitCode, "Create index should succeed.");
        ScenarioAssert.FileExists(workspace.PathFor("ai-bridge/index.xml"));

        var xml = XDocument.Load(workspace.PathFor("ai-bridge/index.xml"));
        ScenarioAssert.Equal("ai-bridge-index", xml.Root?.Name.LocalName, "Index root should be correct.");
        ScenarioAssert.Contains("Program.cs", xml.ToString(), "Index should contain file.");
    }

    private static async Task UpdateIndexAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("update index");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        workspace.WriteText("ai-bridge/index.xml", """
        <ai-bridge-index lastUpdated="2000-01-01T00:00:00.0000000Z">
          <module name="DummyApp">
            <file path="Program.cs" purpose="Old purpose" />
          </module>
        </ai-bridge-index>
        """);

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <update-ai-bridge-index>
          <module name="DummyApp">
            <file path="Program.cs" purpose="New purpose" />
            <file path="Services/GreetingService.cs" purpose="Greeting logic" />
          </module>
        </update-ai-bridge-index>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");
        var index = workspace.ReadText("ai-bridge/index.xml");

        ScenarioAssert.Equal(0, result.ExitCode, "Update index should succeed.");
        ScenarioAssert.Contains("New purpose", index, "Index should update existing file.");
        ScenarioAssert.Contains("Greeting logic", index, "Index should add new file.");
    }

    private static async Task IndexStatusAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("index status");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        workspace.WriteText("ai-bridge/index.xml", """
        <ai-bridge-index lastUpdated="2000-01-01T00:00:00.0000000Z">
          <module name="DummyApp">
            <file path="Program.cs" purpose="Entry" />
            <file path="docs/notes.md" purpose="Docs" />
          </module>
        </ai-bridge-index>
        """);

        workspace.WriteText("Program.cs", "// modified");
        File.Delete(workspace.PathFor("docs/notes.md"));
        workspace.WriteText("NewThing.cs", "public class NewThing { }");

        var result = await context.Cli.RunAsync(workspace, "index", "status");

        ScenarioAssert.Equal(0, result.ExitCode, "Index status command should complete.");
        ScenarioAssert.Contains("Program.cs", result.CombinedOutput, "Status should show modified indexed file.");
        ScenarioAssert.Contains("docs/notes.md", result.CombinedOutput, "Status should show deleted indexed file.");
        ScenarioAssert.Contains("NewThing.cs", result.CombinedOutput, "Status should show new file.");
    }

    private static async Task IncrementalPackAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("incremental pack");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        workspace.WriteText("ai-bridge/index.xml", $"""
        <ai-bridge-index lastUpdated="{DateTime.UtcNow:o}">
          <module name="DummyApp">
            <file path="DummyApp.csproj" purpose="Project" />
            <file path="Program.cs" purpose="Entry" />
            <file path="Services/GreetingService.cs" purpose="Greeting" />
            <file path="docs/notes.md" purpose="Docs" />
          </module>
        </ai-bridge-index>
        """);

        await Task.Delay(1200);
        workspace.WriteText("Program.cs", "// changed program");
        workspace.WriteText("Features/NewFeature.cs", "public class NewFeature { }");

        var result = await context.Cli.RunAsync(workspace, "pack", "--incremental");
        var incremental = workspace.ReadText("ai-bridge/artifacts/ai-incremental-context.txt");

        ScenarioAssert.Equal(0, result.ExitCode, "Incremental pack should succeed.");
        ScenarioAssert.Contains("Program.cs", incremental, "Incremental context should include modified file.");
        ScenarioAssert.Contains("NewFeature.cs", incremental, "Incremental context should include new file.");
        ScenarioAssert.DoesNotContain("GreetingService.cs", incremental, "Incremental context should skip unchanged indexed file.");
    }

    private static async Task AdvancedRequiresIndexUpdateAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("advanced requires index update");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        workspace.WriteText("ai-bridge/index.xml", """
        <ai-bridge-index lastUpdated="2000-01-01T00:00:00.0000000Z">
          <module name="DummyApp">
            <file path="Program.cs" purpose="Entry" />
          </module>
        </ai-bridge-index>
        """);

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-response>
          <ai-edits>
            <file path="Generated/MissingIndexUpdate.cs">public class MissingIndexUpdate { }</file>
          </ai-edits>
        </ai-response>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");

        ScenarioAssert.Contains("forgot to provide", result.CombinedOutput, "Advanced mode should require index update.");
        ScenarioAssert.FileDoesNotExist(workspace.PathFor("Generated/MissingIndexUpdate.cs"));
    }

    private static async Task TrackerAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("tracker");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-response>
          <tracker>
            <scope>Build scenario tests</scope>
            <tasks>
              <task id="1">Create runner</task>
              <task id="2">Add scenarios</task>
            </tasks>
            <focus>1</focus>
          </tracker>
        </ai-response>
        """);

        var createResult = await context.Cli.RunAsync(workspace, "apply");
        ScenarioAssert.Equal(0, createResult.ExitCode, "Tracker create should succeed.");
        ScenarioAssert.FileExists(workspace.PathFor("ai-bridge/artifacts/tracker.xml"));

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-response>
          <tracker-update>
            <done>1</done>
            <focus>2</focus>
            <decision id="D1">Use process-level scenarios.</decision>
          </tracker-update>
        </ai-response>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");
        var tracker = workspace.ReadText("ai-bridge/artifacts/tracker.xml");

        ScenarioAssert.Equal(0, result.ExitCode, "Tracker update should succeed.");
        ScenarioAssert.Contains("status=\"done\"", tracker, "Tracker should mark task done.");
        ScenarioAssert.Contains("<focus>2</focus>", tracker, "Tracker should update focus.");
        ScenarioAssert.Contains("Use process-level scenarios", tracker, "Tracker should add decision.");
    }

    private static async Task PasteFallbackAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("paste fallback");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        var emptyPath = Path.Combine(workspace.RootPath, "empty-path");
        Directory.CreateDirectory(emptyPath);

        const string stdin = """
        <ai-response>
          <ai-edits>
            <file path="FromPaste.cs">public class FromPaste { }</file>
          </ai-edits>
        </ai-response>
        """;

        var result = await context.Cli.RunAsync(
            workspace.RootPath,
            stdin,
            new Dictionary<string, string?> { ["PATH"] = emptyPath },
            "apply",
            "--paste");

        ScenarioAssert.Equal(0, result.ExitCode, "Paste fallback should succeed with stdin.");
        ScenarioAssert.FileExists(workspace.PathFor("FromPaste.cs"));
        ScenarioAssert.Contains("stdin", result.CombinedOutput, "Output should mention stdin fallback.");
    }

    private static async Task WatchAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("watch");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        await using var process = context.Cli.Start(workspace, "apply", "--watch");
        process.BeginCapture();
        await process.WaitForOutputAsync("Waiting for next change", TimeSpan.FromSeconds(10));

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-response>
          <ai-edits>
            <file path="Watched.cs">public class Watched { }</file>
          </ai-edits>
        </ai-response>
        """);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && !File.Exists(workspace.PathFor("Watched.cs")))
            await Task.Delay(100);

        ScenarioAssert.FileExists(workspace.PathFor("Watched.cs"));
    }
}
```

## Step 8: Add Scenario README

Create `tests/AIBridge.Cli.Scenarios/README.md`:

```markdown
# AI Bridge CLI Scenarios

This project runs process-level scenario tests against the real `AIBridge.Cli`.

It is not a unit test project. It creates temporary dummy repositories, runs the
CLI through a real process, and verifies command output plus filesystem effects.

## Run All Scenarios

```bash
dotnet run --project tests/AIBridge.Cli.Scenarios
```

## Run a Subset

```bash
dotnet run --project tests/AIBridge.Cli.Scenarios -- --filter apply
```

## List Scenarios

```bash
dotnet run --project tests/AIBridge.Cli.Scenarios -- --list
```

## Keep Temporary Projects

```bash
dotnet run --project tests/AIBridge.Cli.Scenarios -- --keep-artifacts
```

Temporary projects are created under:

```text
/tmp/ai-bridge-scenarios/
```

## Exit Codes

- `0`: all scenarios passed
- `1`: one or more scenarios failed
- `2`: invalid runner arguments or no scenarios matched

## Failure Output

Example:

```text
PASS init creates workspace and templates
FAIL apply rejects invalid xml
     Expected to contain: not valid XML
     Actual:
     ...

Result: 21 passed, 1 failed
```
```

## Step 9: Run Validation Commands

Run these commands from the repository root:

```bash
dotnet build ai-bridge.slnx
dotnet run --project tests/AIBridge.Cli.Scenarios -- --list
dotnet run --project tests/AIBridge.Cli.Scenarios
```

## Step 10: Expected Coverage

The scenarios cover:

- root help
- command help
- unknown command behavior
- `init`
- idempotent `init`
- `update`
- `pack` before init
- full `pack`
- `.aiignore`
- `.gitignore`
- binary exclusions
- `apply` file create
- `apply` patch
- `apply` delete
- `apply --dry-run`
- invalid XML handling
- path traversal protection
- failed patch handling
- `<ai-request>`
- `<create-ai-bridge-index>`
- `<update-ai-bridge-index>`
- `index status`
- `pack --incremental`
- advanced mode requirement for index updates
- tracker create
- tracker update
- `apply --paste` stdin fallback
- `apply --watch`

## Step 11: Important Notes For The Implementing Agent

1. Do not add xUnit, NUnit, MSTest, or any unit test framework.
2. Do not reference `AIBridge.Core` or `AIBridge.Cli` from the scenario project.
3. The scenario runner must execute the built CLI DLL as a separate process.
4. Use temporary dummy projects only; do not run scenario commands against the repository itself.
5. Keep all test artifacts out of source control.
6. If a scenario fails because the current CLI returns success for an error case, leave the scenario failure visible. That is useful acceptance-test feedback.
7. If `apply --watch` is flaky on the target machine, keep the scenario but allow it to be run alone with `--filter watch` while diagnosing.
