using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Services;
using AIBridge.Mcp.Providers;
using AIBridge.Mcp.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- OAuth Configuration ---
// ClientId and ClientSecret can be supplied via --OAuth:ClientId and --OAuth:ClientSecret,
// or through environment variables (OAuth__ClientId, OAuth__ClientSecret),
// or appsettings.json. If omitted, secure ephemeral values are generated.
string clientId = builder.Configuration["OAuth:ClientId"] ?? "ai-bridge-client";
string clientSecret = builder.Configuration["OAuth:ClientSecret"]
    ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

// Ephemeral RSA key for JWT signing — lives in memory for the lifetime of the server.
// A new key is generated every restart, which automatically invalidates all previously issued tokens.
var rsaKey = RSA.Create(2048);
var signingKey = new RsaSecurityKey(rsaKey) { KeyId = Guid.NewGuid().ToString("N") };
var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

// Derive issuer/audience from the server's configured URL
var serverUrl = (builder.Configuration["urls"] ?? builder.Configuration["Urls"] ?? "http://localhost:5000").TrimEnd('/');
var issuer = serverUrl;
var audience = $"{serverUrl}/mcp";

// Print OAuth credentials clearly
Console.WriteLine();
Console.WriteLine(new string('=', 60));
Console.WriteLine("🔒 AI BRIDGE MCP SERVER — OAuth Client Credentials");
Console.WriteLine($"📋 Client ID:     {clientId}");
Console.WriteLine($"🔑 Client Secret: {clientSecret}");
Console.WriteLine($"🌐 Token URL:     {serverUrl}/token");
Console.WriteLine($"🎯 MCP Endpoint:  {serverUrl}/mcp");
Console.WriteLine(new string('=', 60));
Console.WriteLine();

// --- JWT Bearer Authentication ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
        };
    });

builder.Services.AddAuthorization();

// --- Register Core Services (Scoped so each concurrent MCP request gets its own StringLogger) ---
builder.Services.AddScoped<StringLogger>();
builder.Services.AddScoped<IAIBridgeLogger>(sp => sp.GetRequiredService<StringLogger>());
builder.Services.AddScoped<ProjectDetector>();
builder.Services.AddScoped<PatcherService>();
builder.Services.AddScoped<IndexService>();
builder.Services.AddScoped<RequestService>();
builder.Services.AddScoped<TrackerService>();
builder.Services.AddScoped<ApplyService>();

// Register Tool
builder.Services.AddScoped<ApplyAiResponseTool>();

// Configure MCP
builder.Services.AddMcpServer().WithTools<ApplyAiResponseTool>().WithHttpTransport();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

#pragma warning disable CA1861
// --- OAuth State ---
// Stores authorization codes temporarily (in memory) for the PKCE flow
var authCodes = new System.Collections.Concurrent.ConcurrentDictionary<string, (string ClientId, string CodeChallenge, DateTime ExpiresAt)>();

// --- RFC 9728: Protected Resource Metadata ---
// Tells MCP clients which Authorization Server protects this resource
app.MapGet("/.well-known/oauth-protected-resource", (HttpContext context) => 
{
    var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? context.Request.Scheme;
    var host = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? context.Request.Host.Value;
    var dynamicIssuer = $"{scheme}://{host}";
    
    return Results.Json(new
    {
        resource = $"{dynamicIssuer}/mcp",
        authorization_servers = new[] { dynamicIssuer },
        scopes_supported = new[] { "mcp:tools" },
        bearer_methods_supported = new[] { "header" },
    });
});

