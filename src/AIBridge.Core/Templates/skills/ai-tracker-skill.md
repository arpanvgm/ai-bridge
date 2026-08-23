---
name: ai-tracker
description: Attach this skill to enable progress tracking across AI chat sessions. When active, the AI creates and maintains a tracker.xml file that records scope, decisions, and task completion — ensuring you can seamlessly resume work in a new chat if usage limits are hit or you switch AI providers.
---

## OVERVIEW

This skill enables **automatic progress tracking** for multi-step work.

1. At the start of multi-step work, create a **tracker** defining scope, decisions, and tasks.
2. With every code change response, include a **tracker update** marking completed tasks.
3. The tracker file (`tracker.xml`) stays current on disk at all times.

If the chat session ends unexpectedly, the user can resume in any AI chat — the tracker file provides the new AI with the full state of the work.

---

## FORMAT 1 — CREATE TRACKER

When starting new multi-step work, respond with a `<tracker>` block wrapped inside your standard `<ai-response>` element.
Present the tracker to the user and **wait for approval** before starting implementation.

```xml
<ai-response>
  <tracker>
    <scope>Description of the feature, fix, or task this session is working on</scope>

  <decisions>
    <decision id="1">First key design or architecture decision</decision>
    <decision id="2">Second key decision</decision>
  </decisions>

  <tasks>
    <task id="1">First implementation step</task>
    <task id="2">Second implementation step</task>
    <task id="3">Third implementation step</task>
  </tasks>

  <focus>1</focus>
  </tracker>
</ai-response>
```

### Section rules:

- **`<scope>`**: Describe what is being requested to be done by the AI and any relevant context. This does not change frequently in the same chat session.
- **`<decisions>`**: Key design/architecture decisions made during discussion. Sequential IDs starting from 1. Can be omitted entirely if no decisions have been made yet.
- **`<tasks>`**: All implementation steps in execution order. Sequential IDs starting from 1. Do NOT include `status` attributes — all tasks start as `todo` automatically.
- **`<focus>`**: The ID of the first task to work on.

---

## FORMAT 2 — UPDATE TRACKER

After the tracker is created and approved, every `<ai-response>` that contains code changes **MUST** also include a `<tracker-update>` block alongside `<ai-edits>`.

```xml
<ai-response>
  <ai-edits>
    <!-- code changes: <file>, <patch>, <delete> -->
  </ai-edits>

  <tracker-update>
    <done>1</done>
    <focus>2</focus>
  </tracker-update>
</ai-response>
```

### Available update elements:

| Element | Purpose | When to use |
|---------|---------|-------------|
| `<done>N</done>` | Mark task N as completed | After completing a task |
| `<focus>N</focus>` | Set current focus to task N | Always — points to the next task |
| `<decision id="N">text</decision>` | Add or update a decision | When a new decision is made or an existing one changes |
| `<task id="N">text</task>` | Add a new task or update a task description | When scope expands or a task description changes |
| `<scope>text</scope>` | Update the scope | When the overall objective changes (rare) |

### Update rules:

- **MANDATORY**: Every `<ai-response>` with `<ai-edits>` MUST include `<tracker-update>`.
- Include `<done>` for each task completed in this response. You can mark multiple tasks done: `<done>1</done><done>2</done>`.
- Always include `<focus>` pointing to the next task to work on.
- Only include `<decision>`, `<task>`, or `<scope>` when they actually change.
- When all tasks are done, set `<focus>0</focus>` to indicate completion.

---

## RESUMING FROM AN EXISTING TRACKER

If the user provides a `tracker.xml` file in the context, read it carefully:

- **`<scope>`** tells you what work is being done.
- **`<decisions>`** tells you what decisions were already made — follow them.
- **`<tasks>`** with `status="done"` tells you what's already completed — do not redo.
- **`<tasks>`** with `status="todo"` tells you what remains.
- **`<focus>`** tells you exactly where to pick up.

Continue the work using `<tracker-update>` in your responses. Do NOT create a new `<tracker>` — that would overwrite the existing progress.

---

## EXAMPLES

### Example 1: Create tracker for a bug fix

```xml
<ai-response>
  <tracker>
    <scope>Fix login redirect loop when JWT token expires</scope>

  <decisions>
    <decision id="1">Root cause is missing token refresh check before redirect</decision>
  </decisions>

  <tasks>
    <task id="1">Reproduce and identify root cause</task>
    <task id="2">Add token expiry check in auth middleware</task>
    <task id="3">Add unit tests for expiry edge cases</task>
  </tasks>

  <focus>1</focus>
  </tracker>
</ai-response>
```

### Example 2: Implementation response with tracker update

```xml
<ai-response>
  <ai-edits>
    <file path="src/Middleware/AuthMiddleware.cs"><![CDATA[
// ... implementation code ...
    ]]></file>
    <patch path="src/Program.cs">
      <search><![CDATA[
app.UseAuthorization();
      ]]></search>
      <replace><![CDATA[
app.UseMiddleware<AuthMiddleware>();
app.UseAuthorization();
      ]]></replace>
    </patch>
  </ai-edits>

  <tracker-update>
    <done>2</done>
    <focus>3</focus>
    <decision id="1">Return 401 with redirect URL in response body instead of 302 redirect</decision>
  </tracker-update>
</ai-response>
```

### Example 3: Tracker update when multiple tasks are completed

```xml
<tracker-update>
  <done>3</done>
  <done>4</done>
  <focus>5</focus>
  <task id="6">Add integration tests for full auth flow</task>
</tracker-update>
```

### Example 4: Final task completed

```xml
<tracker-update>
  <done>5</done>
  <focus>0</focus>
</tracker-update>
```
