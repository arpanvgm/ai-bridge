
# AI Bridge — CLI Workflows

The CLI (`ai-bridge`) is a .NET global tool driven manually by the developer. This document covers every command, its full internal flow, and its behaviour on repeated runs.

---

## Workspace State Guard

`pack` and `apply` both call this check before doing anything. `init` and `migrate` skip it — they are the commands that fix state, not the ones that depend on it.

~~~mermaid
flowchart TD
    Start["AssertWorkspaceValid(projectRoot)"]
    Start --> Check["WorkspaceValidator.Check"]
    Check --> Status{"WorkspaceStatus?"}
    Status -- "NotInitialized" --> ErrInit["Log error: 'run ai-bridge init'\nSet ExitCode = 1\nReturn false"]
    Status -- "VersionMismatch" --> ErrMigrate["Log error: 'run ai-bridge migrate'\nSet ExitCode = 1\nReturn false"]
    Status -- "Valid" --> Proceed["Return true\n→ continue with command"]
~~~

---

## `ai-bridge init`

**Purpose:** First-time setup for a new project. Safe to run again — no user data is lost.

~~~mermaid
flowchart TD
    Start["ai-bridge init"]
    Start --> Setup["WorkspaceSetupService.SetupAsync(projectRoot)"]
    Setup --> S1["Create ai-bridge/artifacts/\nCreate ai-response.xml if missing"]
    S1 --> S2["Write ai-bridge/.gitignore\n(always overwritten — tool-owned)"]
    S2 --> S3["Append ai-bridge/ to .dockerignore\n(only if not already there)"]
    S3 --> S4["Create .aiignore with defaults if missing\nOR append only missing default rules"]
    S4 --> S5["TemplateService.ExtractAll\n• Delete old SimpleMode/, AdvancedMode/, AutoIndexMode/\n• Re-extract all embedded templates"]
    S5 --> S6["IndexService.GenerateIndexAsync\n• Scan tracked files\n• Add new with purpose=''\n• Preserve existing purposes"]
    S6 --> Stamp["StateService.InitState\nWrite current version to state.xml"]
    Stamp --> Done(("✅ Setup complete\nUpload ai-bridge/skills/ to your AI"))

    style S4 fill:#2d4a2d,color:#fff
    style S6 fill:#2d4a2d,color:#fff
~~~

**Green steps** = safe even on repeated runs (never destructive to user data).

**Run twice behaviour:**

| File | Behaviour |
|---|---|
| `ai-response.xml` | Never overwritten |
| `.aiignore` | Only missing default rules appended — never cleared |
| `index.xml` | Existing purposes preserved — only new files added |
| `ai-bridge/.gitignore` | Always overwritten (same content, harmless) |
| `AutoIndexMode/`, `skills/` | Always wiped and re-extracted — guaranteed up to date |
| `1-SimpleMode/`, `2-AdvancedMode/` | Deleted if present (legacy cleanup) — never re-extracted |
| `state.xml` | Always overwritten with current version |

---

## `ai-bridge migrate`

**Purpose:** Updates the local workspace after installing a new version of the tool. Identical flow to `init`.

~~~mermaid
flowchart TD
    Start["ai-bridge migrate"]
    Start --> Setup["WorkspaceSetupService.SetupAsync(projectRoot)"]
    Setup --> Same["(identical steps to init —\nsee init diagram above)"]
    Same --> Stamp["StateService.InitState\nOverwrite state.xml with new version"]
    Stamp --> Done(("✅ Migrated\nRe-upload ai-bridge/skills/ to your AI"))
~~~

**When is this needed?**
`pack` and `apply` call `WorkspaceValidator.Check` before running. If the version in `state.xml` doesn't match the running binary, they exit with: *"run ai-bridge migrate"*.

**Key guarantee:** Stale template files from old versions cannot linger — `ExtractAll` deletes the template folders entirely before re-extracting, so removed or renamed files don't accumulate.

---

## `ai-bridge pack`

**Purpose:** Reads all tracked source files and writes `*-context.txt` bundles for the AI to consume.

~~~mermaid
flowchart TD
    Start["ai-bridge pack"]
    Start --> Guard["AssertWorkspaceValid\n(exits if not initialized or outdated)"]
    Guard --> Pack["PackerService.PackAsync"]
    Pack --> GitFiles["git ls-files --cached --others\n(fallback: directory walk)"]
    GitFiles --> Filter["For each file — apply filters:\n• Skip ai-bridge/ prefix\n• Skip binary extensions\n• Skip excluded filenames\n• Skip .aiignore matches\n• Skip .gitignore'd files"]
    Filter --> Detect["ProjectDetector: group files by module"]
    Detect --> Write["For each module:\nWrap in <module> tags\nWrite {Name}-context.txt to ai-bridge/artifacts/\nLog: file count, KB, token estimate"]
    Write --> Done(("✅ Pack complete\nUpload *-context.txt files to your AI"))
~~~

