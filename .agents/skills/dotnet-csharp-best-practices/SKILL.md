---
name: dotnet-csharp-best-practices
description: >
  Modern .NET C# development best practices for any .NET project.
  Covers C# 13 / .NET 10+ language features, code quality, architecture patterns,
  error handling, testability, and maintainability. Activate this skill when writing,
  reviewing, or refactoring any .NET C# code — libraries, APIs, services, or tools.
---

# .NET C# Best Practices Skill

Apply these practices when writing, reviewing, or refactoring any .NET C# codebase.
These rules are grounded in real-world code review findings, not generic advice.

---

## 1. Modern C# Language Usage (C# 13 / .NET 10+)

### 1.1 File-Scoped Namespaces (ALWAYS)

Every `.cs` file must use file-scoped namespaces. Block-scoped namespaces add
an unnecessary indentation level to the entire file.

```csharp
// ❌ WRONG — block-scoped namespace
namespace MyApp.Services
{
    public class UserService
    {
        // entire class indented one level unnecessarily
    }
}

// ✅ CORRECT — file-scoped namespace
namespace MyApp.Services;

public class UserService
{
    // clean, no extra indentation
}
```

### 1.2 Top-Level Statements for Entry Points

Use top-level statements in `Program.cs` to reduce boilerplate.
Reserve the traditional `class Program` pattern only when you need
explicit `Main` method signatures (e.g., `async Task<int> Main`).

```csharp
// ✅ Clean entry point
var builder = WebApplication.CreateBuilder(args);
// ...
app.Run();
```

### 1.3 Primary Constructors

Use primary constructors on classes and structs when the constructor
only assigns parameters to fields/properties.

```csharp
// ❌ Verbose
public class OrderService
{
    private readonly IOrderRepository _repo;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IOrderRepository repo, ILogger<OrderService> logger)
    {
        _repo = repo;
        _logger = logger;
    }
}

// ✅ Primary constructor
public class OrderService(IOrderRepository repo, ILogger<OrderService> logger)
{
    public async Task<Order> GetAsync(int id) => await repo.FindAsync(id);
}
```

### 1.4 Records for Immutable Data

Use `record` for DTOs, value objects, and any type that represents immutable data.
Use `record struct` for small value types that benefit from value semantics.

```csharp
// ✅ Positional record — immutable, has Equals/GetHashCode/ToString for free
public record ProjectInfo(string Name, string DirectoryPrefix);

// ✅ Record struct for small, frequent allocations
public readonly record struct FileEntry(string Path, int LineCount);
```

**Critical rule:** Always declare records inside their namespace block (or after
a file-scoped namespace declaration). Never outside a namespace — this puts the
type in the global namespace and causes subtle bugs.

### 1.5 Collection Expressions

Use collection expressions (`[...]`) instead of `new[]`, `new List<>()`,
`new HashSet<>()`, etc. when initializing collections.

```csharp
// ❌ Old style
private static readonly string[] Prefixes = new[] { "api", "web", "lib" };
private static readonly List<string> Exclusions = new() { ".git", ".vs" };

// ✅ Collection expressions
private static readonly string[] Prefixes = ["api", "web", "lib"];
private static readonly List<string> Exclusions = [".git", ".vs"];
```

### 1.6 Pattern Matching

Use pattern matching instead of long `if/else` chains or manual type checks.

```csharp
// ❌ Verbose
if (root.Name != "response" && root.Name != "request" && root.Name != "index")
{
    // error
}

// ✅ Pattern matching (works with const string values)
if (root.Name is not ("response" or "request" or "index"))
{
    // error
}

// ✅ Switch expressions for mapping
var color = severity switch
{
    "error" => ConsoleColor.Red,
    "warning" => ConsoleColor.Yellow,
    "info" => ConsoleColor.Cyan,
    _ => ConsoleColor.White
};
```

### 1.7 Nullable Reference Types

With `<Nullable>enable</Nullable>` in the `.csproj`:
- Never abuse the `!` (null-forgiving) operator. Each `!` is a suppressed compiler warning.
- Use `?.`, `??`, and `is not null` patterns instead.
- Only use `!` when you have an invariant the compiler can't prove (and add a comment explaining why).

