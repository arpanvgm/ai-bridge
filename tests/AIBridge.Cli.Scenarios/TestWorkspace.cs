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
