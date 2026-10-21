
# AI Bridge

[![NuGet](https://img.shields.io/nuget/v/Tools.AIBridge?label=NuGet&logo=nuget)](https://www.nuget.org/packages/Tools.AIBridge)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **Index your project → Chat with any AI chatbot → Apply changes back.** No API keys, no extensions, no lock-in.

**AI Bridge** is a lightweight CLI tool that bridges your local codebase and browser-based AI chatbots (Claude, Gemini, Qwen, ChatGPT). It works with **any language or technology** — .NET, Node.js, Python, React, Go, Rust, or any project with a sensible folder structure.

---

## 💡 Why AI Bridge?

- **No API keys** — Use the free tiers of Claude, Gemini, Qwen, or ChatGPT directly in your browser.
- **No vendor lock-in** — Switch AI providers mid-conversation. Your workflow stays the same.
- **Token-efficient** — The AI works from a lightweight map of your project and only asks for the files it actually needs.
- **Language-agnostic** — Works with any language or framework, not just the ones your IDE supports.
- **No extensions or agents** — Just a CLI tool and a browser-based AI chat. Nothing running in the background.
- **You stay in control** — Every change is explicit: you see it, you paste it, you apply it.

---

## 🌟 The Big Picture

AI Bridge keeps your workflow simple:
1. **Index:** `ai-bridge init` creates a lightweight map (`index.xml`) of your project. You don't need to prepare or analyze it first.
2. **Chat:** The AI reads the map and asks for the specific files it needs — nothing more. You hand them over with one command.
3. **Apply:** The AI replies with code changes, and one command applies them to your project and keeps the map up to date.

**Optional: MCP Server:** A local `.NET` server you can run alongside the CLI to let AI clients (like Claude Desktop) connect directly and apply changes without you having to copy/paste. [Read the MCP Guide here](src/AIBridge.Mcp/README.md).

---

## ⚙️ Prerequisites & Installation

- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** installed on your machine.
- **Git** installed and on your PATH. AI Bridge uses `git ls-files` to automatically respect your `.gitignore` rules.
- A browser AI account (Claude, Gemini, Qwen, or ChatGPT).

> [!TIP]
> **Which AI model should I use?** Pick a thinking or reasoning "High" model (e.g., Claude 4.6 Sonnet, Gemini 3.1 Pro, Qwen 3.7 Max/Plus) — they produce significantly better, bug-free code.

Install the tool globally using the .NET CLI:
~~~bash
dotnet tool install --global Tools.AIBridge
~~~
*(To update later: `dotnet tool update --global Tools.AIBridge`, then run `ai-bridge migrate` in your projects — see [Updating AI Bridge](#️-updating-ai-bridge))* — [View on NuGet →](https://www.nuget.org/packages/Tools.AIBridge)

<details>
<summary><b>⚡ TL;DR — Try it in 60 seconds</b></summary>

~~~bash
dotnet tool install --global Tools.AIBridge
cd /path/to/your-project
ai-bridge init
# Upload the 4 files in ai-bridge/AutoIndexMode/ to your AI chat, then ask for a feature or a fix
# When the AI replies with XML, copy it, then:
ai-bridge apply --paste
~~~

</details>

---

## 🚀 Getting Started

Open your terminal, navigate to your project directory, and run the `init` command.

~~~bash
cd /path/to/your-project
ai-bridge init
~~~

**What it does:**
It scaffolds the following structure in your project and generates `index.xml`, a map listing every file in your project:

~~~text
YourProjectRoot/
├── .aiignore                           <-- Custom exclusion rules (Tracked)
└── ai-bridge/                          <-- The AI Bridge workspace
    ├── index.xml                       <-- Project map: every file, plus a short purpose per file
    ├── AutoIndexMode/                  <-- System prompt and core instructions for your AI
    ├── skills/                         <-- Optional extra skills (index sync, progress tracker, MCP)
    └── artifacts/                      <-- Scratchpad for AI responses and requested context
~~~

Running `init` again is always safe — your `.aiignore` rules and the purposes already stored in `index.xml` are kept.

### Excluding Files with `.aiignore`

The `.aiignore` file (created by `ai-bridge init`) works like `.gitignore` — add glob patterns to hide files from the AI. Everything already in your `.gitignore` is ignored automatically, so use `.aiignore` only for files that are tracked by Git but that the AI doesn't need to see: test fixtures, generated code, large data files, or anything sensitive. Ignored files stay out of the index and are never sent to the AI.

---

## 🔄 How It Works

### 1. Start a Chat with Your AI

Open your AI (Claude, Gemini, Qwen, or ChatGPT) in a fresh chat and upload the instruction files from your project:

**Required — the four files in `ai-bridge/AutoIndexMode/`:**

| File | What it does |
|------|--------------|
| `ai-system-prompt.md` | The system prompt that defines the workflow |
| `ai-request-skill.md` | Teaches the AI how to ask for the files it needs |
| `ai-response-skill.md` | Teaches the AI how to write code changes in a format AI Bridge can apply |
| `ai-update-index-skill.md` | Teaches the AI how to keep the project map up to date |

**Optional — from `ai-bridge/skills/`:**

| File | What it does |
|------|--------------|
| `ai-sync-index-skill.md` | Lets the AI fill in missing index purposes, or catch up after you edit files by hand |
| `ai-tracker-skill.md` | Tracks progress in `ai-bridge/artifacts/tracker.xml` so you can resume in a new chat |
| `ai-mcp-skill.md` | Only if you use the [MCP Server](src/AIBridge.Mcp/README.md) |

You don't have to upload `index.xml` — the AI will request it itself on its first turn. (Uploading it up front just saves one round trip.)

> [!TIP]
> **Starting a brand new project?** Nothing special is needed. Tell the AI what you want to build and it will generate the initial code.

### 2. Ask for a Feature

~~~mermaid
flowchart LR
    A["💬 Ask feature"] -->|AI requests files| B["ai-bridge apply --paste"]
    B -->|Upload context| C["🤖 AI generates code"]
    C -->|Copy response| D["ai-bridge apply --paste"]
    D --> E["✅ Updated"]
~~~

1. **Ask for a feature or a fix.**
2. **Fulfill the Request:**
   - The AI will output an XML request for files (e.g., `<ai-request><file path="src/app.js" /></ai-request>`).
   - Copy the request and run `ai-bridge apply --paste`.
   - The tool will automatically gather those files, save them to **`ai-bridge/artifacts/ai-requested-context.txt`**, and copy them to your clipboard.
   - Upload or paste that context back into the AI chat.
3. **Apply the Changes:** The AI generates the final code changes. Copy them and run `ai-bridge apply --paste`.

### 3. Apply the Changes

**Option 1 — Clipboard:** Copy the AI's XML response and run:
~~~bash
ai-bridge apply --paste
~~~

**Option 2 — File:** Save the XML to `ai-bridge/artifacts/ai-response.xml` and run:
~~~bash
ai-bridge apply
~~~

> [!NOTE]
> After every run, `ai-response.xml` is reset — even if some changes failed. Changes can't safely be applied twice, so if something fails, ask the AI for a fresh response instead of re-running the old one.

### About the Index

`ai-bridge init` builds `index.xml` for you: every file in your project, listed by path. There's nothing to analyze or set up before you start — the AI can begin working straight away and request any file by its path.

Each entry also has a short one-line `purpose`, which helps the AI decide which files to ask for. These start out empty and fill in gradually: when the AI creates a file or changes a file's role, it records the purpose in the index as part of its response, and `ai-bridge apply` saves it. If you'd rather have all purposes filled in right now — or you've edited files by hand — upload `ai-sync-index-skill.md` and ask the AI to sync the index. It works through your files in batches (20 at a time by default).

---

## ⬆️ Updating AI Bridge

The instruction files in `ai-bridge/` come from the version of the tool you have installed. After updating the tool, refresh them:

~~~bash
dotnet tool update --global Tools.AIBridge
ai-bridge migrate
~~~

Then re-upload the instruction files to your AI chat. Your `index.xml` purposes and `.aiignore` rules are kept. If `pack` or `apply` tells you the workspace is outdated, run `ai-bridge migrate`. On a fresh clone of a project that already uses AI Bridge, run `ai-bridge init` — the instruction files are git-ignored, so each machine creates its own.

---

## 📖 Command Reference

| Command | Description |
|---------|-------------|
| `ai-bridge init` | Scaffolds the `ai-bridge/` workspace, `.aiignore`, templates, and the project index |
| `ai-bridge migrate` | Refreshes the workspace templates after updating the tool (keeps your index and `.aiignore`) |
| `ai-bridge apply` | Applies the XML in `ai-response.xml` from the artifacts folder |
| `ai-bridge apply --paste` | Reads the AI response directly from clipboard |
| `ai-bridge apply --watch` | Watches `ai-response.xml` and auto-applies on save |
| `ai-bridge pack` | Optional. Packs source files into `*-context.txt` bundles in `ai-bridge/artifacts/` |

---

## 🛠️ Ecosystem Detection

AI Bridge groups files into **modules** based on your ecosystem — in `index.xml`, in the files it gathers for the AI, and in `pack` output — to keep everything organized:

| Ecosystem | Detected By | Grouping |
|-----------|-------------|----------|
| .NET | `.csproj` files | One module per project |
| Node.js | `package.json` | One module per package |
| Python | `pyproject.toml` | One module per package |
| Go | `go.mod` | One module per module |
| Rust | `Cargo.toml` | One module per crate |
| Other | (fallback) | One module per top-level folder |

---

## 📋 Clipboard Support

`ai-bridge apply --paste` uses native OS commands — **no external dependencies required**.

| Platform | Clipboard Tool | Notes |
|----------|---------------|-------|
| **Windows** | Built-in (`powershell.exe` / `clip.exe`) | Works out of the box. |
| **macOS** | Built-in (`pbpaste` / `pbcopy`) | Works out of the box. |
| **Linux (X11)** | `xclip` | Install: `sudo apt install xclip` |
| **Linux (Wayland)** | `wl-clipboard` | Install: `sudo apt install wl-clipboard` |
| **WSL2** | Windows bridge | Works automatically. |
| **Headless / SSH** | stdin fallback | Prompts you to manually paste into terminal. |

---


## 🤝 Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and how to submit a PR.

## 📄 License

This project is licensed under the [MIT License](LICENSE).
