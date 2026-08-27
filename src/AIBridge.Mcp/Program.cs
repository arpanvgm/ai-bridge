using AIBridge.Core.Abstractions;
using AIBridge.Core.Services;
using AIBridge.Mcp.Providers;
using AIBridge.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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

app.MapMcp("/mcp"); // Standard MapMcp endpoint

await app.RunAsync();
