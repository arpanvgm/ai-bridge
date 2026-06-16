---
name: ai-request-skill
description: >
  Use this skill when you only have the `ai-bridge-index.xml` (the high-level map) and you need to see the full source code of specific files to complete the user's request.
---

# AI Bridge Request Skill

When you are working from the `ai-bridge-index.xml` map, you do not have the full source code for the files. To perform complex coding tasks, you must ask the system to provide the full contents of the specific files you need.

## RESPONSE STRUCTURE

To request file contents, you MUST output a strict XML block. The requested files will be read and provided to you in the next prompt.

```xml
<ai-request>
  <file path="Relative/Path/To/File1.cs" />
  <file path="Relative/Path/To/File2.cs" />
</ai-request>
```

## RULES FOR REQUESTING FILES

1. **Exact Paths:** The `path` attribute must exactly match the `path` attribute found in the `ai-bridge-index.xml` file.
2. **Batch Requests:** Request all the files you think you will need for the task in a single `<ai-request>` block to save time.
3. **No Code Output Yet:** Do not attempt to guess the code or write an `<ai-response>` patch in the same message as an `<ai-request>`. Wait for the file contents to be provided before you write any code modifications.
4. **Markdown Formatting:** Wrap the XML block in a standard markdown ````xml ```` block.

## REPLY FORMAT (what you receive)

The reply wraps requested files in `<module>` blocks, grouped by module:

```
<module name="WebApi" files="1">
<file path="WebApi/Controllers/UserController.cs" lines="62">
// full source code
</file>
</module>

<module name="Shared" files="1">
<file path="Shared/Models/UserProfile.cs" lines="14">
// full source code
</file>
</module>
```

If a requested file and its content are missing from the reply (e.g. it was renamed or
deleted since the index was generated), do not guess its content — tell the user the
file could not be found and ask how to proceed.

## WORKFLOW EXAMPLE

**User:** "Can you add a new API endpoint for fetching user profiles?"
*(User has only provided `ai-bridge-index.xml`)*

**AI:**
I need to see the current Controllers and the User model to implement this.

```xml
<ai-request>
  <file path="WebApi/Controllers/UserController.cs" />
  <file path="Shared/Models/UserProfile.cs" />
</ai-request>
```

**Reply (What you receive in next prompt):**

```
<module name="WebApi" files="1">
<file path="WebApi/Controllers/UserController.cs" lines="62">
// full source code
</file>
</module>

<module name="Shared" files="1">
<file path="Shared/Models/UserProfile.cs" lines="14">
// full source code
</file>
</module>
```

**AI:**
Now has full source for both files, and proceeds to write the `<ai-response>` as per
`ai-response-skill.md` — no further `<ai-request>` needed for this task unless
additional files turn out to be necessary.