---
name: ai-sync-index
description: >
  Use this skill when the user asks you to build or fill missing purposes in the index, OR when the user asks you to review their manual code changes.
---

# AI Bridge Sync Index Skill

This skill allows you to synchronize the `index.xml` with reality. You should use this skill when:
1. The user asks you to build or complete the index (fill in empty purposes).
2. The user tells you they have manually edited or added files.

## How to Synchronize

Whenever you need to synchronize, simply output the following tag inside an `<ai-request>` block:

```xml
<ai-request>
  <out-of-sync-index-files />
</ai-request>
```

The system will automatically scan the codebase and return the full source code for a safe batch of files that either:
- Currently have an empty `purpose=""` in the index.
- Have been manually modified or added.

Once you receive the file contents, read them and output an `<ai-response>` containing an `<update-index>` block to establish or update their purposes. 

If there are more files pending (the system will leave a note at the bottom of its response), simply request `<out-of-sync-index-files />` again to get the next batch. Repeat this loop until the index is fully synchronized!