```csharp
// ❌ Suppression abuse
var dir = Path.GetDirectoryName(path)!;

// ✅ Guard explicitly
var dir = Path.GetDirectoryName(path)
    ?? throw new InvalidOperationException($"Cannot get directory for: {path}");
```

### 1.8 Remove Redundant Using Directives

When `<ImplicitUsings>enable</ImplicitUsings>` is set, `System`, `System.IO`,
`System.Linq`, `System.Collections.Generic`, `System.Threading.Tasks`, and others
are automatically imported. Do not re-declare them in each file.

---

## 2. Code Quality & Maintainability

### 2.1 Never Swallow Exceptions Silently

Bare `catch { }` blocks hide bugs. At minimum:
- Catch `Exception` explicitly (not bare `catch`)
- Log or trace the error, even if the flow intentionally continues
- Add a comment explaining WHY the exception is being suppressed

```csharp
// ❌ Silent swallow — hides bugs
catch { }

// ✅ Intentional with documentation
catch (Exception ex)
{
    // Git may not be installed; graceful fallback to heuristic file discovery.
    Debug.WriteLine($"Git not available: {ex.Message}");
    return null;
}
```

### 2.2 Avoid Reading Files Multiple Times

Never read a file with one API and then read it again with another to extract
different information. Read once, process the in-memory content.

```csharp
// ❌ Double I/O
var content = File.ReadAllText(file).TrimEnd();
var lineCount = File.ReadLines(file).Count(); // reads the file AGAIN

// ✅ Single read
var content = File.ReadAllText(file).TrimEnd();
var lineCount = content.AsSpan().Count('\n') + 1;
```

### 2.3 Use `string.Replace` Carefully

`string.Replace(oldValue, newValue)` replaces ALL occurrences. If you intend
to replace only the first occurrence (e.g., patching code), you must use a
targeted approach:

```csharp
// ❌ Replaces ALL matches — can corrupt file with duplicate patterns
var updated = content.Replace(search, replace);

// ✅ Replace only the first occurrence
var index = content.IndexOf(search, StringComparison.Ordinal);
if (index >= 0)
{
    var updated = string.Concat(
        content.AsSpan(0, index),
        replace,
        content.AsSpan(index + search.Length));
}
```

### 2.4 Validate Paths Against Directory Traversal

When constructing file paths from external input (user input, API responses,
config files, XML attributes), ALWAYS validate the resolved path stays within
the expected root directory.

```csharp
// ❌ Unsafe — input could be "../../../etc/passwd"
var absPath = Path.Combine(projectRoot, userProvidedPath);

// ✅ Safe — validate resolved path is within bounds
var absPath = Path.GetFullPath(Path.Combine(projectRoot, userProvidedPath));
if (!absPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
{
    throw new SecurityException($"Path '{userProvidedPath}' escapes project root.");
}
```

### 2.5 Use `TryGetValue` Instead of `ContainsKey` + Index

Avoid the double-lookup pattern with dictionaries.

```csharp
// ❌ Double lookup
if (!dict.ContainsKey(key))
{
    dict[key] = new StringBuilder();
}
dict[key].Append(value);

// ✅ Single lookup
if (!dict.TryGetValue(key, out var sb))
{
    sb = new StringBuilder();
    dict[key] = sb;
}
sb.Append(value);
```

### 2.6 Eliminate Code Duplication with Parameterized Methods

When you have 3+ code blocks that differ only in a filename, label, or mapping
function, extract a parameterized helper method.

```csharp
// ❌ Five nearly-identical blocks for ecosystem detection
var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
if (csprojFiles.Length > 0) { /* map, sort, return */ }
var packageFiles = Directory.GetFiles(path, "package.json", SearchOption.AllDirectories);
if (packageFiles.Length > 0) { /* same logic, different name */ }
// ... repeated 3 more times ...

// ✅ Single parameterized method
private static List<ProjectInfo>? TryDetect(string root, string marker, Func<string, string> nameSelector)
{
    var files = Directory.GetFiles(root, marker, SearchOption.AllDirectories);
    if (files.Length == 0) return null;
    return files.Select(f => new ProjectInfo(nameSelector(f), Path.GetDirectoryName(f)!)).ToList();
}
```

### 2.7 Platform-Aware Line Endings

Never hardcode `\r\n` or `\n`. Use `Environment.NewLine` for platform-appropriate
line endings, or detect and preserve the existing file's convention.

