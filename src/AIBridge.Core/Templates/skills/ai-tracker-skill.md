---
name: ai-tracker
description: Attach this skill to enable progress tracking across AI chat sessions. When active, the AI creates and maintains a tracker.xml file that records scope, decisions, and task completion — ensuring you can seamlessly resume work in a new chat if usage limits are hit or you switch AI providers.
---

## OVERVIEW

This skill enables **automatic progress tracking** for multi-step work.

1. Use the `<tracker>` block to define the initial scope, decisions, and tasks.
2. With every subsequent code change, include a `<tracker>` block to update task statuses.
3. The tracker file (`tracker.xml`) stays current on disk at all times.

---

## HOW TO USE THE TRACKER

You interact with the tracker by outputting a `<tracker>` block inside your `<ai-response>`. 
Because the tracker is **declarative**, you use the exact same `<tracker>` tag whether you are creating it for the first time or updating existing progress. You only need to output the parts of the tracker you want to add or change.

### Example 1: Creating a new tracker
At the start of a multi-step task, output the full plan:

```xml
<ai-response>
  <tracker>
    <scope>Description of the feature or fix being worked on</scope>
    
    <decisions>
      <decision id="1">First key design or architecture decision</decision>
    </decisions>
    
    <tasks>
      <task id="1" status="todo">First implementation step</task>
      <task id="2" status="todo">Second implementation step</task>
      <task id="3" status="todo">Third implementation step</task>
    </tasks>
    
    <focus>1</focus>
  </tracker>
</ai-response>
```

### Example 2: Updating progress alongside code changes
When you write code and complete tasks, update their status. You can omit `<scope>` and `<decisions>` if they haven't changed. To mark a task as done, just output the `<task>` with `status="done"`.

```xml
<ai-response>
  <ai-edits>
    <!-- your code changes here -->
  </ai-edits>

  <tracker>
    <!-- Mark task 1 as done -->
    <task id="1" status="done" />
    
    <!-- Add a new task dynamically and mark it done -->
    <task id="4" status="done">Add integration tests</task>
    
    <!-- Update focus to the next pending task -->
    <focus>2</focus>
  </tracker>
</ai-response>
```

### Available elements:

| Element | Purpose |
|---------|---------|
| `<task id="N" status="done">` | Add a new task or update an existing task's status/description. Valid statuses: `todo`, `done`. |
| `<focus>N</focus>` | Set current focus to task N. Set to `0` when all tasks are complete. |
| `<decision id="N">text</decision>` | Add or update a design decision. |
| `<scope>text</scope>` | Update the overall objective. |

---

## RESUMING FROM AN EXISTING TRACKER

If the user asks to resume from a tracker, but hasn't provided the tracker file yet, your very first step must be to output an `<ai-request>` exactly like this:

```xml
<ai-request>
  <file path="ai-bridge/artifacts/tracker.xml" />
</ai-request>
```

Once you have the `tracker.xml` file, read it carefully to understand the scope, decisions, what is `done`, what is `todo`, and the current `focus`. Continue the work using `<tracker>` blocks in your responses to keep the progress updated.
