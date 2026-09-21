
using System.CommandLine;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Helpers;
using AIBridge.Core.Services;
using AIBridge.Mcp.Providers;
using AIBridge.Mcp.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;

var projectRoot = WorkspaceHelper.GetProjectRoot(Environment.CurrentDirectory);

// ── CLI surface ──────────────────────────────────────────────────────────────

var rootCommand = new RootCommand("AI Bridge MCP Server — exposes your local codebase to AI tools via MCP.");

// ── migrate subcommand ───────────────────────────────────────────────────────

var migrateCommand = new Command("migrate", "Updates local AI Bridge workspace templates to match the installed tool version.");
migrateCommand.SetHandler(() =>
{
    var logger = new ConsoleLogger();
    var projectDetector = new ProjectDetector(logger);
    var indexService = new IndexService(logger, projectDetector);
    var templateService = new TemplateService(logger);
    var stateService = new StateService(projectRoot);
    var setupService = new WorkspaceSetupService(logger, templateService, indexService);

    setupService.SetupAsync(projectRoot).GetAwaiter().GetResult();
    stateService.InitState();

    logger.Success("✅ AI Bridge workspace migrated! Please re-upload the files in 'ai-bridge/skills' to your AI.");
});

rootCommand.AddCommand(migrateCommand);

// ── default: start the server ────────────────────────────────────────────────

rootCommand.SetHandler(async () =>
{
    // --- OAuth Configuration ---
    // ClientId and ClientSecret can be supplied via --OAuth:ClientId / --OAuth:ClientSecret,
    // environment variables (OAuth__ClientId, OAuth__ClientSecret), or appsettings.json.
    // If omitted, a secure ephemeral secret is generated.
    var builder = WebApplication.CreateBuilder(args);

    string clientId     = builder.Configuration["OAuth:ClientId"] ?? "ai-bridge-client";
    string clientSecret = builder.Configuration["OAuth:ClientSecret"]
        ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    // Ephemeral RSA key — lives in memory for the server's lifetime.
    // Restarting the server invalidates all previously issued tokens.
    var rsaKey             = RSA.Create(2048);
    var signingKey         = new RsaSecurityKey(rsaKey) { KeyId = Guid.NewGuid().ToString("N") };
    var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

    var serverUrl = (builder.Configuration["urls"] ?? builder.Configuration["Urls"] ?? "http://localhost:5000").TrimEnd('/');

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
                ValidateIssuer          = false,
                ValidateAudience        = false,
                ValidateLifetime        = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey        = signingKey,
            };
        });

    builder.Services.AddAuthorization();

    // --- Register Core Services ---
    builder.Services.AddScoped<StringLogger>();
    builder.Services.AddScoped<IAIBridgeLogger>(sp => sp.GetRequiredService<StringLogger>());
    builder.Services.AddScoped<ProjectDetector>();
    builder.Services.AddScoped<PatcherService>();
    builder.Services.AddScoped<IndexService>();
    builder.Services.AddScoped<RequestService>();
    builder.Services.AddScoped<TrackerService>();
    builder.Services.AddScoped<ApplyService>();
    builder.Services.AddScoped<TemplateService>();
    builder.Services.AddScoped<WorkspaceSetupService>();
    builder.Services.AddScoped<WorkspaceValidator>();
    builder.Services.AddScoped<StateService>(_ => new StateService(projectRoot));

    builder.Services.AddScoped<ApplyAiResponseTool>();

    builder.Services.AddMcpServer().WithTools<ApplyAiResponseTool>().WithHttpTransport();

    var app = builder.Build();

    app.UseAuthentication();
    app.UseAuthorization();

#pragma warning disable CA1861

    // --- RFC 9728: Protected Resource Metadata ---
    app.MapGet("/.well-known/oauth-protected-resource", (HttpContext context) =>
    {
        var (scheme, host) = GetForwardedOrigin(context);
        var issuer = $"{scheme}://{host}";
        return Results.Json(new
        {
            resource                  = $"{issuer}/mcp",
            authorization_servers     = new[] { issuer },
            scopes_supported          = new[] { "mcp:tools" },
            bearer_methods_supported  = new[] { "header" },
        });
    });

    // --- RFC 8414: Authorization Server Metadata ---
    app.MapGet("/.well-known/oauth-authorization-server", (HttpContext context) =>
    {
        var (scheme, host) = GetForwardedOrigin(context);
        var issuer = $"{scheme}://{host}";
        return Results.Json(new
        {
            issuer                                  = issuer,
            authorization_endpoint                  = $"{issuer}/authorize",
            token_endpoint                          = $"{issuer}/token",
            grant_types_supported                   = new[] { "client_credentials", "authorization_code" },
            token_endpoint_auth_methods_supported   = new[] { "none", "client_secret_post" },
            scopes_supported                        = new[] { "mcp:tools" },
            response_types_supported                = new[] { "code" },
            code_challenge_methods_supported        = new[] { "S256" },
        });
    });