```csharp
// ❌ Forces Windows line endings on Linux/macOS
var content = text.Trim('\r', '\n') + "\r\n";

// ✅ Platform-appropriate
var content = text.TrimEnd() + Environment.NewLine;
```

---

## 3. Architecture & Structure

### 3.1 Folder Layout for .NET Projects

Maintain a clean, conventional folder structure:

```
ProjectName/
├── ProjectName.csproj
├── Program.cs              # Entry point (minimal)
├── Commands/               # Command definitions and handlers
├── Core/ or Services/      # Business logic, domain services
├── Models/                 # Data models, DTOs, records
├── Helpers/ or Utilities/  # Cross-cutting utilities
├── Constants/              # String/numeric constants
└── Extensions/             # Extension methods
```

### 3.2 Resolve Expensive Computations Once

When a value requires traversal, I/O, or computation (e.g., finding the project
root by walking up the directory tree), resolve it once at the entry point and
pass it through as a parameter. Do not recompute it in every method.

```csharp
// ❌ Called in every command, every helper, every handler
var projectRoot = WorkspaceHelper.GetProjectRoot(); // walks directory tree

// ✅ Resolve once, pass through
// In Program.cs:
var projectRoot = WorkspaceHelper.GetProjectRoot();
PackCommand.Run(projectRoot, incremental: true);
```

### 3.3 Separate I/O from Logic for Testability

Keep business logic (parsing, validation, transformation) in pure methods
that take inputs and return outputs. Push I/O (file reads, console writes,
process execution) to the edges.

```csharp
// ❌ Logic + I/O intertwined — untestable
public static void Pack()
{
    var files = Directory.GetFiles(...);    // I/O
    var filtered = files.Where(...);       // Logic
    File.WriteAllText(output, ...);         // I/O
    Console.WriteLine("Done");             // I/O
}

// ✅ Separated — the logic method is testable
public static string BuildPackedContent(IEnumerable<string> files, string rootPath)
{
    // Pure logic: filter, transform, build output string
    // No I/O here — fully unit testable
}
```

### 3.4 XML: Prefer XDocument over XmlDocument

`System.Xml.Linq.XDocument` provides a cleaner, more LINQ-friendly API than the
legacy `System.Xml.XmlDocument` DOM API. Use it for new code.

```csharp
// ❌ Legacy XmlDocument
var xml = new XmlDocument();
xml.Load(file);
var nodes = xml.SelectNodes("//file[@path]");

// ✅ Modern XDocument
var xml = XDocument.Load(file);
var paths = xml.Descendants("file")
    .Select(e => e.Attribute("path")?.Value)
    .Where(p => p is not null);
```

---

## 4. Process Execution Safety

### 4.1 Prevent Deadlocks on Process Output

When redirecting both stdout and stderr from a child process, never call
`ReadToEnd()` on one stream and then `WaitForExit()` — this can deadlock
if the process writes enough to fill the OS pipe buffer.

```csharp
// ❌ Can deadlock if output exceeds buffer size
var output = process.StandardOutput.ReadToEnd();
process.WaitForExit();

// ✅ Safe — use async reads or read both streams
var outputTask = process.StandardOutput.ReadToEndAsync();
var errorTask = process.StandardError.ReadToEndAsync();
process.WaitForExit();
var output = await outputTask;
var error = await errorTask;
```

---

## 5. Project Configuration (.csproj)

### 5.1 Always Enable These Properties

```xml
<PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
</PropertyGroup>
```

### 5.2 Consider AOT for CLI Tools and Libraries

.NET 10 has mature Native AOT support. If your project has:
- No dynamic reflection
- No runtime code generation
- No unsupported NuGet dependencies

Then enable AOT for dramatically faster startup:
```xml
<PublishAot>true</PublishAot>
```

---

## 6. Documentation

### 6.1 XML Doc Comments on Public APIs

Every `public` class, method, and property should have `<summary>` XML docs.
Internal/private members need docs only when the purpose isn't obvious.

### 6.2 Comment the WHY, Not the WHAT

```csharp
// ❌ Useless comment — restates the code
// Set the color to red
Console.ForegroundColor = ConsoleColor.Red;

// ✅ Useful comment — explains WHY
// Errors must be visually distinct from normal output because users
// scan terminal output quickly during multi-step workflows.
Console.ForegroundColor = ConsoleColor.Red;
```
