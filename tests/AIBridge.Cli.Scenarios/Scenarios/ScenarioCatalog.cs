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
        new("init is safe to run twice", InitIdempotentAsync),
        new("migrate refreshes templates and preserves index purposes", MigratePreservesIndexAsync),
        new("migrate re-extracts template folders", MigrateReExtractsTemplatesAsync),
        new("pack creates full context from git tracked files", PackCreatesFullContextAsync),
        new("pack respects aiignore gitignore and binaries", PackRespectsIgnoresAsync),
        new("pack fails when workspace not initialized", PackFailsWhenNotInitializedAsync),
        new("pack fails when workspace version mismatches", PackFailsOnVersionMismatchAsync),
        new("apply creates patches deletes and resets response", ApplyCreatePatchDeleteAsync),
        new("apply rejects invalid xml and resets response", ApplyInvalidXmlAsync),
        new("apply blocks file path traversal", ApplyBlocksFileTraversalAsync),
        new("apply reports failed patches and resets response", ApplyFailedPatchAsync),
        new("apply fails when workspace not initialized", ApplyFailsWhenNotInitializedAsync),
        new("apply fails when workspace version mismatches", ApplyFailsOnVersionMismatchAsync),
        new("request creates requested context", RequestCreatesContextAsync),
        new("request out of sync index returns changed and new files", RequestOutOfSyncIndexAsync),
        new("create index writes index xml", CreateIndexAsync),
        new("update index changes index xml", UpdateIndexAsync),
        new("tracker create and update works", TrackerAsync),
        new("apply paste falls back to stdin", PasteFallbackAsync),
        new("apply watch applies saved response", WatchAsync),
        new("init creates auto index mode folder", InitCreatesAutoIndexModeFolderAsync),
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
        ScenarioAssert.DirectoryExists(workspace.PathFor("ai-bridge/AutoIndexMode"));
        ScenarioAssert.FileExists(workspace.PathFor("ai-bridge/index.xml"));
        ScenarioAssert.DirectoryExists(workspace.PathFor("ai-bridge/skills"));
        ScenarioAssert.False(
            Directory.Exists(workspace.PathFor("ai-bridge/1-SimpleMode")),
            "1-SimpleMode should not be extracted — it is a legacy folder.");
        ScenarioAssert.False(
            Directory.Exists(workspace.PathFor("ai-bridge/2-AdvancedMode")),
            "2-AdvancedMode should not be extracted — it is a legacy folder.");
        ScenarioAssert.Contains("ai-bridge/", workspace.ReadText(".dockerignore"), "Init should patch dockerignore.");
    }

    private static async Task InitCreatesAutoIndexModeFolderAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("init auto index mode");
        await workspace.CreateDotNetDummyProjectAsync();

        var result = await context.Cli.RunAsync(workspace, "init");

        ScenarioAssert.Equal(0, result.ExitCode, "Init should succeed.");
        ScenarioAssert.DirectoryExists(workspace.PathFor("ai-bridge/AutoIndexMode"));

        // Verify AutoIndexMode contains at least the skill files the AI needs
        var autoIndexFiles = Directory.GetFiles(
            workspace.PathFor("ai-bridge/AutoIndexMode"),
            "*",
            SearchOption.AllDirectories);

        ScenarioAssert.True(autoIndexFiles.Length > 0, "AutoIndexMode should contain extracted skill/prompt files.");
    }

    private static async Task InitIdempotentAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("init idempotent");
        await workspace.CreateDotNetDummyProjectAsync();

        // First init
        await context.Cli.RunAsync(workspace, "init");

        // Write a known purpose into the index so we can verify it is preserved
        workspace.WriteText("ai-bridge/index.xml", """
        <ai-bridge-index lastUpdated="2000-01-01T00:00:00.0000000Z">
          <module name="DummyApp">
            <file path="Program.cs" purpose="Preserved purpose" />
          </module>
        </ai-bridge-index>
        """);

        // Write something to ai-response.xml so we can verify it is NOT overwritten
        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", "<!-- user content -->");

        // Second init
        var result = await context.Cli.RunAsync(workspace, "init");

        ScenarioAssert.Equal(0, result.ExitCode, "Second init should succeed.");

        // User-edited files must not be overwritten
        ScenarioAssert.Contains(
            "user content",
            workspace.ReadText("ai-bridge/artifacts/ai-response.xml"),
            "Init must not overwrite existing ai-response.xml.");

        // Existing index purposes must be preserved
        ScenarioAssert.Contains(
            "Preserved purpose",
            workspace.ReadText("ai-bridge/index.xml"),
            "Init must preserve existing index purposes.");

        // .aiignore must not accumulate duplicate rules
        var aiIgnoreContent = workspace.ReadText(".aiignore");
        var ruleCount = aiIgnoreContent.Split('\n')
            .Count(l => l.Trim() == "*.log");
        ScenarioAssert.True(ruleCount <= 1, ".aiignore must not accumulate duplicate default rules.");
    }

    private static async Task MigratePreservesIndexAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("migrate preserves index");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        // Simulate AI having filled in purposes
        workspace.WriteText("ai-bridge/index.xml", """
        <ai-bridge-index lastUpdated="2000-01-01T00:00:00.0000000Z">
          <module name="DummyApp">
            <file path="Program.cs" purpose="Entry point of the application" />
            <file path="Services/GreetingService.cs" purpose="Provides greeting logic" />
          </module>
        </ai-bridge-index>
        """);

        var result = await context.Cli.RunAsync(workspace, "migrate");

        ScenarioAssert.Equal(0, result.ExitCode, "Migrate should succeed.");

        var index = workspace.ReadText("ai-bridge/index.xml");
        ScenarioAssert.Contains(
            "Entry point of the application",
            index,
            "Migrate must preserve existing index purposes.");
        ScenarioAssert.Contains(
            "Provides greeting logic",
            index,
            "Migrate must preserve all existing index purposes.");

        // state.xml must be updated
        ScenarioAssert.FileExists(workspace.PathFor("ai-bridge/state.xml"));
    }

    private static async Task MigrateReExtractsTemplatesAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("migrate re-extracts");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        // Simulate a user accidentally deleting a template folder
        var autoIndexDir = workspace.PathFor("ai-bridge/AutoIndexMode");
        if (Directory.Exists(autoIndexDir))
            Directory.Delete(autoIndexDir, recursive: true);

        // Simulate a stale file left by an older version inside SimpleMode
        workspace.WriteText("ai-bridge/1-SimpleMode/stale-old-instruction.md", "# Stale content from v0.1");

        var result = await context.Cli.RunAsync(workspace, "migrate");

        ScenarioAssert.Equal(0, result.ExitCode, "Migrate should succeed.");

        // Deleted folder must be restored
        ScenarioAssert.DirectoryExists(autoIndexDir);

        // Legacy folders must be deleted and not re-extracted
        ScenarioAssert.False(
            Directory.Exists(workspace.PathFor("ai-bridge/1-SimpleMode")),
            "1-SimpleMode should be deleted on migrate and not re-extracted.");
        ScenarioAssert.False(
            Directory.Exists(workspace.PathFor("ai-bridge/2-AdvancedMode")),
            "2-AdvancedMode should be deleted on migrate and not re-extracted.");

        // Stale file from old version must be gone (AutoIndexMode is wiped before re-extraction)
        ScenarioAssert.FileDoesNotExist(workspace.PathFor("ai-bridge/1-SimpleMode/stale-old-instruction.md"));

        // ai-response.xml must never be touched by migrate
        ScenarioAssert.Contains(
            "Paste the AI response XML here",
            workspace.ReadText("ai-bridge/artifacts/ai-response.xml"),
            "Migrate must not overwrite ai-response.xml.");

        // .aiignore must not be overwritten
        ScenarioAssert.FileExists(workspace.PathFor(".aiignore"));
    }

    private static async Task PackCreatesFullContextAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("pack full");
        await workspace.CreateDotNetDummyProjectAsync();

        // Stage all files so git ls-files --cached returns them
        await workspace.RunGitAsync("add", ".");
        await context.Cli.RunAsync(workspace, "init");

        var result = await context.Cli.RunAsync(workspace, "pack");

        ScenarioAssert.Equal(0, result.ExitCode, "Pack should succeed.");
        var contextText = workspace.ReadAllContextFiles();

        ScenarioAssert.Contains("<module", contextText, "Context should contain module XML.");
        ScenarioAssert.Contains("Program.cs", contextText, "Context should include Program.cs.");
        ScenarioAssert.Contains("GreetingService.cs", contextText, "Context should include service file.");

        // ai-bridge workspace files must never appear in pack output
        ScenarioAssert.DoesNotContain("ai-bridge/state.xml", contextText, "Pack must exclude AI Bridge workspace files.");
        ScenarioAssert.DoesNotContain("ai-bridge/index.xml", contextText, "Pack must exclude index.xml.");
    }

    private static async Task PackFailsWhenNotInitializedAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("pack not initialized");
        await workspace.CreateDotNetDummyProjectAsync();
        // Deliberately skip init

        var result = await context.Cli.RunAsync(workspace, "pack");

        ScenarioAssert.NotEqual(0, result.ExitCode, "Pack should fail when workspace is not initialized.");
        ScenarioAssert.Contains("not initialized", result.CombinedOutput, "Pack should tell user to run init.");
    }

    private static async Task PackFailsOnVersionMismatchAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("pack version mismatch");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        // Simulate a stale version stamp
        workspace.WriteText("ai-bridge/state.xml", """
        <ai-bridge-state version="0.0.0" />
        """);

        var result = await context.Cli.RunAsync(workspace, "pack");

        ScenarioAssert.NotEqual(0, result.ExitCode, "Pack should fail on version mismatch.");
        ScenarioAssert.Contains("migrate", result.CombinedOutput, "Pack should tell user to run migrate.");
    }

    private static async Task PackRespectsIgnoresAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("pack ignores");
        await workspace.CreateDotNetDummyProjectAsync();

        // Stage all files before init so git ls-files reflects them
        await workspace.RunGitAsync("add", ".");
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

    private static async Task ApplyFailsWhenNotInitializedAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("apply not initialized");
        await workspace.CreateDotNetDummyProjectAsync();
        // Deliberately skip init

        var result = await context.Cli.RunAsync(workspace, "apply");

        ScenarioAssert.NotEqual(0, result.ExitCode, "Apply should fail when workspace is not initialized.");
        ScenarioAssert.Contains("not initialized", result.CombinedOutput, "Apply should tell user to run init.");
    }

    private static async Task ApplyFailsOnVersionMismatchAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("apply version mismatch");
        await workspace.CreateDotNetDummyProjectAsync();
        await context.Cli.RunAsync(workspace, "init");

        // Simulate a stale version stamp
        workspace.WriteText("ai-bridge/state.xml", """
        <ai-bridge-state version="0.0.0" />
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");

        ScenarioAssert.NotEqual(0, result.ExitCode, "Apply should fail on version mismatch.");
        ScenarioAssert.Contains("migrate", result.CombinedOutput, "Apply should tell user to run migrate.");
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

    private static async Task RequestOutOfSyncIndexAsync(ScenarioContext context)
    {
        using var workspace = context.CreateWorkspace("out of sync index");
        await workspace.CreateDotNetDummyProjectAsync();
        await workspace.RunGitAsync("add", ".");
        await context.Cli.RunAsync(workspace, "init");

        // Write an index that is deliberately stale:
        // - Program.cs is indexed but has an empty purpose (needs filling)
        // - Services/GreetingService.cs is missing entirely (new unindexed file)
        // - OldFile.cs is indexed but does not exist on disk (deleted file)
        workspace.WriteText("ai-bridge/index.xml", """
        <ai-bridge-index lastUpdated="2000-01-01T00:00:00.0000000Z">
          <module name="DummyApp">
            <file path="Program.cs" purpose="" />
            <file path="OldFile.cs" purpose="This file no longer exists" />
          </module>
        </ai-bridge-index>
        """);

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-request>
          <out-of-sync-index-files />
        </ai-request>
        """);

        var result = await context.Cli.RunAsync(workspace, "apply");
        var requestedContext = workspace.ReadText("ai-bridge/artifacts/ai-requested-context.txt");

        ScenarioAssert.Equal(0, result.ExitCode, "Out-of-sync request should succeed.");

        // Empty-purpose file should be returned for the AI to fill in
        ScenarioAssert.Contains(
            "Program.cs",
            requestedContext,
            "Out-of-sync response should include file with empty purpose.");

        // New unindexed file should be flagged
        ScenarioAssert.Contains(
            "GreetingService.cs",
            requestedContext,
            "Out-of-sync response should flag new unindexed file.");

        // Deleted file that is still in the index should be flagged
        ScenarioAssert.Contains(
            "OldFile.cs",
            requestedContext,
            "Out-of-sync response should flag deleted file still in index.");
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

        // Verify the process is still alive before we write the response
        ScenarioAssert.False(
            process.HasExited,
            "Watch process should still be running before response is written.");

        workspace.WriteText("ai-bridge/artifacts/ai-response.xml", """
        <ai-response>
          <ai-edits>
            <file path="Watched.cs">public class Watched { }</file>
          </ai-edits>
        </ai-response>
        """);

        // Wait for the file to appear
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && !File.Exists(workspace.PathFor("Watched.cs")))
            await Task.Delay(100);

        // Verify process is still alive after processing (watch should keep running)
        ScenarioAssert.False(
            process.HasExited,
            "Watch process should still be running after processing the response.");

        ScenarioAssert.FileExists(workspace.PathFor("Watched.cs"));

        // Verify response file was reset after the watch cycle
        ScenarioAssert.Contains(
            "Paste the AI response XML here",
            workspace.ReadText("ai-bridge/artifacts/ai-response.xml"),
            "Watch should reset the response file after applying.");
    }
}
