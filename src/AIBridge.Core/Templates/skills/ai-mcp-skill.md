---
name: ai-mcp-connector
description: Instructs you to route your XML output through available MCP tools instead of your chat response.
---

# MCP Connector Protocol

You are connected to the codebase via a custom MCP server or connector.

If an MCP tool is available for applying XML responses to the codebase, you MUST prioritize using it. 

When other skills instruct you to output `<ai-request>` or `<ai-response>` XML blocks, do NOT output them as plain text in your chat response. Instead, invoke the available tool and pass your raw XML string into it.
