---
name: ai-response-xml-format
description: Use this skill for EVERY response whenever the request involves making code changes. Do not respond with plain code blocks, markdown diffs, or inline explanations — always format the response as the XML structure described below using <file>, <patch>, or <delete> elements, so it can be parsed and applied directly.
---

When asked to make code changes, you MUST respond using the exact XML format described below — this output will be parsed and applied directly to the project.

Do NOT output plain code blocks, markdown diffs, or inline explanations of changes. 

You must wrap the entire XML output in exactly ONE standard markdown code block (```xml ... ```) so it can be copied in a single click. Do not break the response into multiple code blocks.

---

## RESPONSE STRUCTURE

Your entire response MUST be wrapped in a single `<ai-response>` root element. Code changes (`<file>`, `<patch>`, `<delete>`) go inside an `<ai-edits>` block. If your changes affect any file's overall purpose in the index, include an `<update-ai-bridge-index>` block after `<ai-edits>` (see `ai-update-index-skill.md` for rules):

```xml
<ai-response>

  <ai-edits>
    <!-- your <file>, <patch>, and <delete> blocks go here -->
  </ai-edits>

  <!-- Optional: only if file purposes changed -->
  <update-ai-bridge-index>
    <!-- index delta as per ai-update-index-skill.md -->
  </update-ai-bridge-index>

</ai-response>
```

This makes the response a well-formed XML document.

---

## THE THREE OUTPUT FORMATS

### Format 1 — Full File: `<file>`

```xml
<file path="Relative/Path/From/WorkspaceRoot/FileName.cs"><![CDATA[
// Complete file contents — every single line, nothing omitted
]]></file>
```

### Format 2 — Smart Patch: `<patch>`

```xml
<patch path="Relative/Path/From/WorkspaceRoot/FileName.cs">
  <search><![CDATA[
EXACT VERBATIM LINES COPIED FROM THE FILE RIGHT NOW
  ]]></search>
  <replace><![CDATA[
THE NEW LINES THAT REPLACE THE SEARCH BLOCK
  ]]></replace>
</patch>
```

### Format 3 — File Deletion: `<delete>`

```xml
<delete path="Relative/Path/From/WorkspaceRoot/OldFile.cs" />
```

> You do not need to handle empty folders left after deletion.

---

## WHY `<![CDATA[...]]>` IS REQUIRED

All code content inside `<file>`, `<search>`, and `<replace>` blocks MUST be wrapped in a CDATA section:

```xml
<![CDATA[
// your code here — < > & " ' are all safe inside CDATA
]]>
```

Without CDATA, characters like `<`, `>`, and `&` in code would break the XML. CDATA tells the XML parser to treat everything inside as plain text.

**One exception:** a CDATA section cannot itself contain the literal three-character sequence `]]>`. If the code you are emitting genuinely contains that sequence (rare — e.g. inside a string literal), split the CDATA at that point: end it with `]]]]>` and immediately reopen with `<![CDATA[>`, so the `]]>` is reconstructed across the boundary rather than terminating the section early.

---

## WHEN TO USE WHICH FORMAT

Follow this decision tree for EVERY file you touch. Do not skip it.

```
Is this a brand-new file (does not exist yet)?
  └─ YES → use <file>

Is the file smaller than 100 lines?
  └─ YES → use <file>  (token saving from patch is negligible on small files)

Are you changing more than 50% of the file's lines (excluding patch context lines)?
  └─ YES → use <file>  (faster and more reliable than many patches)

Are your changes spread across 4 or more separate locations in the file?
  └─ YES → use <file>  (multiple patches on one file become fragile)

Is the target code poorly/inconsistently indented, or are you unsure of the exact whitespace?
  └─ YES → use <file>  (patches will fail if indentation isn't a 100% perfect match)

Are you 100% certain you can reproduce the search text character-for-character?
  └─ NO / ANY DOUBT → use <file>

All of the above are NO?
  └─ use <patch>  ← Only reach here for large files with small, localized changes
```

**Need to remove a file?** Always use `<delete path="..." />`.

**Default when unsure: always prefer `<file>`.** A reliable full file is always better than a fragile patch.

---

## RULES FOR `<file>` BLOCKS

1. **CDATA required** — Always wrap file contents in `<![CDATA[...]]>`.
2. **Complete content only** — Output every single line. Never write `// ... rest of file` or any shortcut. The file is overwritten completely — truncated output destroys real code.
3. **No commentary inside the block** — Only valid source code inside CDATA.
4. **Path format** — Forward slashes, relative to workspace root, exact casing:
   `SectorAnalysis.WebApi/Controllers/SectorsController.cs`

---

## RULES FOR `<patch>` BLOCKS

These rules are critical. A single character difference in `<search>` causes the patch to fail.

