
# AIBridge.Mcp

`AIBridge.Mcp` is an **optional companion** to the [AI Bridge CLI](../../README.md) that runs a local MCP server over Streamable HTTP. Instead of manually copying AI responses and running `ai-bridge apply --paste`, the AI client communicates directly with your codebase through this server — no copy/paste needed.

It exposes a single MCP tool (`apply_ai_response`) that lets AI clients read files, apply code patches, and manage the AI Bridge index, using the same core engine as the CLI.

## Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** installed on your machine.

> [!NOTE]
> On first run, the server automatically initializes the `ai-bridge/` workspace in your project directory — creating the index, scaffolding template folders, and extracting skill files. No separate `init` step is needed.
> Once initialized, load `ai-bridge/skills/ai-mcp-connector.md` into your AI system prompt to instruct the AI to route responses through the MCP tool automatically, instead of outputting XML as plain chat text.

## Installation

**To install:**
~~~bash
dotnet tool install --global Tools.AIBridge.Mcp
~~~

**To update:**
~~~bash
dotnet tool update --global Tools.AIBridge.Mcp
~~~

**To uninstall:**
~~~bash
dotnet tool uninstall --global Tools.AIBridge.Mcp
~~~

**To migrate after an update:**

After updating the tool, your local `ai-bridge/` workspace may be outdated. The server will detect this on startup and refuse to start, telling you to run:

~~~bash
ai-bridge-mcp migrate
~~~

This updates the template folders and skill files to match the newly installed version, preserving your existing `index.xml` purposes and any user-edited files (`.aiignore`, `ai-response.xml`). Once complete, start the server normally.

## Running the Server

Open a terminal, navigate to your project directory, and run:

~~~bash
ai-bridge-mcp
~~~

### Security & OAuth 2.1 Authentication

The server strictly implements the **MCP OAuth 2.1 specification**, acting as its own self-contained Authorization Server.

Because AI Bridge is designed to be opened and closed frequently, **security is ephemeral by default**:
1. **Dynamic RSA Key:** Every time you start the server, it generates a new in-memory RSA signing key. When you stop the server, the key is destroyed. This means **any previously issued JWT tokens become instantly invalid**. When you restart the server, your AI client will need to reconnect/re-authenticate. This ensures your local codebase is only accessible during that specific session.
2. **Dynamic Client Secret:** By default, the server also generates a random Client Secret on startup. For maximum security, you can use this random secret, but you will need to update your AI client connector settings every time.

**Convenience: Static Credentials**
Setting a new secret in your AI client every time is a headache. You can pass a consistent secret on startup instead. Because the RSA key still rotates, your local connection remains highly secure—you will simply need to let the AI client reconnect or re-authenticate without having to copy-paste a new secret.

~~~bash
ai-bridge-mcp --OAuth:ClientSecret="my-secure-secret-key"
~~~

> [!NOTE]
> **Default Client ID:** The Client ID defaults to `ai-bridge-client` unless you override it. You will need this exact string when setting up your AI connector!

> **Tip:** You can automate this using a VS Code Task that securely prompts you for the secret so it is never saved in your source code.
>
> Add this to any project's `.vscode/tasks.json`:
> ~~~json
> {
>     "version": "2.0.0",
>     "tasks": [
>         {
>             "label": "Start AI Bridge MCP Server",
>             "type": "shell",
>             "command": "ai-bridge-mcp",
>             "args": [
>                 "--OAuth:ClientId=ai-bridge-client",
>                 "--OAuth:ClientSecret=${input:mcpClientSecret}"
>             ],
>             "isBackground": true,
>             "presentation": { "reveal": "always", "panel": "new", "clear": true },
>             "problemMatcher": []
>         }
>     ],
>     "inputs": [
>         {
>             "id": "mcpClientSecret",
>             "type": "promptString",
>             "description": "Enter your AI Bridge MCP Client Secret:",
>             "password": true
>         }
>     ]
> }
> ~~~
> Now, whenever you run this task, VS Code will pop up a secure password box for you to paste your secret before launching the server!

## Port Configuration

By default, ASP.NET Core automatically binds the server to port **`5000`** (`http://localhost:5000`).

