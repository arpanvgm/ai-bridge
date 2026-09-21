
# AI Bridge — Core Library Workflows

`AIBridge.Core` is the shared engine. It has no knowledge of CLI commands or HTTP — it exposes services that any consumer can call. This document covers what each service does internally.

---

## WorkspaceValidator

**Purpose:** Read-only workspace state check. No side effects.

~~~mermaid
flowchart TD
    Start["WorkspaceValidator.Check(projectRoot)"]
    Start --> ReadState["Read ai-bridge/state.xml"]
    ReadState --> Missing{"File exists?"}
    Missing -- "No" --> NotInit["Return: NotInitialized"]
    Missing -- "Yes" --> Compare["Compare stamped version\nvs running assembly version"]
    Compare --> Match{"Versions match?"}
    Match -- "No" --> Mismatch["Return: VersionMismatch"]
    Match -- "Yes" --> Valid["Return: Valid"]
~~~

**Used by:** CLI (`pack`, `apply`), MCP (startup check). Neither consumer acts on the result — they each decide what to do based on the returned status.

---

## WorkspaceSetupService

**Purpose:** Unconditionally creates or restores the workspace. Never checks version — the caller decides when to invoke it.

~~~mermaid
flowchart TD
    Start["WorkspaceSetupService.SetupAsync(projectRoot)"]
    Start --> Artifacts["EnsureArtifactsFolderAsync\n• Create ai-bridge/artifacts/\n• Create ai-response.xml if missing"]
    Artifacts --> Gitignore["EnsureInnerGitignoreAsync\n• Always overwrite ai-bridge/.gitignore\n  (tool-owned, never user-edited)"]
    Gitignore --> Docker["EnsureDockerignoreAsync\n• Append ai-bridge/ to .dockerignore\n  only if not already present"]
    Docker --> AiIgnore["EnsureAiIgnoreAsync\n• Create .aiignore with default rules if missing\n• If exists: only append missing default rules\n  (never overwrite — user edits here)"]
    AiIgnore --> Templates["TemplateService.ExtractAll\n• Delete SimpleMode/, AdvancedMode/, AutoIndexMode/\n• Re-extract all embedded templates\n  (stale files from old versions cannot linger)"]
    Templates --> Index["IndexService.GenerateIndexAsync\n• Scan tracked files\n• Add new files with empty purpose\n• Preserve all existing purposes"]
    Index --> Done(("SetupAsync complete\nCaller stamps state.xml"))
~~~

**Safe to run repeatedly:** `ai-response.xml` and `.aiignore` are never overwritten. Index purposes are never cleared. Template folders are always wiped and re-extracted (guaranteed up-to-date).

---

## StateService

**Purpose:** Reads and writes `ai-bridge/state.xml` — the version stamp that tells the tool whether the workspace matches the running binary.

| Method | What it does |
|---|---|
| `GetCurrentVersion()` | Reads the running `AIBridge.Core` assembly version |
| `CheckState()` | Reads `state.xml`; returns `NotInitialized`, `Outdated`, or `UpToDate` |
| `InitState()` | Writes current version into `state.xml` |

`CheckState()` is called only by `WorkspaceValidator`. `InitState()` is called only after `SetupAsync` completes successfully.

---

## TemplateService

**Purpose:** Manages embedded Markdown skill/prompt files that are baked into the `AIBridge.Core` assembly.

~~~mermaid
flowchart TD
    Start["TemplateService.ExtractAll(targetDir, projectPath)"]
    Start --> DeleteFolders["Delete SimpleMode/, AdvancedMode/, AutoIndexMode/\n(removes stale files from old versions)"]
    DeleteFolders --> Enumerate["Enumerate all embedded resources\nunder AIBridge.Core.Templates.*"]
    Enumerate --> Loop["For each resource"]
    Loop --> Convert["Convert resource name → relative file path\n(un-mangle .NET naming: underscores → hyphens in folder names)"]
    Convert --> Write["Create directories and write file\n(always overwrites)"]
    Write --> Loop
    Loop --> Done(("All templates extracted"))
~~~

**Note:** `ExtractAll` is the only public method. It always overwrites. There is no partial-restore variant — a full re-extraction is always done on setup/migrate to guarantee correctness.

---

## PackerService

**Purpose:** Reads tracked source files, filters ignored items, and bundles everything into `*-context.txt` files for the AI to consume.

~~~mermaid
flowchart TD
    Start["PackerService.PackAsync(projectRoot)"]
    Start --> GetFiles["FileFilterHelper.GetTrackedFilesAsync\n• Run git ls-files --cached --others\n• Fallback: recursive directory walk"]
    GetFiles --> LoadIgnore["Load .aiignore rules"]
    LoadIgnore --> Detect["ProjectDetector.DetectProjects\n• Find .csproj / package.json /\n  pyproject.toml / go.mod / Cargo.toml\n• Group files by project name"]
    Detect --> Loop["For each tracked file"]
    Loop --> Filter{"Filter checks"}
    Filter -- "Skip" --> SkipReasons["• Path starts with ai-bridge/\n• Binary extension\n• Excluded filename\n• .aiignore match"]
    Filter -- "Include" --> Read["Read file content"]
    Read --> Wrap["Wrap: <file path='...' lines='N'>content</file>"]
    Wrap --> Group["Add to project group StringBuilder"]
    Group --> Loop
    Loop --> Output["For each project group:\nWrap in <module name='...' files='N'>\nWrite {ProjectName}-context.txt\nLog: file count, KB, token estimate"]
    Output --> Done(("Return PackResult"))
