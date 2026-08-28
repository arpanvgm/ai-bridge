# AIBridge.Mcp

`AIBridge.Mcp` is a .NET Global Tool that runs a local server using the Model Context Protocol (MCP) over Streamable HTTP. It securely exposes a single tool (`apply_ai_response`) to AI clients, allowing them to read files, apply code patches, and manage the AI Bridge index in your local codebase.

## Running the Server

Once installed globally (e.g., using the `test MCP locally` VS Code task), open a terminal, navigate to the target codebase directory you want the AI to access, and run:

```bash
ai-bridge-mcp
```

## Port Configuration

By default, ASP.NET Core automatically binds the server to port **`5000`** (`http://localhost:5000`). 

If port `5000` is already in use by another application on your machine, or if you simply want to run it on a different port, you can easily override this behavior using the standard ASP.NET Core `--urls` argument:

```bash
ai-bridge-mcp --urls "http://localhost:8080"
```

## Testing the Server

You can quickly verify the server is running and responding to MCP protocol requests by opening a second terminal and sending a standard `tools/list` JSON-RPC payload using `curl`:

```bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc": "2.0", "id": 1, "method": "tools/list"}'
```

If the server is running correctly, it will instantly return a stream of JSON data containing the schema for the `apply_ai_response` tool.

## Exposing to the Internet

If you want to securely expose this local MCP server to a web-based AI client (like `claude.ai`) without opening vulnerable ports on your home router, we highly recommend using a **Cloudflare Tunnel (`cloudflared`)** via Docker.

Please refer to the [Cloudflare Setup Guide](./cloudflared_setup_guide.md) for step-by-step instructions. 

> **Important:** If you change the default port using the `--urls` command above, you **must** update your Cloudflare Tunnel dashboard route to match the new port (e.g., `localhost:8080`), otherwise Cloudflare will return a 502 Bad Gateway error!
