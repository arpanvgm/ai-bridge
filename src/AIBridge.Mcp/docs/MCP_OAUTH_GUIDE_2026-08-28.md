# MCP OAuth 2.1 Implementation Guide

This document explains the transition from custom API key authentication to standard OAuth 2.1, why it was necessary for Claude.ai, how it works, and how to replicate it for any other .NET MCP server.

---

## 1. The Problem: Why Simple API Keys Failed

Initially, the MCP server used a custom ASP.NET Core middleware that intercepted requests to `/mcp` and checked for an `Authorization: Bearer <API-KEY>` header. While perfectly secure for local development, this approach silently failed when connecting to cloud-based AI clients like the **Claude.ai Custom Connector**.

**Why?**
Modern MCP clients strictly enforce the **MCP OAuth 2.1 specification**. When your server returns a `401 Unauthorized` challenge, the AI client does not just blindly prompt the user for an API key. Instead, it automatically queries standard OAuth discovery endpoints (`/.well-known/...`) to figure out how to authenticate. If those endpoints don't exist, the client assumes the server is incompatible and drops the connection.

---

## 2. The Solution: Self-Contained OAuth

Instead of requiring an external Identity Provider (like Auth0, Microsoft Entra, or a database-backed OpenIddict server), we built a **Self-Contained OAuth Server** directly within the MCP application. 

The application now acts as both the **Authorization Server** (issuing tokens) and the **Resource Server** (validating tokens).

### Why We Chose This Approach
1. **Zero External Dependencies:** No need to set up databases or third-party cloud tenants for a local developer tool.
2. **Interactive Flow Support:** Claude.ai web specifically requires the interactive **Authorization Code + PKCE** flow. We built a lightweight `/authorize` endpoint that *auto-approves* the request, completely skipping the need to build a complex HTML login UI.
3. **Cloudflare Tunnel Compatibility:** By dynamically reading `X-Forwarded-*` headers, the server correctly advertises its public internet URLs (e.g., `https://local-ai-bridge...`) instead of `localhost:5000`. Without this, Claude's backend would try to exchange tokens at `localhost:5000` and fail with `mcp_token_exchange_failed`.

---

## 3. How It Works: The Protocol Flow

When Claude.ai connects to the server, here is the exact sequence of HTTP requests happening under the hood:

1. **Discovery:** Claude fetches `GET /.well-known/oauth-authorization-server` to discover where your `/authorize` and `/token` endpoints live.
2. **Authorization Request:** Claude redirects your browser to `GET /authorize?response_type=code&client_id=...&code_challenge=...`.
3. **Auto-Approval:** Our server generates a secure, one-time authorization code, saves the `code_challenge` in memory, and immediately HTTP 302 redirects your browser back to `claude.ai/api/mcp/auth_callback`.
4. **Token Exchange:** Anthropic's backend silently calls `POST /token` providing the code and a `code_verifier`. Our server verifies the PKCE challenge, ensures the client ID matches, and returns a signed **JWT Access Token**.
5. **JSON-RPC Communication:** Claude connects to `POST /mcp` using the JWT as a Bearer token. The server validates the JWT signature (using an in-memory ephemeral RSA key) and accepts the connection.

---

## 4. How to Replicate This (Implementation Template)

If you need to build another MCP server in the future, you can use the following steps to implement this exact architecture in one go.

### Step 1: Add the JWT NuGet Package
Your `.csproj` must reference the Web SDK and include the JWT package for token generation:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- ... -->
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.19.2" />
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="2.2.0" />
  </ItemGroup>