If port `5000` is already in use, you can override this behavior using the standard ASP.NET Core `--urls` argument:

~~~bash
ai-bridge-mcp --urls "http://localhost:8080"
~~~

## Limitations: Multiple Projects

The standard **AI Bridge CLI** (`apply --paste`) is entirely stateless and can be used in dozens of projects simultaneously with zero friction.

By contrast, the **`ai-bridge-mcp` Server** is designed for one active project at a time.

If you want to connect your AI to multiple projects *simultaneously* via MCP, you must manually manage the networking for each one:
1. Start each server on a **different port** (e.g., `--urls "http://localhost:5001"`).
2. Configure **separate Cloudflare routes** (e.g., `projectA.yourdomain.com` -> `5000`, `projectB.yourdomain.com` -> `5001`).
3. Set up **separate AI Connectors** in your AI client, one for each subdomain/port.

Because the AI needs to know *which* project it is talking to, you cannot share a single MCP connection across multiple codebases.

## Connecting Local AI Clients (e.g., Claude Desktop)

If you are using an AI client that runs locally on your machine, you **do not need a tunnel**. You can connect directly to `localhost`.

Configure your MCP client settings (usually a `claude_desktop_config.json` or similar) to point to the server:

1. **Server URL:** `http://localhost:5000/mcp`
2. **Authentication Type:** OAuth 2.0 (or provide credentials directly if the client expects `client_credentials`)
3. **Client ID:** `ai-bridge-client` (or your custom ID)
4. **Client Secret:** The secret printed in your terminal (or your custom secret)
5. **Token URL:** `http://localhost:5000/token`

## Connecting Remote AI Clients (e.g., Claude.ai Web)

Since Claude.ai is a web-based service, you must first expose your local server securely to the internet. We highly recommend using a **Cloudflare Tunnel (`cloudflared`)**. Please refer to the [Cloudflare Setup Guide](./cloudflared_setup_guide.md).

Once exposed (e.g., `https://local-ai-bridge.yourdomain.com`), configure the Claude.ai Custom Connector as follows:

1. **Server URL:** `https://local-ai-bridge.yourdomain.com/mcp`
2. **Authentication Type:** Select **OAuth 2.0**
3. **Client ID:** `ai-bridge-client` (or your custom ID)
4. **Client Secret:** The secret printed in your terminal (or your custom secret)
5. **Token URL:** `https://local-ai-bridge.yourdomain.com/token`

The server automatically detects Cloudflare's `X-Forwarded-*` headers to dynamically advertise the correct public endpoints during the OAuth discovery flow.

## Testing the Server Locally

You can verify the server is running by manually executing the OAuth `client_credentials` flow using `curl`.

**1. Exchange Client Credentials for a Token:**
~~~bash
TOKEN=$(curl -s -X POST http://localhost:5000/token \
  -d "grant_type=client_credentials" \
  -d "client_id=ai-bridge-client" \
  -d "client_secret=YOUR_SECRET" | jq -r .access_token)
~~~

**2. Send an MCP Request with the Token:**
~~~bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"jsonrpc": "2.0", "id": 1, "method": "tools/list"}'
~~~
If successful, it will return a stream of JSON data containing the schema for the `apply_ai_response` tool.

## Troubleshooting

### `mcp_token_exchange_failed` in Claude.ai
This error means the initial authorization redirect worked, but Claude's backend failed to exchange the authorization code for a token at your `/token` endpoint.
- **Check your terminal logs:** The server logs detailed `[OAuth]` events for every token exchange attempt.
- **Client ID mismatch:** Ensure the Client ID you entered in Claude.ai exactly matches the one you started the server with.
- **Missing Cloudflare Headers:** If you are using a proxy other than Cloudflare, ensure it forwards the `X-Forwarded-Host` and `X-Forwarded-Proto` headers, as the server uses these to dynamically construct the correct issuer URLs.

### HTTP 502 Bad Gateway
If Claude.ai reports a 502 error, your Cloudflare tunnel cannot reach your local server.
- Verify the server is running.
- If you changed the port (e.g., `--urls "http://localhost:8080"`), ensure you updated your Cloudflare Dashboard route to match `localhost:8080`.