// --- RFC 8414: Authorization Server Metadata ---
// Tells MCP clients where to exchange credentials for a token
app.MapGet("/.well-known/oauth-authorization-server", (HttpContext context) => 
{
    var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? context.Request.Scheme;
    var host = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? context.Request.Host.Value;
    var dynamicIssuer = $"{scheme}://{host}";

    return Results.Json(new
    {
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
#pragma warning restore CA1861

// --- Authorization Endpoint (Interactive Flow) ---
// Claude.ai redirects the user's browser here. We auto-approve for the configured client_id.
app.MapGet("/authorize", (
    string response_type,
    string client_id,
    string redirect_uri,
    string state,
    string? code_challenge,
    string? code_challenge_method) =>
{
    if (response_type != "code")
        return Results.BadRequest("Unsupported response_type. Only 'code' is supported.");

    if (!string.Equals(client_id, clientId, StringComparison.Ordinal))
        return Results.BadRequest("Invalid client_id.");

    // Generate a secure one-time authorization code
    var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    // Store the PKCE challenge for verification at the /token endpoint
    authCodes[code] = (client_id, code_challenge ?? "", DateTime.UtcNow.AddMinutes(5));

    // Auto-approve and redirect back to Claude.ai
    var redirectUrl = $"{redirect_uri}?code={code}&state={state}";
    return Results.Redirect(redirectUrl);
});

// --- Token Endpoint ---
// Accepts client_id + client_secret OR authorization_code + PKCE verifier, returns a signed JWT
app.MapPost("/token", async (HttpContext context) =>
{
    Console.WriteLine("\n[OAuth] Token exchange requested...");
    
    string grantType = "", reqClientId = "", reqClientSecret = "", code = "", codeVerifier = "";
    
    // Parse form data or JSON
    if (context.Request.HasFormContentType)
    {
        var form = await context.Request.ReadFormAsync();
        grantType = form["grant_type"].ToString();
        reqClientId = form["client_id"].ToString();
        reqClientSecret = form["client_secret"].ToString();
        code = form["code"].ToString();
        codeVerifier = form["code_verifier"].ToString();
    }
    else
    {
        Console.WriteLine($"[OAuth] Error: Unexpected Content-Type: {context.Request.ContentType}");
        return Results.BadRequest(new { error = "invalid_request", error_description = "Expected application/x-www-form-urlencoded" });
    }

    // Fallback: Check for HTTP Basic Auth (many confidential clients use this instead of form body)
    if (string.IsNullOrEmpty(reqClientId) && context.Request.Headers.Authorization.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var authHeader = context.Request.Headers.Authorization.ToString()["Basic ".Length..].Trim();
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authHeader));
            var parts = decoded.Split(':', 2);
            if (parts.Length == 2)
            {
                reqClientId = parts[0];
                reqClientSecret = parts[1];
                Console.WriteLine("[OAuth] Extracted client credentials from Basic Auth header.");
            }
        }
        catch { /* ignore decode errors */ }
    }

    Console.WriteLine($"[OAuth] Grant Type: {grantType}");
    Console.WriteLine($"[OAuth] Client ID provided: {!string.IsNullOrEmpty(reqClientId)}");

    if (grantType == "authorization_code")
    {
        // Validate the authorization code
        if (!authCodes.TryRemove(code, out var request) || request.ExpiresAt < DateTime.UtcNow)
        {
            Console.WriteLine("[OAuth] Error: Code expired or not found.");
            return Results.Json(new { error = "invalid_grant", error_description = "Code expired or invalid." }, statusCode: 400);
        }

        if (request.ClientId != reqClientId)
        {
            Console.WriteLine($"[OAuth] Error: Client ID mismatch. Expected {request.ClientId}, got {reqClientId}");
            return Results.Json(new { error = "invalid_client" }, statusCode: 401);
        }

        // Verify PKCE (if challenge was provided)
        if (!string.IsNullOrEmpty(request.CodeChallenge))
        {
            var challengeBytes = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(codeVerifier));
            var expectedChallenge = Base64UrlEncoder.Encode(challengeBytes);
            
            if (expectedChallenge != request.CodeChallenge)
            {
                Console.WriteLine($"[OAuth] Error: PKCE verification failed. Expected {request.CodeChallenge}, got {expectedChallenge}");
                return Results.Json(new { error = "invalid_grant", error_description = "PKCE verification failed." }, statusCode: 400);
            }
            Console.WriteLine("[OAuth] PKCE verification passed.");
        }
    }
    else if (grantType == "client_credentials")
    {
        if (!string.Equals(reqClientId, clientId, StringComparison.Ordinal) ||
            !string.Equals(reqClientSecret, clientSecret, StringComparison.Ordinal))
        {
            Console.WriteLine("[OAuth] Error: Invalid client_credentials provided.");
            return Results.Json(
                new { error = "invalid_client", error_description = "Invalid client_id or client_secret." },
                statusCode: 401);
        }
    }
    else
    {
        Console.WriteLine($"[OAuth] Error: Unsupported grant type '{grantType}'.");
        return Results.Json(
            new { error = "unsupported_grant_type", error_description = "Only client_credentials and authorization_code are supported." },
            statusCode: 400);
    }

    Console.WriteLine("[OAuth] Token successfully generated.");
    var now = DateTime.UtcNow;
    var expiresInSeconds = 3600;

    var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? context.Request.Scheme;
    var host = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? context.Request.Host.Value;
    var dynamicIssuer = $"{scheme}://{host}";

    var token = new JwtSecurityToken(
        issuer: dynamicIssuer,
        audience: $"{dynamicIssuer}/mcp",
        claims:
        [
            new Claim(JwtRegisteredClaimNames.Sub, reqClientId),
            new Claim("scope", "mcp:tools"),
        ],
        notBefore: now,
        expires: now.AddSeconds(expiresInSeconds),
        signingCredentials: signingCredentials);

    var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Json(new
    {
        access_token = accessToken,
        token_type = "Bearer",
        expires_in = expiresInSeconds,
        scope = "mcp:tools",
    });
});

// --- MCP Endpoint (protected by JWT auth) ---
app.MapMcp("/mcp").RequireAuthorization();

await app.RunAsync();
