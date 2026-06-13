---
name: ai-bridge-index
description: >
  Use this skill to understand the purpose and structure of the `ai-bridge-index.xml` file.
  This file serves as a lightweight map of the entire codebase. You should use this skill when:
  1. Reading an existing `ai-bridge-index.xml` provided by the user to orient yourself before starting work.
  2. Generating an initial `ai-bridge-index.xml` when the user provides `*-context.txt` files.
  3. Updating the `ai-bridge-index.xml` alongside code modifications to keep the summaries up-to-date.
---

# AI Bridge Index Skill

Generates `ai-bridge-index.xml` — an AI-authored summary of every file in a module — by
reading one or more `*-context.txt` files and writing a concise `purpose` attribute for
each file.

---

## Input format

Each `*-context.txt` is an XML-like document with this shape:

```xml
<module name="ModuleName" files="N">
  <file path="relative/path/to/File.cs" lines="38">
    ... full file content ...
  </file>
  <file path="..." lines="...">
    ...
  </file>
</module>
```

Key facts:
- One `*-context.txt` per module/folder (e.g. `IndexCsvConsolidator-context.txt`,
  `Playwright_NSE_Indices-context.txt`, `playwright-nse-indices-root-context.txt`).
- The `<module name="...">` attribute is the logical module name.
- Every `<file path="...">` inside it is one source file with its full content.
- Files can be of any type: `.cs`, `.json`, `.csproj`, `.md`, `.slnx`, `.txt`, etc.

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
- One `<module>` block per context file, using the `name` attribute from `<module>`.
- One self-closing `<file />` element per source file with two attributes:
  - `path` — preserved exactly from the source context file.
  - `purpose` — AI-generated 1–2 sentence description (see summarization guidelines below).
- File order inside each `<module>` must match the order they appear in the context file.
- No other elements, attributes, or metadata are added.

---

## Step-by-step workflow

### Step 1 — Locate the context files

Context files are provided by the user as uploads or inline documents. They are always
named `*-context.txt`. If no context files are present, ask the user to provide them.

If the files are on disk (uploads), read them:

```bash
cat "/mnt/user-data/uploads/MyModule-context.txt"
```

If their content is already in the conversation (inside `<documents>` blocks), read
directly from the context — no disk read needed.

### Step 2 — Parse the structure

For each context file, extract:
- The **module name** from `<module name="...">`.
- Each **file path** from `<file path="...">`.
- The **file content** between the opening and closing `<file>` tags.

You do not need to run code for this — the format is simple enough to parse by reading.

### Step 3 — Summarize each file (AI judgement)

For every file, write a `purpose` attribute value of **1–2 sentences** that answers:
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

### Step 4 — Assemble the XML

Build the full `ai-bridge-index.xml` string in the order: declaration →
`<ai-bridge-index>` open → one `<module>` block per context file (in the order the user
provided them) → close.

Use 2-space indentation. No blank lines between `<file />` entries.

### Step 5 — Write the output file

```python
output = """<?xml version=\"1.0\" encoding=\"utf-8\"?>
<ai-bridge-index>
  ...
</ai-bridge-index>"""

with open("/mnt/user-data/outputs/ai-bridge-index.xml", "w", encoding="utf-8") as f:
    f.write(output)
```

Then present the file to the user with `present_files`.

---

## Handling multiple context files

When the user provides several context files (e.g. one per sub-module plus one for the
solution root), process them all and emit one `<module>` block each. If a context file
contains only infrastructure files (`.vscode/tasks.json`, `.slnx`), still include them —
do not skip any file.

If two context files declare the same `module name`, append a suffix to distinguish them
(e.g. `name="Solution"` → keep as-is; if a second one is also named `"Solution"`, use
`name="Solution.2"`).

---

## Example — minimal output

Given a context file for a module called `IndexCsvConsolidator` with three files:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ai-bridge-index>

  <module name="IndexCsvConsolidator">
    <file path="IndexCsvConsolidator/appsettings.json" purpose="Holds folder paths for input downloads, output master CSVs, and the archive destination, along with flags that control overwrite and auto-create behaviour." />
    <file path="IndexCsvConsolidator/IndexCsvConsolidator.csproj" purpose="Defines the console application targeting .NET 10, references CsvHelper and Microsoft.Extensions.Configuration, and ensures appsettings.json is copied to the output directory." />
    <file path="IndexCsvConsolidator/Program.cs" purpose="Entry point that loads configuration, wires up services, and drives the CSV consolidation pipeline." />
  </module>

</ai-bridge-index>
```

---

## Error conditions

| Situation | Action |
|---|---|
| No context files provided | Ask the user to share the `*-context.txt` file(s) |
| A `<file>` block has empty content | Use `purpose="Empty file — no content to summarize."` |
| A file type is unrecognised | Infer purpose from the path and filename alone |
| Module name attribute is missing | Use the context filename (minus `-context.txt`) as the module name |
