# AI Bridge — Usage Guide

A no-agent, no-extension workflow for applying AI-generated code changes to any project directly from your browser using the `ai-bridge` .NET Global Tool.

Works with **any language or technology** — .NET, Node.js, Python, React, or any project with a sensible folder structure.

---

## The Big Picture

```text
Your Project
    │
    ▼
[1] ai-bridge pack  ──►  aiArtifacts/*-context.txt
                                    │
                                    ▼
                         [2] Paste into Browser AI
                             (ChatGPT / Claude / Gemini)
                                    │
                             Ask for changes
                                    │
                                    ▼
                         [3] Save AI response
                             ──► aiArtifacts/ai-response.xml
                                    │
                                    ▼
                         [4] ai-bridge apply
                                    │
                                    ▼
                             Code lands in your project ✅
```

---

## Prerequisites

- **.NET 10 SDK** installed on your machine.
- **Git** installed and on your PATH (the target project must be a git repo). AI Bridge uses `git ls-files` to automatically respect your `.gitignore` rules.
- A browser AI account: [ChatGPT](https://chatgpt.com), [Claude](https://claude.ai), or [Gemini](https://gemini.google.com)

---

## Installation

Install the tool globally using the .NET CLI. Once installed, the `ai-bridge` command is available in any terminal window.

```bash
dotnet tool install --global Tools.AIBridge
```
*(Note: If installing locally during development, use `dotnet tool install --global --add-source ./AIBridge/bin/Release/ Tools.AIBridge`).*

### Uninstallation
To remove the global tool from your machine:
```bash
dotnet tool uninstall --global Tools.AIBridge
```

### Updating the Tool
To update the tool to the latest version from NuGet:
```bash
dotnet tool update --global Tools.AIBridge
```

---

## Supported Ecosystems

AI Bridge automatically detects your project type and groups files intelligently:

| Ecosystem | Detected By | Grouping |
|-----------|-------------|----------|
| .NET | `.csproj` files | One context file per project |
| Node.js | `package.json` in subfolders | One context file per package |
| Python | `pyproject.toml` | One context file per package |
| Go | `go.mod` | One context file per module |
| Rust | `Cargo.toml` | One context file per crate |
| Other | (fallback) | One context file per top-level folder |

Root-level files (e.g., `docker-compose.yml`, `README.md`) are always packed into a `*-Solution-context.txt` file.

---


## Step 1 — Pack Your Project Context

Open your terminal, navigate to your project directory, and run the `pack` command. On first run, it auto-initializes your workspace (creates `.aiignore`, `aiSkills/`, `aiArtifacts/`, patches `.gitignore`).

```bash
cd D:\Code\Github\you\your-project
ai-bridge pack
```

**What it does:**
1. **Auto-initializes** on first run — creates `.aiignore`, `aiSkills/ai-system-prompt.md`, and `aiArtifacts/`.
2. **Uses `git ls-files`** to determine which files to include — automatically respects all `.gitignore` rules (nested, negation, global).
3. **Filters out binary files** (images, fonts, executables, archives, etc.) so only source code and config are packed.
4. **Applies `.aiignore` rules** for any additional exclusions you define.
5. **Groups files by project** based on ecosystem detection.

**Output:** One `*-context.txt` file per project layer, saved to `aiArtifacts\`:

```text
aiArtifacts\
    YourApp.WebApi-context.txt
    YourApp.DataProvider-context.txt
    YourApp.SharedContracts-context.txt
    YourApp-Solution-context.txt
```

> **Tip:** You don't always need to upload all files. If you're only changing the API layer, just upload `YourApp.WebApi-context.txt`.

### File Filtering

AI Bridge uses a layered approach to decide which files to pack:

| Layer | What it does |
|-------|-------------|
| `.gitignore` | Files ignored by git are automatically excluded (via `git ls-files`) |
| Binary blocklist | Images, fonts, executables, archives, media, etc. are always skipped |
| `.aiignore` | Your additional exclusions (e.g., `TestResults/`, `*.g.cs`) |

> **Tip:** If the packed context includes files you don't need, add your own rules to `.aiignore` and run `ai-bridge pack` again to regenerate the context files.

> **Fallback:** If git is not available, AI Bridge uses built-in exclusion rules (common folders like `bin/`, `obj/`, `node_modules/`, etc.).

---

## Step 2 — Give Context to the AI

1. Open your browser AI (ChatGPT, Claude, or Gemini).
2. **Set the System Prompt** — paste the contents of `ai-system-prompt.md` into the system / custom instructions area. You only need to do this once per chat session.
3. **Upload the context file(s)** — attach the relevant `*-context.txt` file(s) from `aiArtifacts\`.
4. **Describe what you want** — ask the AI to add a feature, fix a bug, refactor code, delete files, etc.

---

## Step 3 — Save the AI Response

The AI will respond with a valid XML document. AI Bridge performs **strict validation** on this file:
- The root element must be `<ai-response>`.
- Only `<file>`, `<patch>`, and `<delete>` are allowed as child elements.
- Conversational text outside these tags is ignored, but invalid tags will cause an error.

Save this response as `ai-response.xml` in the `aiArtifacts\` folder.

### Option A — Copy & Paste (Works with all AI tools)
1. Select the entire AI response text in the browser.
2. Copy it (`Ctrl+C`).
3. Open Notepad (or any editor), paste, and save as:
   ```text
   aiArtifacts\ai-response.xml
   ```

### Option B — Download (ChatGPT with Code Interpreter)
Ask ChatGPT directly:
> *"Save your response to a file called ai-response.xml and give me a download link."*

ChatGPT will generate a downloadable file. Place it in `aiArtifacts\`.

---

## Step 4 — Apply the AI Response

Inside your project directory, run the `apply` command to apply the changes.

```bash
ai-bridge apply
```

### What the tool does
1. **Phase 1** — Applies `<file>` blocks (creates or overwrites full files).
2. **Phase 2** — Applies `<patch>` blocks (targeted search-and-replace).
3. **Phase 3** — Applies `<delete>` blocks (removes files).
4. **Phase 4** — Cleans up any folders left empty after deletions.
5. **Prints a summary** of all changes made.

### After the run
- If some patches failed → paths are saved to `aiArtifacts\failed-patches.txt`. Go back to the AI and ask:
  > *"Some patches failed for these files. Please give me full `<file>` blocks instead: [paste failed-patches.txt contents]"*

### Apply Options

| Flag | Description |
|------|-------------|
| `--watch` | Continuous mode. Applies current changes, then monitors `ai-response.xml` and auto-applies whenever you save it. |
| `--dry-run` | Preview what changes would be made without modifying any files. |

**Continuous Watch Mode (Recommended workflow):**
~~~bash
ai-bridge apply --watch
~~~
*Leave this running in a separate terminal. Every time you paste an AI response into `ai-response.xml` and save, your code is updated automatically.*

**Dry-run example:**
~~~bash
ai-bridge apply --dry-run
~~~
Output:
~~~text
  CREATE: MyApp/Services/NewService.cs
  OVERWRITE: MyApp/Controllers/OrderController.cs
  PATCH: MyApp/Models/Order.cs
  DELETE: MyApp/Services/OldService.cs

Dry run complete: 2 file(s), 1 patch(es), 1 delete(s).
No files were modified. Run 'ai-bridge apply' to apply for real.
~~~

---

## System Prompt

The AI needs a system prompt so it responds in the correct XML format that `ai-bridge apply` understands. After running `ai-bridge pack`, you'll find the system prompt at:

```text
aiSkills/ai-system-prompt.md
```

**Setup (one-time per AI chat session):**
1. Open `aiSkills/ai-system-prompt.md` in any text editor.
2. Copy the entire contents of the file.
3. Paste it into your browser AI's system instructions:
   - **ChatGPT**: Settings → Personalization → Custom Instructions (top box)
   - **Claude**: Start a new Project → Project Instructions
   - **Gemini**: System instructions (in Gemini Advanced / API settings)

---

## What it generates in your project

When you run `ai-bridge pack`, it sets up two folders:

```text
YourProjectRoot\
├── .aiignore                   ← Additional ignore rules (works alongside .gitignore)
├── aiSkills\                   ← Committed to git (team-shared)
│   └── ai-system-prompt.md    ← System prompt for your browser AI
└── aiArtifacts\                ← Auto-created, gitignored
    ├── *-context.txt           ← Output of ai-bridge pack
    ├── ai-response.xml         ← AI response you paste/download here
    └── failed-patches.txt      ← Created only when patches fail
```

---

## Troubleshooting

### `ai-bridge` is not recognized as a command

**Symptom:**
```
ai-bridge: The term 'ai-bridge' is not recognized as the name of a cmdlet, function, script file, or executable program.
```

**Cause:** .NET global tools are installed to `%USERPROFILE%\.dotnet\tools`, which should be on your `PATH`. This entry can go missing after a Windows Update, .NET SDK update, or environment refresh — even though the tool itself is still installed and intact.

**Fix:** Run the following in PowerShell to permanently restore the entry to your user PATH:
```powershell
[System.Environment]::SetEnvironmentVariable(
    "PATH",
    ([System.Environment]::GetEnvironmentVariable("PATH", [System.EnvironmentVariableTarget]::User) + ";$env:USERPROFILE\.dotnet\tools"),
    [System.EnvironmentVariableTarget]::User
)
```
Then **restart your terminal**. The command should work again.

---
