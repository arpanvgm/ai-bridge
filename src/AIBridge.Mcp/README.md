# AIBridge.Mcp

`AIBridge.Mcp` is a .NET Global Tool that runs a local server using the Model Context Protocol (MCP) over Streamable HTTP. It securely exposes a single tool (`apply_ai_response`) to AI clients, allowing them to read files, apply code patches, and manage the AI Bridge index in your local codebase.

## Running the Server

Once installed globally, open a terminal, navigate to the target codebase directory you want the AI to access, and run:

```bash
ai-bridge-mcp
```

### Security & OAuth 2.1 Authentication

The server strictly implements the **MCP OAuth 2.1 specification**. It acts as its own self-contained Authorization Server, issuing secure JWT tokens. 

By default, every time you start the server, it generates a highly secure **ephemeral Client Secret** and an in-memory RSA signing key. You must provide these OAuth credentials to your AI Client for it to connect successfully.

If you prefer to use consistent credentials so you don't have to reconfigure your AI Client on every restart, you can override the random generation:

```bash
ai-bridge-mcp --OAuth:ClientId="my-custom-client" --OAuth:ClientSecret="my-secure-secret-key"
```

> **Tip:** A great way to use this is to save these arguments inside a specific codebase's `.vscode/tasks.json`. This allows you to launch the server with consistent credentials via a simple 1-click VS Code task.

## Port Configuration

By default, ASP.NET Core automatically binds the server to port **`5000`** (`http://localhost:5000`). 

If port `5000` is already in use, you can override this behavior using the standard ASP.NET Core `--urls` argument:

```bash
ai-bridge-mcp --urls "http://localhost:8080"
```

## Connecting Claude.ai (Custom Connector)

Since Claude.ai is a web-based service, you must first expose your local server securely to the internet. We highly recommend using a **Cloudflare Tunnel (`cloudflared`)**. Please refer to the [Cloudflare Setup Guide](./docs/cloudflared_setup_guide_2026-08-28.md).

Once exposed (e.g., `https://local-ai-bridge.yourdomain.com`), configure the Claude.ai Custom Connector as follows:

1. **Server URL:** `https://local-ai-bridge.yourdomain.com/mcp`
2. **Authentication Type:** Select **OAuth 2.0**
3. **Client ID:** `my-custom-client` (or the default `ai-bridge-client`)
4. **Client Secret:** `my-secure-secret-key` (or the one printed in your terminal)
5. **Token URL:** `https://local-ai-bridge.yourdomain.com/token`

The server automatically detects Cloudflare's `X-Forwarded-*` headers to dynamically advertise the correct public endpoints during the OAuth discovery flow.

## Testing the Server Locally

You can verify the server is running by manually executing the OAuth `client_credentials` flow using `curl`.

**1. Exchange Client Credentials for a Token:**
```bash
TOKEN=$(curl -s -X POST http://localhost:5000/token \
  -d "grant_type=client_credentials" \
  -d "client_id=ai-bridge-client" \
  -d "client_secret=YOUR_SECRET" | jq -r .access_token)
```

**2. Send an MCP Request with the Token:**
```bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"jsonrpc": "2.0", "id": 1, "method": "tools/list"}'
```
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
