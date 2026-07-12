using System.Text;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;

using AIBridge.Cli.Abstractions;

namespace AIBridge.Cli.Services;

public class InputService(IAIBridgeLogger logger, IInputProvider inputProvider)
{
    public async Task<bool> ResolveAsync(string inputFile, bool paste)
    {
        var artifactsDir = Path.GetDirectoryName(inputFile)!;
        if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

        if (!paste)
        {
            if (File.Exists(inputFile))
            {
                logger.Info($"Reading AI response from {FileNames.ResponseXml}.");
                return true;
            }

            logger.Error($"File not found: {FileNames.ResponseXml}");
            logger.Info($"Paste content into the file, or use 'ai-bridge apply --paste'.");
            return false;
        }

        string? content = null;
        try
        {
            content = await inputProvider.GetPrimaryInputAsync();
        }
        catch
        {
            // Suppress clipboard errors — falls back to stdin
        }

        if (!string.IsNullOrWhiteSpace(content))
        {
            await File.WriteAllTextAsync(inputFile, content, Encoding.UTF8);
            logger.Info($"Read AI response from clipboard → saved to {FileNames.ResponseXml}.");
            return true;
        }

        content = await inputProvider.GetFallbackInputAsync("Paste your entire AI response XML below and then press Enter:");

        if (!string.IsNullOrWhiteSpace(content))
        {
            await File.WriteAllTextAsync(inputFile, content, Encoding.UTF8);
            logger.Info($"Read AI response from stdin → saved to {FileNames.ResponseXml}.");
            return true;
        }

        logger.Error("Error: No content received.");
        logger.Info($"Save the AI response to '{FileNames.ResponseXml}' and run 'ai-bridge apply'.");
        return false;
    }

    public async Task ResetInputFileAsync(string inputFile)
    {
        var content = "<!-- Paste the AI response XML here -->\n";
        await File.WriteAllTextAsync(inputFile, content);
        logger.Info($"\nReset {FileNames.ResponseXml} for the next prompt.");
    }
}
