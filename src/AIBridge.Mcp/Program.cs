using AIBridge.Core.Abstractions;
using AIBridge.Core.Services;
using AIBridge.Mcp.Providers;
using AIBridge.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.AspNetCore;
using System;
using System.IO;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// --- API Key Management ---
string apiKey = builder.Configuration["ApiKey"] ?? "";
if (string.IsNullOrWhiteSpace(apiKey))
{
    // Generate a secure 32-byte ephemeral key (64 hex characters)
    apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}

// Print the key clearly
Console.WriteLine();
Console.WriteLine(new string('=', 60));
Console.WriteLine("🔒 AI BRIDGE MCP SERVER SECURED");
Console.WriteLine($"🔑 API Key: {apiKey}");
Console.WriteLine("Please configure your AI Client to use this API Key (Bearer token).");
Console.WriteLine("Note: A new ephemeral key is generated every time you start the server.");
Console.WriteLine(new string('=', 60));
Console.WriteLine();
// -------------------------

// Register Core Services (Scoped so each concurrent MCP request gets its own StringLogger)
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

// --- Security Middleware ---
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp"))
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        var expectedHeader = $"Bearer {apiKey}";
        
        if (string.IsNullOrEmpty(authHeader) || !authHeader.Equals(expectedHeader, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 401; // Unauthorized
            await context.Response.WriteAsync("Unauthorized: Invalid or missing API Key.");
            return;
        }
    }
    await next(context);
});
// ---------------------------

app.MapMcp("/mcp"); // Standard MapMcp endpoint

await app.RunAsync();
