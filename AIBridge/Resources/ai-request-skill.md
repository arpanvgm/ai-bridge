---
name: ai-request-skill
description: >
  Use this skill when you only have the `ai-bridge-index.xml` (the high-level map) and you need to see the full source code of specific files to complete the user's request.
---

# AI Bridge Request Skill

When you are working from the `ai-bridge-index.xml` map, you do not have the full source code for the files. To perform complex coding tasks, you must ask the system to provide the full contents of the specific files you need.

## RESPONSE STRUCTURE

To request file contents, you MUST output a strict XML block. The `ai-bridge` tool will intercept this block, read the requested files from the user's local disk, and automatically provide them to you in the next prompt.

```xml
<ai-request>
  <file path="Relative/Path/To/File1.cs" />
  <file path="Relative/Path/To/File2.cs" />
</ai-request>
```

## RULES FOR REQUESTING FILES

1. **Exact Paths:** The `path` attribute must exactly match the `path` attribute found in the `ai-bridge-index.xml` file.
2. **Batch Requests:** Request all the files you think you will need for the task in a single `<ai-request>` block to save time.
3. **No Code Output Yet:** Do not attempt to guess the code or write an `<ai-response>` patch in the same message as an `<ai-request>`. Wait for the user's tool to reply with the file contents before you write any code modifications.
4. **Markdown Formatting:** Wrap the XML block in a standard markdown ````xml ```` block.

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