~~~

---

## ApplyService

**Purpose:** Parses AI-generated XML and applies it to the codebase. Handles both `<ai-request>` (context fetch) and `<ai-response>` (code changes).

~~~mermaid
flowchart TD
    Start["ApplyService.ExecuteAsync(rawContent, projectRoot)"]
    Start --> Strip["Strip markdown fences if present\n(AI sometimes wraps output in ```xml```)"]
    Strip --> Parse{"Parse XML"}
    Parse -- "Invalid" --> Abort(("Abort — return failure\nno files touched"))
    Parse -- "Valid" --> Root{"Root element?"}

    Root -- "<ai-request>" --> RequestBranch["RequestService.HandleAsync\n(see RequestService below)"]
    RequestBranch --> ReturnContext(("Return ContextPayload\nto caller"))

    Root -- "<ai-response>" --> Validate["Validate child elements\n(only known tags allowed)"]
    Validate --> Edits["Process <ai-edits>"]

    Edits --> FileNodes["<file> nodes\n→ Write complete file to disk\n→ Create missing directories\n→ Trim + append single newline"]
    Edits --> PatchNodes["<patch> nodes\n→ PatcherService.ApplyPatchAsync\n(see PatcherService below)"]
    Edits --> DeleteNodes["<delete> nodes\n→ Delete file from disk\n→ Collect parent dir for cleanup"]

    FileNodes --> PatchCheck
    PatchNodes --> PatchCheck
    DeleteNodes --> PatchCheck

    PatchCheck{"Any patch failures?"}
    PatchCheck -- "Yes" --> LogFail["Log each failed file\nSet failure flag"]
    PatchCheck -- "No" --> CleanDirs["Clean empty directories\nleft by deletions"]

    LogFail --> CleanDirs
    CleanDirs --> IndexCheck{"<update-index> present\nAND no patch failures?"}
    IndexCheck -- "Yes" --> UpdateIndex["IndexService.HandleUpdate\n• Update/add purposes\n• Delete removed file entries\n• Remove empty modules"]
    IndexCheck -- "No (patch failed)" --> SkipIndex["Skip index update\n(codebase may be inconsistent)"]
    IndexCheck -- "No (tag missing)" --> WarnIndex["Log warning:\n'Edits made but no <update-index> found'"]

    UpdateIndex --> TrackerCheck
    SkipIndex --> TrackerCheck
    WarnIndex --> TrackerCheck

    TrackerCheck{"<tracker> present?"}
    TrackerCheck -- "Yes" --> Tracker["TrackerService.HandleTracker\n(see TrackerService below)"]
    TrackerCheck -- "No" --> Summary

    Tracker --> Summary["Log summary:\nN written, N patched, N deleted"]
    Summary --> Done(("Return ApplyResult"))
~~~

---

## PatcherService

**Purpose:** Applies a single `<patch>` block — finds the `<search>` text in the target file and replaces it with `<replace>` text.

~~~mermaid
flowchart TD
    Start["PatcherService.ApplyPatchAsync(path, search, replace)"]
    Start --> ReadFile["Read target file from disk"]
    ReadFile --> Normalize["Normalize line endings: CRLF → LF"]
    Normalize --> StripCDATA["Strip CDATA markers from search and replace blocks"]
    StripCDATA --> ExactMatch{"Exact string match\n(string.Contains)"}
    ExactMatch -- "Found" --> ApplyExact["Replace first occurrence\nWrite file"]
    ExactMatch -- "Not found" --> FuzzyMatch{"Fuzzy match\n(normalize whitespace per line,\nignore indentation differences)"}
    FuzzyMatch -- "Found" --> ApplyFuzzy["Apply replace block\nWrite file"]
    FuzzyMatch -- "Not found" --> Fail(("Return failure\nFile unchanged"))
    ApplyExact --> Success(("Return success"))
    ApplyFuzzy --> Success
~~~

**Why fuzzy fallback exists:** Indentation can drift between what the AI saw and what is on disk (editor formatting, prior patches). The fuzzy pass normalizes leading whitespace per line before matching, so minor indentation differences don't cause avoidable failures.

---

## RequestService

**Purpose:** Handles `<ai-request>` blocks — reads the requested files from disk and returns them as a formatted context string.

