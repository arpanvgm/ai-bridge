---
name: dotnet-cli-tool-best-practices
description: >
  Best practices for building .NET CLI tools using C#. Covers argument parsing
  with System.CommandLine, exit codes, stdout/stderr separation, help text,
  cancellation, watch modes, clipboard integration, output formatting, and
  CLI-specific architecture patterns. Activate this skill when building,
  reviewing, or extending any .NET CLI tool or console application.
---

# .NET CLI Tool Best Practices Skill

Apply these practices when building, reviewing, or extending any .NET-based CLI tool.
These rules are specific to command-line tools — for general .NET C# practices,
see the `dotnet-csharp-best-practices` skill.

---

## 1. Argument Parsing — Use System.CommandLine

### 1.1 Never Hand-Roll Argument Parsing

A hand-rolled `switch` on `args[0]` with manual flag parsing is the #1 architectural
mistake in .NET CLI tools. It doesn't scale, produces no `--help`, has no validation,
and breaks conventions. Use **`System.CommandLine`** (Microsoft's official CLI library).

```xml
<!-- Add to .csproj -->
<PackageReference Include="System.CommandLine" Version="2.*" />
```

### 1.2 System.CommandLine Setup Pattern

```csharp
using System.CommandLine;

var rootCommand = new RootCommand("My CLI tool description");

// Define commands with descriptions
var packCommand = new Command("pack", "Packs source files into context for AI.");
var incrementalOption = new Option<bool>(
    "--incremental",
    "Pack only files modified since the last update.");
packCommand.AddOption(incrementalOption);

packCommand.SetHandler((bool incremental) =>
{
    PackCommand.Run(incremental);
}, incrementalOption);

rootCommand.AddCommand(packCommand);

// Run — handles --help, --version, errors, exit codes automatically
return await rootCommand.InvokeAsync(args);
```

### 1.3 What System.CommandLine Gives You for Free

- `--help` auto-generated for every command and subcommand
- `--version` auto-generated from assembly version
- Argument validation and type conversion
- Tab completion (`dotnet-suggest` integration)
- Proper exit codes (0 on success, 1 on error)
- Consistent error formatting to stderr
- Middleware pipeline (for cross-cutting concerns like workspace resolution)

### 1.4 Command Hierarchy — Verb-Noun Pattern

Design your command hierarchy to be intuitive:

```
mytool init                    # Setup command
mytool pack [--incremental]    # Action with options
mytool apply [--paste] [--dry-run] [--watch]
mytool index                   # Display subcommand
mytool index status            # Subcommand (prefer over --status flag)
```

Prefer subcommands (`index status`) over flags (`index --status`) when the
flag fundamentally changes what the command does.

---

## 2. Exit Codes — Non-Negotiable

### 2.1 Always Return Meaningful Exit Codes

`Main` must return `int` (or `Task<int>`). Exit code 0 = success, non-zero = failure.
Every script, CI pipeline, and `&&` chain depends on this.

```csharp
// ❌ CRITICAL BUG — always returns 0, even on failure
static void Main(string[] args) { /* ... */ }

// ✅ Returns exit codes
static int Main(string[] args)
{
    try
    {
        // ... command dispatch ...
        return 0; // Success
    }
    catch (ValidationException)
    {
        return 1; // User/input error
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Fatal: {ex.Message}");
        return 2; // Runtime/unexpected error
    }
}
```

### 2.2 Standard Exit Code Conventions

| Code | Meaning | When to Use |
|------|---------|-------------|
| `0` | Success | Command completed successfully |
| `1` | General error | Validation failure, missing files, bad input |
| `2` | Misuse | Unknown command, invalid flag combinations |
| `130` | Interrupted | User pressed Ctrl+C (convention: 128 + signal number) |

---

## 3. stdout vs stderr — The Fundamental CLI Rule

### 3.1 Normal Output → stdout, Errors/Diagnostics → stderr

This is the most violated CLI rule. If you get this wrong, piping,
redirection, and scripting break. The rule is simple:

- **stdout** (`Console.WriteLine`): Output the user asked for — results, data, content
- **stderr** (`Console.Error.WriteLine`): Errors, warnings, progress, diagnostics

```csharp
// ❌ BROKEN — errors go to stdout, polluting piped output
public static void Error(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(message);      // Goes to stdout!
    Console.ResetColor();
}

// ✅ CORRECT — errors go to stderr
public static void Error(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(message); // Goes to stderr
    Console.ResetColor();
}
```

### 3.2 Why This Matters — Real Example

```bash
# User wants to process the output of your tool:
mytool pack > context.txt

# If errors go to stdout, the error message ends up IN context.txt
# and the user sees nothing in the terminal. This is a data corruption bug.
```

### 3.3 ConsoleHelper Pattern for CLI Tools

```csharp
namespace MyTool.Helpers;

public static class ConsoleHelper
{
    // These go to STDERR — they are diagnostics, not output
    public static void Error(string message) => WriteToStderr(message, ConsoleColor.Red);
    public static void Warning(string message) => WriteToStderr(message, ConsoleColor.Yellow);
    public static void Info(string message) => WriteToStderr(message, ConsoleColor.Cyan);
    public static void Success(string message) => WriteToStderr(message, ConsoleColor.Green);

    // This goes to STDOUT — it is the tool's output/data
    public static void Output(string message) => Console.WriteLine(message);

    private static void WriteToStderr(string message, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = prev;
    }
}
```

---

## 4. Essential Flags Every CLI Tool Should Have

### 4.1 Must-Have Flags

| Flag | Purpose | Notes |
|------|---------|-------|
| `--help`, `-h` | Show usage and options | Free with System.CommandLine |
| `--version` | Print tool version | Free with System.CommandLine |
| `--verbose`, `-v` | Increase output detail | Log file sizes, timings, debug info |
| `--quiet`, `-q` | Suppress non-error output | Only errors go to stderr |

### 4.2 High-Value Optional Flags

| Flag | Purpose | When to Add |
|------|---------|-------------|
| `--dry-run` | Show what would happen without doing it | Any command with side effects (writes, deletes) |
| `--output json` | Machine-readable output | When your tool's output might be piped |
| `--no-color` | Disable colored output | For CI/piped environments |
| `--config <path>` | Custom config file location | When you support config files |

### 4.3 Implementing `--dry-run`

```csharp
public static void Apply(string projectRoot, bool dryRun = false)
{
    foreach (var file in filesToCreate)
    {
        if (dryRun)
        {
            ConsoleHelper.Info($"[dry-run] Would create: {file.Path}");
            continue;
        }
        File.WriteAllText(file.Path, file.Content);
        ConsoleHelper.Success($"Created: {file.Path}");
    }

    foreach (var file in filesToDelete)
    {
        if (dryRun)
        {
            ConsoleHelper.Info($"[dry-run] Would delete: {file}");
            continue;
        }
        File.Delete(file);
        ConsoleHelper.Success($"Deleted: {file}");
    }
}
```

---

## 5. Cancellation & Long-Running Operations

### 5.1 Use CancellationToken for Ctrl+C

The idiomatic .NET pattern for handling Ctrl+C is `CancellationTokenSource`:

```csharp
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;   // Prevent immediate termination
    cts.Cancel();       // Signal cancellation
};

try
{
    await LongRunningOperation(cts.Token);
}
catch (OperationCanceledException)
{
    ConsoleHelper.Info("Operation cancelled.");
    return 130; // Standard exit code for Ctrl+C
}
```

### 5.2 Watch Mode — Timer-Based Debounce

When implementing `--watch` with `FileSystemWatcher`, never use `Thread.Sleep`
for debouncing — it blocks the callback thread and can cause missed events.
Use a timer-based pattern:

```csharp
// ❌ Blocks the FileSystemWatcher callback thread
void OnChanged(object sender, FileSystemEventArgs e)
{
    Thread.Sleep(500); // BLOCKS — can miss events
    ProcessFile(e.FullPath);
}

// ✅ Timer-based debounce — non-blocking
private Timer? _debounceTimer;

void OnChanged(object sender, FileSystemEventArgs e)
{
    _debounceTimer?.Dispose();
    _debounceTimer = new Timer(_ =>
    {
        ProcessFile(e.FullPath);
    }, null, dueTime: 500, period: Timeout.Infinite);
}
```

### 5.3 FileSystemWatcher Best Practices

```csharp
using var watcher = new FileSystemWatcher(watchDirectory)
{
    Filter = "*.xml",
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
    EnableRaisingEvents = true,
    // Important: set buffer size if monitoring many files
    InternalBufferSize = 64 * 1024 // 64KB
};

// Handle BOTH Changed and Created — editors differ in behavior
watcher.Changed += OnChanged;
watcher.Created += OnChanged;
```

---

## 6. Clipboard Integration (Cross-Platform)

### 6.1 Platform Detection Pattern

```csharp
private enum ClipboardPlatform { Windows, MacOS, Wsl2, LinuxWayland, LinuxX11, Unsupported }

private static ClipboardPlatform DetectPlatform()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return ClipboardPlatform.Windows;

    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return ClipboardPlatform.MacOS;

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        // WSL2 check must come before Wayland/X11
        if (File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop"))
            return ClipboardPlatform.Wsl2;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            return ClipboardPlatform.LinuxWayland;

        return ClipboardPlatform.LinuxX11;
    }

    return ClipboardPlatform.Unsupported;
}
```

### 6.2 Clipboard Read/Write Tool Mapping

| Platform | Read Command | Write Command |
|----------|-------------|---------------|
| Windows | `powershell.exe -NoProfile -Command Get-Clipboard` | `clip.exe` (via stdin) |
| macOS | `pbpaste` | `pbcopy` (via stdin) |
| WSL2 | `powershell.exe -NoProfile -Command Get-Clipboard` | `clip.exe` (via stdin) |
| Linux (Wayland) | `wl-paste --no-newline` | `wl-copy` (via stdin) |
| Linux (X11) | `xclip -selection clipboard -o` | `xclip -selection clipboard` (via stdin) |

### 6.3 Always Provide a Stdin Fallback

Clipboard access can fail (headless servers, SSH, disabled interop). Always
fall back to reading from stdin so the tool works everywhere:

```csharp
string? content = null;
try
{
    content = ClipboardHelper.GetText();
}
catch
{
    // Clipboard unavailable — will prompt for stdin
}

if (string.IsNullOrWhiteSpace(content))
{
    ConsoleHelper.Info("Paste content below and press Enter:");
    content = ReadFromStdin();
}
```

---

## 7. Output Formatting & UX

### 7.1 Consistent Output Format

Pick one output style and use it everywhere:

```csharp
// ✅ Consistent emoji-prefixed status messages
ConsoleHelper.Success("✅ Created: src/services/UserService.cs");
ConsoleHelper.Warning("⚠ Skipped: binary file detected");
ConsoleHelper.Error("❌ Failed: patch could not be applied");
ConsoleHelper.Info("ℹ Using git for file discovery...");
```

### 7.2 Summary After Operations

Always print a summary after batch operations:

```csharp
ConsoleHelper.Info($"\nSummary: {created} created, {patched} patched, {deleted} deleted.");
if (failed > 0)
{
    ConsoleHelper.Error($"Failed: {failed} operation(s). See details above.");
}
```

### 7.3 Respect `NO_COLOR` Environment Variable

Follow the [no-color.org](https://no-color.org/) convention:

```csharp
private static readonly bool UseColor =
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
    && !Console.IsOutputRedirected;
```

### 7.4 Token/Size Estimation Display

For tools that produce AI context or large text outputs, show size metrics:

```csharp
var fileSizeKB = Math.Round(new FileInfo(outputPath).Length / 1024.0, 1);
var approxTokens = content.Length / 4; // rough heuristic
ConsoleHelper.Success($"Packed ({fileCount} files, {fileSizeKB} KB, ~{approxTokens:N0} tokens)");
```

---

## 8. CLI Architecture Patterns

### 8.1 Command Structure

Each command should be its own class implementing a handler interface:

```csharp
// With System.CommandLine, commands are self-contained:
public static class PackCommandSetup
{
    public static Command Create()
    {
        var command = new Command("pack", "Pack source files into AI context.");
        var incrementalOption = new Option<bool>("--incremental", "Pack only changed files.");
        var dryRunOption = new Option<bool>("--dry-run", "Show what would be packed without writing.");

        command.AddOption(incrementalOption);
        command.AddOption(dryRunOption);

        command.SetHandler(PackCommand.Run, incrementalOption, dryRunOption);
        return command;
    }
}
```

### 8.2 Workspace Resolution as Middleware

Don't resolve the project root in every command. Use middleware or resolve once:

```csharp
// Middleware pattern with System.CommandLine
rootCommand.AddGlobalOption(projectRootOption);

// Or resolve once and pass through
var projectRoot = WorkspaceHelper.GetProjectRoot();
// Thread this through to all commands
```

### 8.3 State Files — Keep Them Simple

If your CLI needs to persist state between runs (version, timestamps, config):

```xml
<!-- Simple XML state file -->
<tool-state
  version="1.0.7"
  lastRunAt="2026-07-01T05:00:00Z"
  ecosystem="dotnet" />
```

Rules for state files:
- Store in the tool's workspace directory, not the project root
- Add to `.gitignore`
- Validate on read — never assume the file is well-formed
- Use ISO 8601 for timestamps with explicit UTC (`"o"` format specifier)

### 8.4 Version Checking Pattern

Check that local state/templates match the installed tool version:

```csharp
public static bool EnsureUpToDate()
{
    var localVersion = LoadState()?.Version ?? "";
    var currentVersion = GetCurrentVersion();

    if (localVersion != currentVersion)
    {
        ConsoleHelper.Warning(
            $"Version mismatch: tool is {currentVersion}, local state is {localVersion}.");
        ConsoleHelper.Info("Run 'mytool update' to sync.");
        return false;
    }
    return true;
}
```

---

## 9. Security Considerations for CLI Tools

### 9.1 Path Traversal — Validate All External Paths

Any path received from external input (XML, JSON, user args, clipboard) MUST
be validated to stay within the expected directory:

```csharp
public static string SafeResolvePath(string projectRoot, string relativePath)
{
    var resolved = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
    if (!resolved.StartsWith(Path.GetFullPath(projectRoot), StringComparison.OrdinalIgnoreCase))
    {
        throw new SecurityException(
            $"Path '{relativePath}' resolves outside project root. Blocked.");
    }
    return resolved;
}
```

### 9.2 Never Embed Secrets in Code or State Files

If your tool handles API keys, tokens, or credentials:
- Use environment variables or OS credential stores
- Never write secrets to log files or state files
- Mask secrets in error messages

### 9.3 Process Execution Safety

When spawning child processes (e.g., `git`, clipboard tools):
- Never pass unsanitized user input as process arguments
- Set `UseShellExecute = false` to avoid shell injection
- Set `CreateNoWindow = true` to avoid UI popups
- Always read stderr to diagnose failures

---

## 10. Publishing as a .NET Global Tool

### 10.1 Required .csproj Properties

```xml
<PropertyGroup>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>my-tool</ToolCommandName>
    <PackageId>MyOrg.MyTool</PackageId>
    <Version>1.0.0</Version>
    <Description>A tool that does X for Y users.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <RepositoryUrl>https://github.com/org/repo</RepositoryUrl>
</PropertyGroup>
```

### 10.2 Embedding Templates/Resources

If your tool includes template files that get extracted at runtime:

```xml
<ItemGroup>
    <Content Include="Templates\**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Access at runtime via:
```csharp
var templateDir = Path.Combine(AppContext.BaseDirectory, "Templates");
```

### 10.3 Installation and Update Commands

```bash
# Install
dotnet tool install --global MyOrg.MyTool

# Update
dotnet tool update --global MyOrg.MyTool

# Uninstall
dotnet tool uninstall --global MyOrg.MyTool
```

---

## 11. Testing CLI Tools

### 11.1 Integration Test Pattern

```csharp
[Fact]
public void Pack_ProducesContextFiles()
{
    // Arrange — set up a temp project directory
    using var tempDir = new TempDirectory();
    File.WriteAllText(Path.Combine(tempDir, "Program.cs"), "class Program {}");

    // Act — run the command programmatically (not via Process)
    var result = PackCommand.Run(tempDir.Path, incremental: false);

    // Assert
    Assert.Equal(0, result.ExitCode);
    Assert.True(File.Exists(Path.Combine(tempDir, "ai-bridge/artifacts/root-context.txt")));
}
```

### 11.2 Making Commands Testable

Separate the "what to do" from "how to do I/O":

```csharp
// ✅ The logic method takes inputs and returns a result object — no I/O
public record PackResult(int FileCount, string OutputContent, List<string> Warnings);

public static PackResult BuildPack(IEnumerable<string> files, string rootPath, PackOptions options)
{
    // Pure logic — fully testable
}

// The command handler does the I/O
public static int Run(string projectRoot, bool incremental)
{
    var files = Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories);
    var result = BuildPack(files, projectRoot, new PackOptions(incremental));
    File.WriteAllText(outputPath, result.OutputContent);
    return 0;
}
```