#pragma warning restore CA1861

    // --- Authorization Codes (PKCE flow) ---
    var authCodes = new System.Collections.Concurrent.ConcurrentDictionary<string, (string ClientId, string CodeChallenge, DateTime ExpiresAt)>();

    // --- Authorization Endpoint ---
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

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        authCodes[code] = (client_id, code_challenge ?? "", DateTime.UtcNow.AddMinutes(5));

        return Results.Redirect($"{redirect_uri}?code={code}&state={state}");
    });

    // --- Token Endpoint ---
    app.MapPost("/token", async (HttpContext context) =>
    {
        Console.WriteLine("\n[OAuth] Token exchange requested...");

        string grantType = "", reqClientId = "", reqClientSecret = "", code = "", codeVerifier = "";

        if (context.Request.HasFormContentType)
        {
            var form      = await context.Request.ReadFormAsync();
            grantType     = form["grant_type"].ToString();
            reqClientId   = form["client_id"].ToString();
            reqClientSecret = form["client_secret"].ToString();
            code          = form["code"].ToString();
            codeVerifier  = form["code_verifier"].ToString();
        }
        else
        {
            Console.WriteLine($"[OAuth] Error: Unexpected Content-Type: {context.Request.ContentType}");
            return Results.BadRequest(new { error = "invalid_request", error_description = "Expected application/x-www-form-urlencoded" });
        }

        // Fallback: HTTP Basic Auth
        if (string.IsNullOrEmpty(reqClientId) &&
            context.Request.Headers.Authorization.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var authHeader = context.Request.Headers.Authorization.ToString()["Basic ".Length..].Trim();
                var decoded    = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authHeader));
                var parts      = decoded.Split(':', 2);
                if (parts.Length == 2) { reqClientId = parts[0]; reqClientSecret = parts[1]; }
                Console.WriteLine("[OAuth] Extracted client credentials from Basic Auth header.");
            }
            catch { /* ignore decode errors */ }
        }

        Console.WriteLine($"[OAuth] Grant Type: {grantType}");
        Console.WriteLine($"[OAuth] Client ID provided: {!string.IsNullOrEmpty(reqClientId)}");

        if (grantType == "authorization_code")
        {
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

            if (!string.IsNullOrEmpty(request.CodeChallenge))
            {
                var challengeBytes    = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(codeVerifier));
                var expectedChallenge = Base64UrlEncoder.Encode(challengeBytes);
                if (expectedChallenge != request.CodeChallenge)
                {
                    Console.WriteLine($"[OAuth] Error: PKCE verification failed.");
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

        var now            = DateTime.UtcNow;
        var expiresInSecs  = 3600;
        var (scheme, host) = GetForwardedOrigin(context);
        var issuer         = $"{scheme}://{host}";

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           $"{issuer}/mcp",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, reqClientId),
                new Claim("scope", "mcp:tools"),
            ],
            notBefore:          now,
            expires:            now.AddSeconds(expiresInSecs),
            signingCredentials: signingCredentials);

        return Results.Json(new
        {
            access_token = new JwtSecurityTokenHandler().WriteToken(token),
            token_type   = "Bearer",
            expires_in   = expiresInSecs,
            scope        = "mcp:tools",
        });
    });

    // --- MCP Endpoint ---
    app.MapMcp("/mcp").RequireAuthorization();

    // --- Workspace check / auto-init ---
    Console.WriteLine("[Startup] Checking AI Bridge workspace...");

    using (var scope = app.Services.CreateScope())
    {
        var validator = scope.ServiceProvider.GetRequiredService<WorkspaceValidator>();
        var status    = validator.Check(projectRoot);

        if (status == WorkspaceStatus.NotInitialized)
        {
            Console.WriteLine("[Startup] No workspace found. Initializing for the first time...");
            var setupService  = scope.ServiceProvider.GetRequiredService<WorkspaceSetupService>();
            var stateService  = scope.ServiceProvider.GetRequiredService<StateService>();
            await setupService.SetupAsync(projectRoot);
            stateService.InitState();
            Console.WriteLine("[Startup] ✅ Workspace initialized. Please upload 'ai-bridge/skills' files to your AI.");
        }
        else if (status == WorkspaceStatus.VersionMismatch)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Error] AI Bridge workspace is outdated. Please run 'ai-bridge-mcp migrate' to update it.");
            Console.ResetColor();
            return;
        }
        else
        {
            Console.WriteLine("[Startup] Workspace is up to date.");
        }
    }

    await app.RunAsync();
});

return await rootCommand.InvokeAsync(args);

// ── Helpers ──────────────────────────────────────────────────────────────────

static (string scheme, string host) GetForwardedOrigin(HttpContext context)
{
    var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? context.Request.Scheme;
    var host   = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault()  ?? context.Request.Host.ToString();
    return (scheme, host);
}
