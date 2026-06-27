---
name: ai-bridge-update-index
description: >
  Formatting reference for the `<update-ai-bridge-index>` block. This block must be included
  inside `<ai-response>` (after `<ai-edits>`) whenever your code changes affect a file's
  overall purpose in the index.
---

# AI Bridge Update Index — Formatting Reference

Whenever you generate an `<ai-response>` containing code changes, evaluate whether any
file's overall purpose in the index has changed. If so, include an `<update-ai-bridge-index>`
block inside `<ai-response>`, placed after `<ai-edits>`.

---

## When to include this block

You only need to evaluate the **changes you just made in the current `<ai-edits>`**.
Do not scan past conversations.

For every `<file>`, `<patch>`, or `<delete>` in your current `<ai-edits>`:

1. **Added Files** (`<file>` for a path not in the baseline index)
   - Determine the target `<module>` by finding an existing module with a matching path prefix. If none fits, create a new `<module name="...">`.
   - Write a 1–2 sentence `purpose` (factual, third-person, present tense) summarizing the new file.

2. **Modified Files** (Full `<file>` rewrite or `<patch>`)
   - Re-evaluate the file's `purpose` based on your changes.
   - If your changes fundamentally alter or expand the file's role, write an updated `purpose`.
   - **Crucial Rule:** If your change is minor/local (bug fix, small refactor, added parameter) and doesn't change the file's overall purpose, **do not include this file**. The delta should only contain files whose purpose has materially changed.

3. **Deleted Files** (`<delete>`)
   - Output a `<delete path="..." />` tag. You do not need to wrap it in a `<module>`.

4. **Renames**
   - Treat as **Deleted** (the old path) + **Added** (the new path).

---

## When to omit this block

If your `<ai-edits>` consists *entirely* of minor changes (bug fixes, small refactors, formatting) and no file's overall purpose needs updating, you may omit the `<update-ai-bridge-index>` block entirely.

---

## CRITICAL: Path Matching

The `path` attribute is the unique identifier for every file in the index. The C# tooling uses exact string matching to find the file.

- When outputting a `<delete path="...">` or updating an existing `<file>`, the path you provide **MUST EXACTLY MATCH** the path as it appears in the original `ai-bridge-index.xml`.
- If the casing is wrong, or if you use backslashes instead of forward slashes, the index update will fail or create duplicate ghost entries.
- Paths must always be relative to the workspace root (e.g. `Folder/Subfolder/File.cs`).

---

## Output format

Because you are generating a delta, you must **omit** any file whose `purpose` did not change. Do not output untouched files, and do not output `<module>` wrappers if you are not adding or modifying any files inside them.

Formatting rules:
- 2-space indentation.
- Escape `"` → `&quot;`, `&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;` inside `purpose`.

Place the block inside `<ai-response>`, after `<ai-edits>`:

```xml
<ai-response>

  <ai-edits>
    ... your code changes ...
  </ai-edits>

  <update-ai-bridge-index>
    <!-- Group added/modified files by module -->
    <module name="ModuleName">
      <file path="path/to/AddedOrModified.cs" purpose="New or updated 1-2 sentence purpose." />
    </module>

    <!-- Deleted files go anywhere inside the block -->
    <delete path="path/to/DeletedFile.cs" />
  </update-ai-bridge-index>

</ai-response>
```

---

## Error conditions

| Situation | Action |
|---|---|
| A new file's path prefix doesn't match any existing module | Create a new `<module name="...">` block for it |
| A `<patch>` touches a path not present in the baseline index | Treat as **Added** — the baseline was apparently incomplete for this path |
| Same path added then deleted within the edits | Net effect: no entry — do not add it |
