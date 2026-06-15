---
name: ai-bridge-context-format
description: >
  Reference skill describing the structure of `*-context.txt` files: each file
  contains one or more `<module name="...">` blocks, and each module contains one or
  more `<file path="..." lines="N">...full file content...</file>` elements. Consult
  this skill whenever a `*-context.txt` file is provided, to correctly extract each
  file's path and content before doing anything else with it.
---

# AI Bridge Context Format

`*-context.txt` files provide full source content for a codebase (or part of one).
This skill describes their structure so the AI can reliably extract every file's path
and content.

## Prerequisite

At least one `*-context.txt` file must be present in the conversation or as an upload.
If none is provided, stop and ask the user to share the `*-context.txt` file(s) before
proceeding — do not guess at codebase structure or generate placeholder output.

---

## Structure

```xml
<module name="ModuleNameA">
  <file path="full/relative/path/from/codebase/root/FileA.ext" lines="N">
... full file content ...
  </file>
  <file path="full/relative/path/from/codebase/root/FileB.ext" lines="N">
... full file content ...
  </file>
</module>

<module name="ModuleNameB">
  <file path="another/full/relative/path/FileC.ext" lines="N">
... full file content ...
  </file>
</module>
```

- A single `*-context.txt` file can contain **multiple `<module>` blocks**, each with
  its own `name`.
- There can be **multiple `*-context.txt` files**, each potentially containing
  multiple `<module>` blocks.

---

## `<module name="...">` — logical grouping only

- `<module name="...">` is purely a **logical partition** used to organize the content
  within a `*-context.txt` file. It has no meaning beyond that.
- The module `name` has **no relationship to folder structure** and **no relationship
  to the `path` attribute** of the `<file>` elements inside it.
- Never infer a file's location, folder, or any part of its `path` from the
  `<module name="...">` it happens to sit under.
- Use the module name only as a label for grouping or summarizing — never for
  resolving, validating, or constructing a file's `path`.

---

## `<file path="..." lines="N">` — the file entry

- **`path`** — the **full relative path of the file from the root of the codebase**.
  This is the canonical identifier for the file. Record it exactly as written
  (including casing and slashes/separators) — this value will be needed for every
  later step that refers back to this file.
- **`lines="N"`** — informational line count only. Do not rely on it for parsing or
  validation.
- The text between the opening and closing `<file>` tags is the **complete,
  unmodified content** of that file — treat it as the full source, not an excerpt.

---

## How to read a `*-context.txt` file

1. Scan for every `<module name="...">...</module>` block — there may be more than
   one.
2. Within each `<module>`, iterate every `<file path="..." lines="N">` element.
3. For each `<file>`, record two things: its `path` (full relative path from the
   codebase root) and its content (everything between the tags).
4. Repeat for every `*-context.txt` file provided.
5. The resulting set of `(path, content)` pairs is the working knowledge of the
   codebase. `path` is the key used to refer back to any specific file in all later
   steps.