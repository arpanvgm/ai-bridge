

# AI Bridge — Architecture Overview

This document is the entry point for understanding the AI Bridge ecosystem. Read this first, then go to the relevant workflow file for deeper detail.

---

## What AI Bridge is

AI Bridge is a toolkit that connects a local codebase to an AI assistant. The AI reads source files, reasons about them, and outputs structured XML. AI Bridge applies that XML to the real files on disk — creating, patching, and deleting code, and keeping a lightweight index of the codebase in sync.

---

## The Three Layers

~~~
┌─────────────────────────────────────────────────────────┐
│                        Consumer Layer                   │
│                                                         │
│   AIBridge.Cli          │      AIBridge.Mcp             │
│   (dotnet tool)         │      (dotnet tool)            │
│   ai-bridge             │      ai-bridge-mcp            │
│                         │                               │
│   Manual workflow.      │   Always-on server.           │
│   User runs commands,   │   AI calls the MCP tool       │
│   pastes AI output.     │   directly — no copy/paste.   │
└────────────┬────────────┴──────────────┬────────────────┘
             │                           │
             └─────────────┬─────────────┘
                           │  both depend on
                           ▼
┌─────────────────────────────────────────────────────────┐
│                        Core Layer                       │
│                     AIBridge.Core                       │
│                                                         │
│  WorkspaceValidator     WorkspaceSetupService           │
│  StateService           TemplateService                 │
│  PackerService          ApplyService                    │
│  PatcherService         RequestService                  │
│  IndexService           TrackerService                  │
│  ProjectDetector        FileFilterHelper                │
└─────────────────────────────────────────────────────────┘
~~~

**Core** contains all business logic. It has no knowledge of CLI commands, MCP endpoints, or HTTP. Both consumers depend on it; it depends on neither.

**CLI** is a command-line tool. The user drives it manually — running commands, uploading context files to the AI, and pasting responses back.

**MCP** is an HTTP server. Once running, the AI drives it directly via the MCP protocol — no human copy/paste in the loop.

---

## The Workspace

Both consumers operate on an `ai-bridge/` folder that lives inside the project repo:

~~~
<your-project>/
├── ai-bridge/
│   ├── .template-stamp                    ← local hash of the extracted templates (gitignored)
│   ├── index.xml                          ← codebase map (path + purpose per file)
│   ├── .gitignore                         ← excludes artifacts, templates and the stamp from git
│   ├── artifacts/
│   │   ├── ai-response.xml                ← where AI output is written / read from
│   │   ├── ai-requested-context.txt       ← files the AI asked for
│   │   └── tracker.xml                    ← optional multi-session progress tracker
│   ├── AutoIndexMode/                     ← skill/prompt files (extracted on setup)
│   └── skills/                            ← shared skill files used by both modes
├── .aiignore                              ← user-controlled file exclusion rules
└── ... your source code ...
~~~

`.aiignore` is the only file the user is expected to edit. Everything else inside `ai-bridge/` is managed by the tool.

`.template-stamp` is machine-local: it describes the templates extracted on *this* machine, so it is gitignored and never shared through the repo.

---

## Workspace States

Every command (except `init` and `migrate`) checks workspace state before doing anything. The check is `WorkspaceValidator.Check`, which asks `StateService` whether the templates extracted into `ai-bridge/` still match the templates built into the running tool.

| Status | When | CLI | MCP |
|---|---|---|---|
| `NotInitialized` | `ai-bridge/.template-stamp` is missing — first run or a fresh clone | Error + exit: run `ai-bridge init` | Auto-initialize, then continue |
| `VersionMismatch` | The stamp does not match the templates in the running tool, **or** an expected template file is missing from `ai-bridge/` | Error + exit: run `ai-bridge migrate` | Error + exit: run `ai-bridge-mcp migrate` |
| `Valid` | The stamp matches **and** every expected template file exists | Proceed normally | Proceed normally |

The stamp is a SHA-256 hash of the templates embedded in the tool — not a version number. Adding, editing, renaming or removing a template changes the hash automatically, so there is no version to bump by hand. See `StateService` in `workflows/CORE.md` for details and limits.

---

## The AutoIndexMode Loop (Daily Workflow)

This is the primary workflow for working on an existing codebase.

~~~
┌──────────┐    upload     ┌───────────┐    <ai-request>    ┌──────────────┐
│          │   index.xml   │           │ ─────────────────► │              │
│ Developer│  + skills     │    AI     │                    │  CLI / MCP   │
│          │ ────────────► │ Assistant │    context.txt     │  (apply cmd) │
│          │               │           │ ◄───────────────── │              │
│          │               │           │                    └──────────────┘
│          │               │           │    <ai-response>   ┌──────────────┐
│          │               │           │ ─────────────────► │              │
│          │               │           │                    │  CLI / MCP   │
│          │               │           │    files patched   │  (apply cmd) │
│          │               │           │ ◄───────────────── │              │
└──────────┘               └───────────┘                    └──────────────┘
~~~

**Turn 1 — Context request:**
AI outputs `<ai-request>` listing files it needs. CLI/MCP reads them from disk and returns them as context.

**Turn 2 — Code change:**
AI outputs `<ai-response>` with file writes, patches, deletes, and an index delta. CLI/MCP applies everything to disk.

---

## Further Reading

| File | What it covers |
|---|---|
| `workflows/CORE.md` | Every Core service — what it does and how it works internally |
| `workflows/CLI.md` | Every CLI command — full internal flow, decision points, file effects |
| `workflows/MCP.md` | MCP server startup, OAuth, migrate subcommand, tool invocation flow |