### `<patch>` block
1. **CDATA required** — Wrap search text in `<![CDATA[...]]>`.
2. **VERBATIM COPY (CRITICAL)** — Strict string matching is used to locate the search text. If you change indentation by even one space, fix a typo, or reflow a line break inside the `<search>` block, the patch will fail. You MUST copy the lines exactly as they appear in the source context.
3. **Never fix formatting in `<search>`** — If the original code is poorly indented, leave it poorly indented in the `<search>` block. Only fix it in the `<replace>` block.
4. **Minimum 4 lines of context** — Include at least 4 surrounding lines above and below your actual change point. This makes the match unique. If the surrounding code is too small to provide 4 lines of context, use `<file>` instead.
5. **Complete lines only** — Never start or end mid-line.
6. **No line numbers** — Do not include line numbers.

### `<replace>` block
1. **CDATA required** — Wrap replacement text in `<![CDATA[...]]>`.
2. Keep the unchanged context lines from `<search>` exactly as they were.
3. Only the lines you actually want to change should differ from `<search>`.

### One change per `<patch>`
If you need to change two separate locations in the same file, output **two separate `<patch>` blocks** with the same file path.

---

## PATH FORMAT RULES (applies to all formats)

| ❌ Wrong | ✅ Right |
|---------|---------|
| `.\SectorAnalysis.WebApi\Controllers\File.cs` | `SectorAnalysis.WebApi/Controllers/File.cs` |
| `C:\full\absolute\path\File.cs` | `SectorAnalysis.WebApi/Controllers/File.cs` |
| `file.cs` (no folder) | `SectorAnalysis.WebApi/Controllers/File.cs` |
| Wrong casing: `controllers/file.cs` | Exact casing: `Controllers/File.cs` |

---

## MARKDOWN ESCAPING RULE (CRITICAL FOR UI)

If you are writing or updating a Markdown file (like `README.md`), use standard tildes (`~~~`) for code blocks inside the CDATA section instead of triple backticks (` ``` `). 
Using triple backticks inside the XML payload will prematurely close the chat UI's outer markdown block and break the copy button.

---

## COMMON MISTAKES TO AVOID

| ❌ Wrong | ✅ Right |
|---------|---------|
| Missing `<ai-response>` root wrapper | Always wrap everything in `<ai-response>` |
| Missing `<ai-edits>` wrapper around code changes | Always wrap `<file>`, `<patch>`, `<delete>` in `<ai-edits>` |
| Missing `<![CDATA[...]]>` around code | Always use CDATA for all code content |
| Using `<patch>` on a file under 100 lines | Use `<file>` |
| Using `<patch>` on 4+ scattered spots | Use `<file>` |
| Changing indentation inside `<search>` | Copy indentation byte-for-byte |
| Writing `// ... existing code ...` in `<file>` | Include every line |
| Mentioning "please delete X" in plain text | Use `<delete path="X" />` |

---

## EXAMPLE OF A VALID RESPONSE

```xml
<ai-response>

  <ai-edits>

  <file path="SectorAnalysis.SharedContracts/Models/SectorDto.cs"><![CDATA[
namespace SectorAnalysis.SharedContracts.Models;

public record SectorDto(string Code, string Name, decimal Weight);
]]></file>

  <patch path="SectorAnalysis.WebApi/Controllers/SectorsController.cs">
    <search><![CDATA[
        [HttpGet]
        public IActionResult GetAll()
        {
            return Ok(_service.GetSectors());
        }
    }
}
    ]]></search>
    <replace><![CDATA[
        [HttpGet]
        public IActionResult GetAll()
        {
            return Ok(_service.GetSectors());
        }

        [HttpGet("{code}")]
        public IActionResult GetByCode(string code)
        {
            var result = _service.GetSector(code);
            return result is null ? NotFound() : Ok(result);
        }
    }
}
    ]]></replace>
  </patch>

  <delete path="SectorAnalysis.WebApi/Controllers/OldController.cs" />

  </ai-edits>

  <update-ai-bridge-index>
    <module name="SectorAnalysis.SharedContracts">
      <file path="SectorAnalysis.SharedContracts/Models/SectorDto.cs" purpose="Defines the SectorDto record used as the API contract for sector data." />
    </module>
    <delete path="SectorAnalysis.WebApi/Controllers/OldController.cs" />
  </update-ai-bridge-index>

</ai-response>
```

---

## SELF-CHECK BEFORE RESPONDING

Before you output anything, answer these questions:

1. Is the entire response wrapped in `<ai-response>...</ai-response>`?
2. Are all `<file>`, `<patch>`, and `<delete>` blocks inside `<ai-edits>...</ai-edits>`?
3. Is every code block (file contents, search text, replace text) inside `<![CDATA[...]]>`?
4. For every `<file>` block: does it contain the **complete file** with zero truncation?
5. For every `<patch>` block: is the `<search>` text a **character-for-character copy** from the source context (including all original whitespace and indentation)?
6. Did I follow the **decision tree** to choose the right format for each file?
7. Are all paths in **forward-slash format**, relative to the workspace root?
8. Is the entire payload wrapped in EXACTLY ONE ````xml ```` block?
9. Did I use `~~~` instead of backticks for code blocks inside markdown files?
10. If my changes affect any file's overall purpose, did I include an `<update-ai-bridge-index>` block?

If any answer is "no" — fix it before outputting.