**Output files** go to `ai-bridge/artifacts/`. Running pack twice always overwrites with current state — fully idempotent.

---

## `ai-bridge apply`

**Purpose:** Reads an `<ai-request>` or `<ai-response>` XML block and executes it against the codebase.

### Step 1 — Input resolution

~~~mermaid
flowchart TD
    Start["ai-bridge apply"]
    Start --> Guard["AssertWorkspaceValid"]
    Guard --> PasteFlag{"--paste flag?"}
    PasteFlag -- "No" --> ReadFile["Read ai-bridge/artifacts/ai-response.xml"]
    PasteFlag -- "Yes" --> Clipboard["Try clipboard\n(pbpaste / Get-Clipboard / wl-paste / xclip)"]
    Clipboard --> ClipOk{"Content found?"}
    ClipOk -- "Yes" --> SaveToFile["Save to ai-response.xml"]
    ClipOk -- "No" --> Stdin["Fall back to stdin\n(read until </ai-response> or </ai-request>)"]
    Stdin --> SaveToFile
    ReadFile --> CoreApply["ApplyService.ExecuteAsync"]
    SaveToFile --> CoreApply
~~~

### Step 2 — Apply branches

~~~mermaid
flowchart TD
    CoreApply["ApplyService.ExecuteAsync"]
    CoreApply --> Strip["Strip markdown fences if present"]
    Strip --> Parse{"Valid XML?"}
    Parse -- "No" --> Abort["Log error: not valid XML\nSet ExitCode = 1"]
    Parse -- "Yes" --> Root{"Root element?"}

    Root -- "<ai-request>" --> Request["RequestService.HandleAsync\n• Read requested files from disk\n• Write ai-requested-context.txt\n• Return ContextPayload"]
    Request --> CopyClip["Copy ContextPayload to clipboard"]
    CopyClip --> Reset1["Reset ai-response.xml"]

    Root -- "<ai-response>" --> Edits["Process <ai-edits>:\n<file> → write to disk\n<patch> → PatcherService\n<delete> → delete from disk"]
    Edits --> PatchOk{"All patches succeeded?"}
    PatchOk -- "No" --> LogFail["Log each failed patch\nSet ExitCode = 1\nSkip <update-index>"]
    PatchOk -- "Yes" --> Index["IndexService.HandleUpdate\n(if <update-index> present)"]
    Index --> NoIndex{"<update-index> missing\nbut edits were made?"}
    NoIndex -- "Yes" --> Warn["Log warning: index not updated"]
    NoIndex -- "No" --> Tracker
    Warn --> Tracker
    LogFail --> Tracker
    Tracker["TrackerService.HandleTracker\n(if <tracker> present)"]
    Tracker --> Reset2["Reset ai-response.xml"]
    Abort --> Reset2
    Reset1 --> Done(("Exit"))
    Reset2 --> Done
~~~

**Why `ai-response.xml` is always reset:** Patches are not idempotent. If a run partially fails, re-running the same file would re-apply already-successful patches against the now-modified file, causing them to fail or corrupt content. The reset forces the user to get a fresh response from the AI.

---

## `ai-bridge apply --watch`

**Purpose:** Runs apply once on startup, then stays alive and re-applies whenever `ai-response.xml` is saved.

~~~mermaid
flowchart TD
    Start["ai-bridge apply --watch"]
    Start --> PasteConflict{"--paste also set?"}
    PasteConflict -- "Yes" --> WarnPaste["Warn: --watch ignored with --paste\nRun once from clipboard\nExit"]
    PasteConflict -- "No" --> Guard["AssertWorkspaceValid"]
    Guard --> RunOnce["Run apply once (normal flow)"]
    RunOnce --> Watcher["Create FileSystemWatcher\non ai-bridge/artifacts/\nfilter: ai-response.xml"]
    Watcher --> Wait["Wait for Changed or Created event"]
    Wait --> Debounce{"Last run < 1000ms ago?"}
    Debounce -- "Yes" --> Wait
    Debounce -- "No" --> LockWait["Wait 500ms for file lock to release"]
    LockWait --> RunAgain["Run apply (full flow)"]
    RunAgain --> Wait
    Wait -- "Ctrl+C" --> Exit(("Exit"))
~~~

---

## `ai-bridge --help` / `ai-bridge <command> --help`

Standard `System.CommandLine` help output. Lists all commands and options. No Core services invoked.

---

## Command Summary

| Command | Guard | Core services called | Exits after? |
|---|---|---|---|
| `init` | None | `WorkspaceSetupService`, `StateService` | Yes |
| `migrate` | None | `WorkspaceSetupService`, `StateService` | Yes |
| `pack` | `WorkspaceValidator` | `PackerService` | Yes |
| `apply` | `WorkspaceValidator` | `ApplyService`, `RequestService`, `IndexService`, `TrackerService` | Yes |
| `apply --watch` | `WorkspaceValidator` | Same as apply, in a loop | No (Ctrl+C) |