</Project>
```

### Step 2: Full `Program.cs` Template

You can drop this boilerplate into any ASP.NET Core application to instantly add MCP OAuth support.

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. Setup OAuth Credentials & Ephemeral Keys
string clientId = builder.Configuration["OAuth:ClientId"] ?? "default-client";
string clientSecret = builder.Configuration["OAuth:ClientSecret"] 
    ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

var rsaKey = RSA.Create(2048);
var signingKey = new RsaSecurityKey(rsaKey) { KeyId = Guid.NewGuid().ToString("N") };
var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

// 2. Configure JWT Validation (Resource Server)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,   // Disabled so it accepts dynamic cloudflare hosts
            ValidateAudience = false, // Disabled so it accepts dynamic cloudflare hosts
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddMcpServer().WithHttpTransport(); // Register your tools here

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

// 3. OAuth State (Stores auth codes in memory for PKCE flow)
var authCodes = new System.Collections.Concurrent.ConcurrentDictionary<string, (string ClientId, string CodeChallenge, DateTime ExpiresAt)>();

// 4. RFC 9728 & RFC 8414 Discovery Endpoints
app.MapGet("/.well-known/oauth-protected-resource", (HttpContext context) => 
{
    var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? context.Request.Scheme;
    var host = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? context.Request.Host.Value;
    var dynamicIssuer = $"{scheme}://{host}";
    
    return Results.Json(new {
        resource = $"{dynamicIssuer}/mcp",
        authorization_servers = new[] { dynamicIssuer },
        scopes_supported = new[] { "mcp:tools" },
        bearer_methods_supported = new[] { "header" }
    });
});

app.MapGet("/.well-known/oauth-authorization-server", (HttpContext context) => 
{
    var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? context.Request.Scheme;
    var host = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? context.Request.Host.Value;
    var dynamicIssuer = $"{scheme}://{host}";

    return Results.Json(new {
        issuer = dynamicIssuer,
        authorization_endpoint = $"{dynamicIssuer}/authorize",
        token_endpoint = $"{dynamicIssuer}/token",
        grant_types_supported = new[] { "client_credentials", "authorization_code" },
        token_endpoint_auth_methods_supported = new[] { "none", "client_secret_post" },
        scopes_supported = new[] { "mcp:tools" },
        response_types_supported = new[] { "code" },
        code_challenge_methods_supported = new[] { "S256" }
    });
});

// 5. Interactive Authorization Endpoint (Auto-Approve)
app.MapGet("/authorize", (string response_type, string client_id, string redirect_uri, string state, string? code_challenge) =>
{
    if (response_type != "code") return Results.BadRequest("Only 'code' is supported.");
    if (!string.Equals(client_id, clientId, StringComparison.Ordinal)) return Results.BadRequest("Invalid client_id.");

    var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    authCodes[code] = (client_id, code_challenge ?? "", DateTime.UtcNow.AddMinutes(5));

    return Results.Redirect($"{redirect_uri}?code={code}&state={state}");
});

// 6. Token Exchange Endpoint
app.MapPost("/token", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    var grantType = form["grant_type"].ToString();
    var reqClientId = form["client_id"].ToString();
    var reqClientSecret = form["client_secret"].ToString();

    if (grantType == "authorization_code")
    {
        var code = form["code"].ToString();
        var codeVerifier = form["code_verifier"].ToString();

        if (!authCodes.TryRemove(code, out var req) || req.ExpiresAt < DateTime.UtcNow)
            return Results.Json(new { error = "invalid_grant" }, statusCode: 400);

        if (req.ClientId != reqClientId) return Results.Json(new { error = "invalid_client" }, statusCode: 401);

        if (!string.IsNullOrEmpty(req.CodeChallenge))
        {
            var challengeBytes = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(codeVerifier));
            if (Base64UrlEncoder.Encode(challengeBytes) != req.CodeChallenge)
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
        }
    }
    else if (grantType == "client_credentials")
    {
        if (reqClientId != clientId || reqClientSecret != clientSecret)
            return Results.Json(new { error = "invalid_client" }, statusCode: 401);
    }
    else
    {
        return Results.Json(new { error = "unsupported_grant_type" }, statusCode: 400);
    }

    var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? context.Request.Scheme;
    var host = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? context.Request.Host.Value;
    var dynamicIssuer = $"{scheme}://{host}";

    var token = new JwtSecurityToken(
        issuer: dynamicIssuer,
        audience: $"{dynamicIssuer}/mcp",
        claims: [new Claim(JwtRegisteredClaimNames.Sub, reqClientId), new Claim("scope", "mcp:tools")],
        notBefore: DateTime.UtcNow,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: signingCredentials);

    return Results.Json(new {
        access_token = new JwtSecurityTokenHandler().WriteToken(token),
        token_type = "Bearer",
        expires_in = 3600,
        scope = "mcp:tools"
    });
});

// 7. Secure the MCP Endpoint
app.MapMcp("/mcp").RequireAuthorization();

await app.RunAsync();
```
