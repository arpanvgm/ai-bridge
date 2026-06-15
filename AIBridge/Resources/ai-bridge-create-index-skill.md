---
name: ai-bridge-create-index
description: >
  Use this skill to generate a brand-new `ai-bridge-index.xml` — a lightweight map of an
  entire codebase, grouped by module with a short `purpose` summary per file — from one
  or more `*-context.txt` files containing full source. Use this skill when the user
  provides `*-context.txt` file(s) and asks for a project index/map, or wants one
  generated new index.xml / index file.

  This skill covers from-scratch generation only. To refresh an existing
  `ai-bridge-index.xml` after code changes, use `ai-bridge-update-index-skill.md`
  instead.
---

# AI Bridge Create Index Skill

Generates `ai-bridge-index.xml` — an AI-authored summary of every file in a module — by
reading one or more `*-context.txt` files and writing a concise `purpose` attribute for
each file.

---

## Prerequisite — read `ai-bridge-context-format-skill.md`

Before proceeding, read `ai-bridge-context-format-skill.md`. It defines the structure
of `*-context.txt` files, how to parse them, and will stop the workflow if no context
files are present.

---

## Output format — `ai-bridge-index.xml`


```xml
<?xml version="1.0" encoding="utf-8"?>
<ai-bridge-index>

  <module name="ModuleName">
    <file path="relative/path/to/File.cs" purpose="One or two sentences describing what this file does and why it exists." />
    <file path="..." purpose="..." />
  </module>

  <module name="AnotherModule">
    ...
  </module>

</ai-bridge-index>
```

Rules for the output:
- One `<module>` block per `<module>` found in the input, preserving the same module
  names and the same order they appear in the context files.
- One self-closing `<file />` element per source file with two attributes:
  - `path` — preserved exactly from the source context file (full relative path from
    the codebase root).
  - `purpose` — AI-generated 1–2 sentence description (see summarization guidelines
    below).
- Within each `<module>`, `<file>` entries preserve the same order as in the input.
- No other elements, attributes, or metadata are added.

---

## Step-by-step workflow

### Step 1 — Summarize each file (AI judgement)

For every file parsed from the context files, write a `purpose` attribute value of
**1–2 sentences** that answers:
> "What does this file do and why does it exist in this project?"

**Summarization guidelines by file type:**

| File type | Focus of the purpose |
|---|---|
| `.cs` (C# source) | The class/service responsibility, what it processes or produces |
| `.csproj` | Target framework, NuGet dependencies, project type (Exe/Library) |
| `.json` (appsettings) | Which settings it holds and what they configure |
| `.md` (README / LEARNINGS) | What documentation or knowledge it captures |
| `.slnx` / `.sln` | Which projects it groups together |
| `tasks.json` | What build/run tasks it defines for the IDE |
| Other config/data | Its role in the broader workflow |

**Tone:** factual, third-person, present tense. No padding phrases like "This file is
responsible for…" — start directly with the subject.

**Good example:**
```
Parses daily NSE index CSV downloads and merges new rows into per-index master CSV files,
skipping duplicates based on date.
```

**Bad example:**
```
This file is responsible for handling the processing of files in the system.
```

**Escaping inside the `purpose` attribute:** replace `"` with `&quot;`, `&` with `&amp;`,
`<` with `&lt;`, `>` with `&gt;`.

### Step 2 — Assemble the XML

Build the full `ai-bridge-index.xml` string: XML declaration → `<ai-bridge-index>` →
one `<module>` block per module from the input, in the same order → close.

Use 2-space indentation. No blank lines between `<file />` entries.

Present the assembled `ai-bridge-index.xml` content to the user as a standalone file
they will place into their project themselves.

---

## Example — single module

Given a context file containing one `<module name="DataPipeline">` block with
three files:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ai-bridge-index>

  <module name="DataPipeline">
    <file path="IndexCsvConsolidator/appsettings.json" purpose="Holds folder paths for input downloads, output master CSVs, and the archive destination, along with flags that control overwrite and auto-create behaviour." />
    <file path="IndexCsvConsolidator/IndexCsvConsolidator.csproj" purpose="Defines the console application targeting .NET 10, references CsvHelper and Microsoft.Extensions.Configuration, and ensures appsettings.json is copied to the output directory." />
    <file path="IndexCsvConsolidator/Program.cs" purpose="Entry point that loads configuration, wires up services, and drives the CSV consolidation pipeline." />
  </module>

</ai-bridge-index>
```

Note: the module name `DataPipeline` is a logical label — it does not match the folder
`IndexCsvConsolidator/` in the file paths. Always use the `path` attribute to identify a
file's location, never the module name.

## Example — multiple modules across context files

Suppose `batch-context.txt` contains:

```xml
<module name="DataPipeline">
  <file path="IndexCsvConsolidator/Processor.cs" lines="50">...</file>
</module>
```

and `infra-context.txt` contains:

```xml
<module name="Inventory">
  <file path="WarehouseApi/Service.cs" lines="35">...</file>
</module>
```

Output:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ai-bridge-index>

  <module name="DataPipeline">
    <file path="IndexCsvConsolidator/Processor.cs" purpose="..." />
  </module>

  <module name="Inventory">
    <file path="WarehouseApi/Service.cs" purpose="..." />
  </module>

</ai-bridge-index>
```


---

## Error conditions

| Situation | Action |
|---|---|
| A `<file>` block has empty content | Use `purpose="Empty file — no content to summarize."` |
| A file type is unrecognised | Infer purpose from the path and filename alone |
| `<module name="...">` attribute is missing on a block | Flag this to the user as a malformed context file rather than guessing a name |