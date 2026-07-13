# Contributing to AI Bridge

Thanks for your interest! This is a young, solo-maintained project — contributions are welcome, and the process is intentionally lightweight.

## Building & Running Locally

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download), Git.

```bash
# Clone and build
git clone https://github.com/arpanvgm/ai-bridge.git
cd ai-bridge
dotnet build ai-bridge.slnx

# Run the CLI directly (without installing as a global tool)
dotnet run --project src/AIBridge.Cli -- init
dotnet run --project src/AIBridge.Cli -- pack
```

The solution contains two projects:
- **`src/AIBridge.Cli`** — the CLI entry point (command parsing, I/O, clipboard)
- **`src/AIBridge.Core`** — shared logic (packing, patching, templates, constants)

## Tests

There is no test suite yet. If you add a feature, manual verification is fine for now.

## Coding Conventions

- Target **.NET 10 / C# 13** — use modern language features (file-scoped namespaces, primary constructors, etc.)
- The repo includes AI-agent skill files under `.agents/skills/` with detailed best-practice guidelines for .NET and CLI patterns — skim those if you want to match the existing style.
- Keep the CLI dependency-light; avoid adding NuGet packages unless truly necessary.

## Submitting a PR

1. **Branch from `main`** — use a descriptive name like `feat/incremental-pack` or `fix/clipboard-wsl`.
2. **Keep PRs focused** — one feature or fix per PR.
3. **Describe what and why** in the PR body — a short paragraph is plenty.
4. Make sure `dotnet build ai-bridge.slnx` succeeds with zero warnings before opening.

## Questions?

Open an issue — happy to help.
