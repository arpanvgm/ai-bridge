# AI Bridge — Usage Guide

A no-agent, no-extension workflow for applying AI-generated code changes to any project directly from your browser using the `ai-bridge` .NET Global Tool.

Works with **any language or technology** — .NET, Node.js, Python, React, or any project with a sensible folder structure.

---

## The Big Picture

```text
Your Project
    │
    ▼
[1] ai-bridge init  ──►  Scaffolds .aiignore, aiSkills/, aiPrompts/
    │
    ▼
[2] ai-bridge pack  ──►  aiArtifacts/*-context.txt
                                    │
                                    ▼
                         [3] Paste into Browser AI
                             (ChatGPT / Claude / Gemini)
                                    │
                             Ask for changes
                                    │
                                    ▼
                         [4] Save AI response
                             ──► aiArtifacts/ai-response.xml
                                    │
                                    ▼
                         [5] ai-bridge apply
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


## Step 1 — Initialize Your Workspace

Open your terminal, navigate to your project directory, and run the `init` command.

```bash
cd D:\Code\Github\you\your-project
ai-bridge init
```

**What it does:**
1. Creates a `.aiignore` file for custom exclusion rules.
2. Extracts default AI skills and prompts to the `aiSkills/` and `aiPrompts/` folders.
3. Patches your `.gitignore` so these folders aren't checked into source control.
4. Creates the `aiArtifacts/` folder and placeholder `ai-response.xml`.

> **Note:** Running `init` is completely safe. It will **never** overwrite your files if you have customized the skills or prompts locally.
> If you want to pull down the newest default templates after updating the tool, you can run `ai-bridge update` to overwrite your local templates.

---

## Step 2 — Pack Your Project Context

Once initialized, run the `pack` command to build your context file.

```bash
ai-bridge pack
```

**What it does:**
1. **Uses `git ls-files`** to determine which files to include — automatically respects all `.gitignore` rules (nested, negation, global).
2. **Filters out binary files** (images, fonts, executables, archives, etc.) so only source code and config are packed.
3. **Applies `.aiignore` rules** for any additional exclusions you define.
4. **Groups files by project** based on ecosystem detection.

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

## Step 3 — Give Context to the AI

1. Open your browser AI (ChatGPT, Claude, or Gemini).
2. **Set the System Prompt** — paste the contents of `aiSkills/ai-system-prompt.md` into the system / custom instructions area. You only need to do this once per chat session.
3. **Upload the context file(s)** — attach the relevant `*-context.txt` file(s) from `aiArtifacts\`.
4. **Describe what you want** — ask the AI to add a feature, fix a bug, refactor code, delete files, etc.

---

## Step 4 — Apply the AI Response

The AI will respond with a valid XML document. AI Bridge performs **strict validation** on this response (ensuring only valid `<file>`, `<patch>`, or `<delete>` tags are present).

Once the AI generates the response, simply run:

```bash
ai-bridge apply
```

AI Bridge **automatically finds** the AI response using a smart 3-step detection:

| Priority | Source | What happens |
|----------|--------|--------------|
| 1️⃣ | **ai-response.xml** | If the file has real content (not the default placeholder), it's used directly. |
| 2️⃣ | **Clipboard** | If the file is empty/placeholder, AI Bridge reads from your system clipboard. The content is saved to `ai-response.xml` and then processed. |
| 3️⃣ | **Terminal (stdin)** | If clipboard is unavailable (e.g. WSL2, SSH, headless server), you're prompted to paste the XML directly into your terminal. Just paste and press Enter after the closing `</ai-response>` tag. |

> **Tip:** The fastest workflow is: copy the AI response in your browser (`Ctrl+C`), switch to your terminal, and run `ai-bridge apply`. That's it — no file saving needed!

### How to get the AI response into your project

**Option A: Just copy and run (Fastest ✨)**

1. Select the AI response in your browser and copy it (`Ctrl+C`).
2. Run `ai-bridge apply` — it reads from your clipboard automatically.

**Option B: Save to file**

Save the response as `ai-response.xml` inside your `aiArtifacts/` folder, then run `ai-bridge apply`.

**Option C: Paste into terminal**

If you're on a headless system or clipboard isn't available, `ai-bridge apply` will prompt you to paste the XML directly into the terminal. Just paste and press Enter after the closing tag — no Ctrl+D needed.

### What the tool does

1. **Phase 1** — Applies `<file>` blocks (creates or overwrites full files).
2. **Phase 2** — Applies `<patch>` blocks (targeted search-and-replace, with fuzzy matching fallback).
3. **Phase 3** — Applies `<delete>` blocks (removes files).
4. **Phase 4** — Cleans up any folders left empty after deletions.
5. **Prints a summary** of all changes made.
6. **Resets** `ai-response.xml` to prevent accidental re-application.

### After the run
- If some patches failed → paths are saved to `aiArtifacts\failed-patches.txt`. Go back to the AI and ask:
  > *"Some patches failed for these files. Please give me full `<file>` blocks instead: [paste failed-patches.txt contents]"*

### Apply Options

| Flag | Description |
|------|-------------|
| `--watch` | Continuous mode. Monitors `ai-response.xml` and auto-applies whenever you save it. |
| `--dry-run` | Preview what changes would be made without modifying any files. |
| `--paste` | Skip the file check and read directly from clipboard. Optional — auto-detected by default. |

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

When you run `ai-bridge init`, it sets up these folders:

```text
YourProjectRoot\
├── .aiignore                   ← Additional ignore rules (works alongside .gitignore)
├── aiSkills\                   ← Auto-created, gitignored
│   ├── ai-system-prompt.md     ← System prompt for your browser AI
│   └── ai-response-skill.md    ← Code modification protocol
├── aiPrompts\                  ← Auto-created, gitignored
│   └── ...                     ← Additional prompt templates
└── aiArtifacts\                ← Auto-created, gitignored
    ├── *-context.txt           ← Output of ai-bridge pack
    ├── ai-response.xml         ← AI response you paste/download here
    └── failed-patches.txt      ← Created only when patches fail
