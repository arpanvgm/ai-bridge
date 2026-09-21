
# AI Bridge — MCP Server Workflows

The MCP server (`ai-bridge-mcp`) is an always-on HTTP server. Once running, the AI calls it directly via the MCP protocol — no human copy/paste in the loop. It uses the same Core engine as the CLI.

---

## Command Surface

~~~
ai-bridge-mcp              # start the server (default)
ai-bridge-mcp migrate      # update workspace templates, then exit
~~~

---

## `ai-bridge-mcp` (Start Server)

### Step 1 — Startup and workspace check

~~~mermaid
flowchart TD
    Start["ai-bridge-mcp"]
    Start --> Build["WebApplication.CreateBuilder\nRegister all Core services in DI"]
    Build --> Print["Print OAuth credentials to console:\nClient ID, Client Secret, Token URL, MCP endpoint"]
    Print --> Check["WorkspaceValidator.Check(projectRoot)"]
    Check --> Status{"WorkspaceStatus?"}

    Status -- "NotInitialized" --> AutoInit["WorkspaceSetupService.SetupAsync\nStateService.InitState\nLog: workspace initialized"]
    AutoInit --> StartWeb

    Status -- "VersionMismatch" --> ErrMigrate["Log error:\n'run ai-bridge-mcp migrate'\nReturn — server does NOT start"]

    Status -- "Valid" --> StartWeb["app.RunAsync()\nServer is now accepting connections"]
~~~

**Key difference from CLI:** On `NotInitialized`, the MCP server auto-initializes and continues. On `VersionMismatch`, it exits — same as CLI. There is no `init` subcommand; first run handles it automatically.

---

### Step 2 — OAuth 2.1 Authentication

The server acts as its own Authorization Server, implementing RFC 8414 and RFC 9728.

~~~mermaid
flowchart TD
    Client["AI Client (e.g. Claude.ai)"]
    Client --> Discover["GET /.well-known/oauth-authorization-server\nGET /.well-known/oauth-protected-resource\n→ Server returns metadata with token URL, scopes"]
    Discover --> Authorize["GET /authorize\n(authorization_code flow with PKCE)\n→ Server issues auth code\n→ Redirect back to client"]
    Authorize --> Token["POST /token\n(exchange code for JWT)\nOR\nclient_credentials grant\n(direct secret exchange)"]
    Token --> Verify{"Credentials valid?"}
    Verify -- "No" --> Reject["401 invalid_client\nOR 400 invalid_grant"]
    Verify -- "Yes" --> JWT["Issue signed JWT\n(RSA-SHA256, 1 hour expiry)\nReturn: access_token, token_type, expires_in"]
    JWT --> UseMCP["AI includes Bearer token\nin all /mcp requests"]
~~~

**Ephemeral security model:**
- RSA signing key is generated fresh on every server start — lives in memory only
- Restarting the server invalidates all previously issued tokens
- Client Secret is random by default; pass `--OAuth:ClientSecret="..."` for a stable secret across restarts
- Cloudflare `X-Forwarded-Proto` / `X-Forwarded-Host` headers are automatically used to construct the correct public issuer URL

---

### Step 3 — MCP Tool Invocation

Once authenticated, the AI can call the `apply_ai_response` MCP tool directly.

~~~mermaid
flowchart TD
    AI["AI Client"]
    AI --> POST["POST /mcp\nAuthorization: Bearer <token>\n{method: 'tools/call', params: {name: 'apply_ai_response', ...}}"]
    POST --> Auth["JWT validation\n(signature, expiry, issuer)"]
    Auth --> AuthOk{"Token valid?"}
    AuthOk -- "No" --> Reject["401 Unauthorized"]
    AuthOk -- "Yes" --> Tool["ApplyAiResponseTool.ExecuteAsync"]
    Tool --> Core["ApplyService.ExecuteAsync\n(same engine as CLI apply)"]
    Core --> Branch{"Root element?"}
    Branch -- "<ai-request>" --> RequestFlow["RequestService reads files\nReturns context to AI"]
    Branch -- "<ai-response>" --> ApplyFlow["Write files, apply patches,\ndelete files, update index,\nupdate tracker"]
    RequestFlow --> Return["Return result to AI\nas MCP tool response"]
    ApplyFlow --> Return
    Return --> AI
~~~

**No file reset:** Unlike CLI, the MCP server does not reset `ai-response.xml` after each call — the AI sends content directly via the tool payload, not through the file. The file is not involved in the MCP flow.

---

## `ai-bridge-mcp migrate`

**Purpose:** Updates the local workspace after installing a new version of the tool. Runs outside the web server — no HTTP, no DI container, no OAuth.

~~~mermaid
flowchart TD
    Start["ai-bridge-mcp migrate"]
    Start --> Services["Instantiate Core services directly\n(no DI container)"]
    Services --> Setup["WorkspaceSetupService.SetupAsync(projectRoot)\n• Restore artifacts folder\n• Update .gitignore\n• Patch .dockerignore\n• Append missing .aiignore rules\n• Delete + re-extract all templates\n• Sync index.xml (preserve purposes)"]
    Setup --> Stamp["StateService.InitState\nWrite new version to state.xml"]
    Stamp --> Log["Log: ✅ Migrated\nRe-upload ai-bridge/skills/ to your AI"]
    Log --> Exit(("Exit — server does NOT start"))
~~~

**When is this needed?**
On normal startup, `WorkspaceValidator` detects a version mismatch and refuses to start with: *"run ai-bridge-mcp migrate"*.

---

## Key Differences: CLI vs MCP

| Behaviour | CLI | MCP |
|---|---|---|
| First run (NotInitialized) | Error — run `ai-bridge init` | Auto-initialize, then start server |
| Version mismatch | Error — run `ai-bridge migrate` | Error — run `ai-bridge-mcp migrate` |
| Setup command | `ai-bridge init` or `ai-bridge migrate` | `ai-bridge-mcp migrate` |
| Apply input source | `ai-response.xml` or clipboard/stdin | MCP tool payload (direct from AI) |
| `ai-response.xml` reset after apply | Always | Never (not used in MCP flow) |
| Context returned after `<ai-request>` | Written to file + copied to clipboard | Returned as MCP tool response |
| Runs continuously | Only with `--watch` | Always (it's a server) |
| Multiple projects simultaneously | Yes (stateless) | No (one project per server instance) |

---

## Port and URL Configuration

~~~bash
# Default: http://localhost:5000
ai-bridge-mcp

# Custom port
ai-bridge-mcp --urls "http://localhost:8080"

# Static credentials (recommended for daily use)
ai-bridge-mcp --OAuth:ClientSecret="my-stable-secret"

# Custom client ID and secret
ai-bridge-mcp --OAuth:ClientId="my-project" --OAuth:ClientSecret="my-secret"
~~~

For remote AI clients (e.g. Claude.ai web), expose the server via Cloudflare Tunnel. See `src/AIBridge.Mcp/cloudflared_setup_guide.md`.
