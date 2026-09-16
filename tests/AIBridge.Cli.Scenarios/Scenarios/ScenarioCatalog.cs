using System.Xml.Linq;

namespace AIBridge.Cli.Scenarios.Scenarios;

public static class ScenarioCatalog
{
    public static IReadOnlyList<Scenario> All { get; } =
    [
        new("help shows root usage", HelpRootAsync),
        new("help shows command usage", HelpCommandAsync),
        new("unknown command returns non zero", UnknownCommandAsync),
        new("init creates workspace and templates", InitCreatesWorkspaceAsync),
        new("pack creates full context", PackCreatesFullContextAsync),
        new("pack respects aiignore gitignore and binaries", PackRespectsIgnoresAsync),
        new("apply creates patches deletes and resets response", ApplyCreatePatchDeleteAsync),
        new("apply rejects invalid xml and resets response", ApplyInvalidXmlAsync),
        new("apply blocks file path traversal", ApplyBlocksFileTraversalAsync),
        new("apply reports failed patches and resets response", ApplyFailedPatchAsync),
        new("request creates requested context", RequestCreatesContextAsync),
        new("create index writes index xml", CreateIndexAsync),
        new("update index changes index xml", UpdateIndexAsync),
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
        ScenarioAssert.Contains("--watch", result.CombinedOutput, "Apply help should include watch.");
    }

    private static async Task UnknownCommandAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("unknown command");
        var result = await context.Cli.RunAsync(workspace, "does-not-exist");

        ScenarioAssert.NotEqual(0, result.ExitCode, "Unknown command should fail.");
        ScenarioAssert.Contains("Unrecognized", result.CombinedOutput, "Unknown command should explain parse failure.");
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

    private static async Task ApplyInvalidXmlAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("invalid xml");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", "<ai-response>");

        var result = await context.Cli.RunAsync(workspace, "apply");

        ScenarioAssert.NotEqual(0, result.ExitCode, "Invalid XML should return a non-zero exit code.");
        ScenarioAssert.Contains("not valid XML", result.CombinedOutput, "Invalid XML should be reported.");
        ScenarioAssert.Contains(
            "Paste the AI response XML here",
            workspace.ReadText("ai-bridge/artifacts/ai-response.xml"),
            "Invalid response should reset after the failed apply attempt.");
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

        ScenarioAssert.NotEqual(0, result.ExitCode, "Failed patch should return a non-zero exit code.");
        ScenarioAssert.Contains("Patch failed: Program.cs", result.CombinedOutput, "Failed patch should be reported.");
        ScenarioAssert.DoesNotContain("replacement", workspace.ReadText("Program.cs"), "Failed patch should not modify the target file.");
        ScenarioAssert.FileDoesNotExist(workspace.PathFor("ai-bridge/artifacts/failed-patches.txt"));
        ScenarioAssert.Contains(
            "Paste the AI response XML here",
            workspace.ReadText("ai-bridge/artifacts/ai-response.xml"),
            "Response should reset after the failed patch attempt.");
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
        <ai-response>
          <create-index>
            <module name="DummyApp">
              <file path="Program.cs" purpose="Entry point" />
            </module>
          </create-index>
        </ai-response>
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
        <ai-response>
          <update-index>
            <module name="DummyApp">
              <file path="Program.cs" purpose="New purpose" />
              <file path="Services/GreetingService.cs" purpose="Greeting logic" />
            </module>
          </update-index>
        </ai-response>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");
        var index = workspace.ReadText("ai-bridge/index.xml");

        ScenarioAssert.Equal(0, result.ExitCode, "Update index should succeed.");
        ScenarioAssert.Contains("New purpose", index, "Index should update existing file.");
        ScenarioAssert.Contains("Greeting logic", index, "Index should add new file.");
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
          <tracker>
            <task id="1" status="done">Create runner</task>
            <focus>2</focus>
            <decision id="D1">Use process-level scenarios.</decision>
          </tracker>
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
