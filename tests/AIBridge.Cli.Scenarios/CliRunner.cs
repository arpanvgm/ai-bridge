using System.Diagnostics;
using System.Text;

namespace AIBridge.Cli.Scenarios;

public sealed class CliRunner(string dotnetPath, string cliDllPath)
{
    public static async Task<CliRunner> CreateAsync(string repoRoot, string configuration)
    {
        var dotnetPath = "dotnet";
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