```

---

## Platform Support — Clipboard

AI Bridge uses native OS commands for clipboard access — **no external dependencies required**.

| Platform | Clipboard Tool | Notes |
|----------|---------------|-------|
| **Windows** | Built-in (`powershell.exe` / `clip.exe`) | Works out of the box. |
| **macOS** | Built-in (`pbpaste` / `pbcopy`) | Works out of the box. |
| **Linux (X11)** | `xclip` | Install: `sudo apt install xclip` |
| **Linux (Wayland)** | `wl-clipboard` | Install: `sudo apt install wl-clipboard` |
| **WSL2** | Windows bridge (`powershell.exe` / `clip.exe`) | Works automatically when Windows interop is enabled (default). |
| **WSL2 (interop off)** | stdin fallback | Clipboard unavailable — AI Bridge prompts you to paste into the terminal. |
| **Headless / SSH** | stdin fallback | No clipboard — AI Bridge prompts you to paste into the terminal. |

> **How it works:** When clipboard is unavailable, AI Bridge gracefully falls back to terminal input. You paste your XML and press Enter after the closing `</ai-response>` tag. No special key combinations needed.

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

### Clipboard not working

**Symptom:**
```
⚠ Could not access clipboard: Could not start 'xclip'. Is it installed and in PATH?
```

**Fix:** Install the clipboard tool for your platform:
- **Linux (X11):** `sudo apt install xclip`
- **Linux (Wayland):** `sudo apt install wl-clipboard`
- **WSL2:** Ensure Windows interop is enabled (it is by default). If interop is disabled, AI Bridge will fall back to terminal paste.

If no clipboard tool is available, AI Bridge automatically prompts you to paste the AI response directly into the terminal.

---
