# AIBridge.Mcp

`AIBridge.Mcp` is a .NET Global Tool that runs a local server using the Model Context Protocol (MCP) over Streamable HTTP. It securely exposes a single tool (`apply_ai_response`) to AI clients, allowing them to read files, apply code patches, and manage the AI Bridge index in your local codebase.

## Running the Server

Once installed globally (e.g., using the `test MCP locally` VS Code task), open a terminal, navigate to the target codebase directory you want the AI to access, and run:

```bash
ai-bridge-mcp
```

## Security & API Keys

By default, every time you start the server, it generates a highly secure **ephemeral API Key** in memory and prints it to the console. You must provide this key to your AI Client (as a Bearer token) for it to connect successfully.

If you prefer to use a consistent, static API Key so you don't have to reconfigure your AI Client on every restart, you can override the random generation by passing the `--ApiKey` argument:

```bash
ai-bridge-mcp --ApiKey="my-secure-static-key"
```

> **Tip:** A great way to use this is to save the argument inside a specific codebase's `.vscode/tasks.json`. This allows you to launch the server with a consistent API key via a simple 1-click VS Code task, avoiding the need to ever copy and paste the key!

## Port Configuration

By default, ASP.NET Core automatically binds the server to port **`5000`** (`http://localhost:5000`). 

If port `5000` is already in use by another application on your machine, or if you simply want to run it on a different port, you can easily override this behavior using the standard ASP.NET Core `--urls` argument:

```bash
ai-bridge-mcp --urls "http://localhost:8080"
```

## Testing the Server

You can quickly verify the server is running and responding to MCP protocol requests by opening a second terminal and sending a standard `tools/list` JSON-RPC payload using `curl`. 

**Note:** You must replace `YOUR_API_KEY` in the command below with the API key printed to your console when the server started.

```bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_API_KEY" \
  -d '{"jsonrpc": "2.0", "id": 1, "method": "tools/list"}'
```

If the server is running correctly, it will instantly return a stream of JSON data containing the schema for the `apply_ai_response` tool.

## Exposing to the Internet

If you want to securely expose this local MCP server to a web-based AI client (like `claude.ai`) without opening vulnerable ports on your home router, we highly recommend using a **Cloudflare Tunnel (`cloudflared`)** via Docker.

Please refer to the [Cloudflare Setup Guide](./cloudflared_setup_guide.md) for step-by-step instructions. 

> **Important:** If you change the default port using the `--urls` command above, you **must** update your Cloudflare Tunnel dashboard route to match the new port (e.g., `localhost:8080`), otherwise Cloudflare will return a 502 Bad Gateway error!
