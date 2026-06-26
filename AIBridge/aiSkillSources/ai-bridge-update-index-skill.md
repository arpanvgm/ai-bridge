---
name: ai-bridge-update-index
description: >
  Use this skill to refresh an existing `ai-bridge-index.xml` after code changes have
  been made via `ai-response-skill.md` earlier in this conversation. Use this skill when
  the user asks to "update the index", "refresh the index", "sync the index", or
  similar — and an `ai-bridge-index.xml` (the current/baseline version) is available,
  either already in context or provided as an upload.

  This skill does NOT cover generating an index from scratch — for that, use
  `ai-create-index-system-prompt.md`.
---

# AI Bridge Update Index Skill

Produces a refreshed `ai-bridge-index.xml` by applying the net effect of every
`<ai-response>` block emitted in this conversation since the baseline index was
established, then presenting the result to the user.

---

## Inputs

1. **Baseline index** — the current `ai-bridge-index.xml`, either already present in the
   conversation or provided as an upload in this turn. If neither is available, ask the
   user to provide it before proceeding.
2. **Change set** — every past `<ai-response>` block emitted earlier in this conversation
   *after* the point where the baseline index was introduced.

---

## Step-by-step workflow

### Step 1 — Locate the baseline index

Find the most recent `ai-bridge-index.xml` in the conversation (either uploaded by the
user, or previously generated). This is the baseline you will modify.

If no `ai-bridge-index.xml` can be found anywhere in the conversation or uploads, ask
the user to provide the current one before proceeding.

### Step 2 — Build the change set

Scan the conversation for every past `<ai-response>` block that appears **after** the point
the baseline index was introduced. Process them in chronological order (oldest first) —
later changes to the same path supersede earlier ones.

For each `<ai-response>`, classify every child element by path against the baseline
index:

| Element | Effect |
|---|---|
| `<file path="...">` for a path **not** in the baseline index | **Added** |
| `<file path="...">` for a path **already** in the baseline index | **Modified** (full rewrite) |
| `<patch path="...">` | **Modified** (partial change) |
| `<delete path="..." />` | **Deleted** |

Apply these chronologically to a working list, so the net effect is correct even if a
path was touched more than once (e.g. added then later deleted within the change set →
no entry at all; added then later patched → still **Added**, but reflect the patched
content when writing its `purpose`).

### Step 3 — No past `<ai-response>` blocks found

If Step 2 finds nothing — e.g. this is a fresh conversation, or the code changes were
made in a previous session — do not guess. Ask the user to describe what changed, in
either form:
- A plain list of added / removed / modified paths with a short note on each, **or**
- A `*-context.txt`-style delta containing the full content of new/changed files, plus
  an explicit list of any deleted or renamed paths.

Wait for the user's reply before proceeding to Step 4.

### Step 4 — Apply the change set to the index

For each path in the change set:

**Added** — Determine the target `<module>` by finding which existing module in the
baseline index contains files with a matching path prefix. If no existing module is a
clear match, create a new `<module name="...">` block placed after the last existing
module — choose a logical name consistent with the naming conventions used in the
baseline index. Generate `purpose` as a 1–2 sentence summary: factual, third-person,
present tense, based on the new file's content from the past `<ai-response>`.

**Modified** — Locate the existing `<file path="...">` entry and re-evaluate its
`purpose`:
- For a full `<file>` rewrite, regenerate `purpose` from the new content.
- For a `<patch>`, regenerate `purpose` only if the patch changes what the file
  fundamentally does (e.g. adds a new responsibility, changes its role). If the patch is
  a minor/local change (bug fix, small refactor, added parameter) that doesn't change
  the file's overall purpose, leave the existing `purpose` unchanged. If the purpose is unchanged, **do not include this file in your output**.
- If you need the file's current full content to judge this accurately and don't have
  it in context, and `ai-request-skill.md` is available in this conversation, you may
  use it to fetch the file before finalizing the index. Otherwise, make a best-effort
  judgement from the patch diff plus the existing `purpose`.

**Deleted** — Output a `<delete path="..." />` tag for the file. You do not need to worry about the module; the system will automatically locate the file and clean up any empty modules for you.

**Renames** — A past `<ai-response>` represents a rename as a `<delete>` of the old path plus a
`<file>` for the new path; handle it as **Deleted** + **Added** per the rules above.
Optionally, if the new file's content is largely unchanged from the old one's, you may
adapt the old entry's `purpose` for the new entry instead of writing it from scratch —
but a fresh summary is always acceptable too.

### Step 5 — Omit everything else

Because you are generating a delta, you must **omit** any file whose `purpose` did not change. Do not output untouched files, and do not output `<module>` wrappers if you are not adding or modifying any files inside them.

### Step 6 — Assemble and write the output

Formatting rules:
- 2-space indentation, no blank lines between `<file />` entries.
- Escape `"` → `&quot;`, `&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;` inside `purpose`.

Output the index changes using the `<update-ai-bridge-index>` format. This is a highly efficient delta format where you only output the specific files that were added, modified, or deleted. DO NOT output the full index file. This is your ONLY output for this turn.

```xml
<update-ai-bridge-index>
  <!-- Group added/modified files by module -->
  <module name="ModuleName">
    <file path="path/to/AddedOrModified.cs" purpose="New or updated 1-2 sentence purpose." />
  </module>
  
  <!-- Deleted files go anywhere inside the block -->
  <delete path="path/to/DeletedFile.cs" />
</update-ai-bridge-index>
```

---

## Error conditions

| Situation | Action |
|---|---|
| No baseline `ai-bridge-index.xml` available | Ask the user to provide the current one (Step 1) |
| No past `<ai-response>` blocks found since the baseline | Ask the user to describe what changed (Step 3) |
| A `<patch>` touches a path not present in the baseline index | Treat as **Added** — the baseline was apparently incomplete for this path |
| Same path added then deleted within the change set | Net effect: no entry — do not add it |
| Module for a new file's path prefix doesn't exist yet | Create a new `<module name="...">` block for it |