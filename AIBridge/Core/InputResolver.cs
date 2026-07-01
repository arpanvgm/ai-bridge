using System;
using System.IO;
using System.Text;
using AIBridge.Helpers;
using AIBridge.Constants;

namespace AIBridge.Core;

/// <summary>
/// Resolves AI response content from multiple sources (file, clipboard, stdin)
/// and ensures it is saved to ai-response.xml before processing.
/// </summary>
public static class InputResolver
{
    /// <summary>
    /// Resolves AI response content into the ai-response.xml file.
    /// Without --paste: reads strictly from the file (no fallback).
    /// With --paste: clipboard → stdin → saves to file.
    /// Returns true if content was resolved successfully; false otherwise.
    /// </summary>
    public static async Task<bool> ResolveAsync(string inputFile, bool paste)
    {
        var artifactsDir = Path.GetDirectoryName(inputFile)!;
        if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

        // Without --paste: only read from the file
        if (!paste)
        {
            if (File.Exists(inputFile))
            {
                ConsoleHelper.Info($"Reading AI response from {FileNames.ResponseXml}.");
                return true;
            }

            ConsoleHelper.Error($"File not found: {FileNames.ResponseXml}");
            ConsoleHelper.Info($"Paste content into the file, or use 'ai-bridge apply --paste'.");
            return false;
        }

        // With --paste: try clipboard first
        string? content = null;
        try
        {
            content = ClipboardHelper.GetText();
        }
        catch
        {
            // Suppress clipboard errors in isolated environments (e.g. WSL without interop).
            // It will automatically fall back to stdin prompting.
        }

        if (!string.IsNullOrWhiteSpace(content))
        {
            await File.WriteAllTextAsync(inputFile, content, Encoding.UTF8);
            ConsoleHelper.Info($"Read AI response from clipboard → saved to {FileNames.ResponseXml}.");
            return true;
        }

        // Fall back to stdin
        ConsoleHelper.Info("Paste your entire AI response XML below and then press Enter:");
        Console.WriteLine();
        content = ClipboardHelper.ReadXmlFromStdin();

        if (!string.IsNullOrWhiteSpace(content))
        {
            await File.WriteAllTextAsync(inputFile, content, Encoding.UTF8);
            ConsoleHelper.Info($"Read AI response from stdin → saved to {FileNames.ResponseXml}.");
            return true;
        }

        ConsoleHelper.Error("Error: No content received.");
        ConsoleHelper.Info($"Save the AI response to '{FileNames.ResponseXml}' and run 'ai-bridge apply'.");
        return false;
    }

    /// <summary>
    /// Resets ai-response.xml to the placeholder content to prevent accidental re-application.
    /// </summary>
    public static async Task ResetInputFileAsync(string inputFile)
    {
        var content = "<!-- Paste the AI response XML here -->\n";
        await File.WriteAllTextAsync(inputFile, content);
        ConsoleHelper.Info($"\nReset {FileNames.ResponseXml} for the next prompt.");
    }
}