~~~mermaid
flowchart TD
    Start["RequestService.HandleAsync(requestNode, projectRoot)"]
    Start --> CollectPaths["Collect all <file path='...'> entries"]
    CollectPaths --> OutOfSync{"<out-of-sync-index-files/>\npresent?"}
    OutOfSync -- "Yes" --> ScanChanges["IndexService.GetChangedFilesAsync\n• Files modified since index lastUpdated\n• New files not yet in index\n• Files in index but deleted on disk\n• Empty-purpose files\nAdd up to max-files (default 20) to request list\nNote if more remain"]
    OutOfSync -- "No" --> CheckIndex
    ScanChanges --> CheckIndex

    CheckIndex{"index.xml requested\nbut missing?"}
    CheckIndex -- "Yes" --> GenIndex["IndexService.GenerateIndexAsync\non the fly"]
    CheckIndex -- "No" --> ReadLoop
    GenIndex --> ReadLoop

    ReadLoop["For each requested path"]
    ReadLoop --> AiIgnored{"Blocked by .aiignore?"}
    AiIgnored -- "Yes" --> Denied["// ACCESS DENIED"]
    AiIgnored -- "No" --> Exists{"File exists on disk?"}
    Exists -- "No" --> Missing["// File not found on disk"]
    Exists -- "Yes" --> ReadFile["Read file content"]
    ReadFile --> Wrap["Wrap in <file path='...' lines='N'>"]
    Denied --> NextFile
    Missing --> NextFile
    Wrap --> NextFile
    NextFile --> ReadLoop

    ReadLoop --> Group["Group by module\n(ProjectDetector)"]
    Group --> Write["Write ai-requested-context.txt"]
    Write --> Done(("Return ContextPayload"))
~~~

---

## IndexService

**Purpose:** Manages `ai-bridge/index.xml` — the lightweight codebase map that lets the AI navigate the project without reading every file.

| Method | What it does |
|---|---|
| `GenerateIndexAsync` | Scans tracked files; adds new entries with `purpose=""`; preserves existing purposes; writes `index.xml` |
| `HandleUpdate` | Applies an `<update-index>` delta — updates purposes, adds new entries, removes deleted ones |
| `HandleCreate` | Replaces `index.xml` entirely from a `<create-index>` block |
| `GetChangedFilesAsync` | Compares `index.xml` against disk — returns modified, new, deleted, and empty-purpose files |

**Index file structure:**
~~~xml
<ai-bridge-index lastUpdated="2024-01-15T10:30:00Z">
  <module name="MyApp">
    <file path="src/Program.cs" purpose="Entry point. Wires DI and starts the host." />
    <file path="src/Services/Foo.cs" purpose="" />  <!-- not yet described -->
  </module>
</ai-bridge-index>
~~~

---

## TrackerService

**Purpose:** Maintains `ai-bridge/artifacts/tracker.xml` — an optional multi-session progress log that the AI updates to track scope, decisions, tasks, and current focus.

~~~mermaid
flowchart TD
    Start["TrackerService.HandleTracker(trackerNode, projectRoot)"]
    Start --> ResetCheck{"reset='true' attribute?"}
    ResetCheck -- "Yes" --> Delete["Delete existing tracker.xml"]
    ResetCheck -- "No" --> Load
    Delete --> Load["Load or create tracker.xml"]
    Load --> Scope{"<scope> present?"}
    Scope -- "Yes" --> UpsertScope["Upsert scope text"]
    Scope -- "No" --> Decisions
    UpsertScope --> Decisions{"<decisions> present?"}
    Decisions -- "Yes" --> UpsertDecisions["Upsert each <decision id='N'>"]
    Decisions -- "No" --> Tasks
    UpsertDecisions --> Tasks{"<tasks> present?"}
    Tasks -- "Yes" --> UpsertTasks["Upsert each <task id='N' status='...'>"]
    Tasks -- "No" --> Focus
    UpsertTasks --> Focus{"<focus> present?"}
    Focus -- "Yes" --> UpsertFocus["Update current focus task ID"]
    Focus -- "No" --> Save
    UpsertFocus --> Save["Save tracker.xml"]
    Save --> Log["Log: N/M tasks done → Focus: Task X"]
    Log --> Done(("Return"))
~~~

---

## ProjectDetector

**Purpose:** Detects the project structure of the workspace so files can be grouped into named modules.

- Looks for ecosystem markers: `.csproj`, `package.json`, `pyproject.toml`, `go.mod`, `Cargo.toml`
- Each marker's directory becomes a named project (e.g. `MyApp.csproj` → module `MyApp`)
- Files that don't belong to any detected project fall into a root group named after the workspace folder
- Used by `PackerService`, `RequestService`, and `IndexService` to produce consistent module names across all output

---

## FileFilterHelper

**Purpose:** Shared filtering logic used by both `PackerService` and `IndexService`.

| Filter | What it excludes |
|---|---|
| `AlwaysExcludePrefixes` | Paths starting with `ai-bridge/` or `ai-bridge-` |
| `BinaryExtensions` | `.dll`, `.png`, `.zip`, `.exe`, and other non-text formats |
| `ExcludeFileNames` | `package-lock.json`, `.DS_Store`, and other always-irrelevant files |
| `.aiignore` rules | User-defined folder prefixes and file glob patterns |
| `.gitignore` / git | Files not tracked by git are excluded via `git ls-files` |
