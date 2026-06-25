using System;
using System.IO;
using System.Text;
using AIBridge.Helpers;

namespace AIBridge.Core
{
    /// <summary>
    /// Resolves AI response content from multiple sources (file, clipboard, stdin)
    /// and ensures it is saved to ai-response.xml before processing.
    /// </summary>
    public static class InputResolver
    {
        private const string Placeholder = "<!-- Paste the AI response XML here -->";

        /// <summary>
        /// Resolves AI response content into the ai-response.xml file.
        /// Without --paste: reads strictly from the file (no fallback).
        /// With --paste: clipboard → stdin → saves to file.
        /// Returns true if content was resolved successfully; false otherwise.
        /// </summary>
        public static bool Resolve(string inputFile, bool paste)
        {
            var artifactsDir = Path.GetDirectoryName(inputFile)!;
            if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

            // Without --paste: only read from the file
            if (!paste)
            {
                if (File.Exists(inputFile))
                {
                    ConsoleHelper.Info("Reading AI response from ai-response.xml.");
                    return true;
                }

                ConsoleHelper.Error("File not found: aiArtifacts/ai-response.xml");
                ConsoleHelper.Info("Paste content into the file, or use 'ai-bridge apply --paste'.");
                return false;
            }

            // With --paste: try clipboard first
            string? content = null;
            try
            {
                content = ClipboardHelper.GetText();
            }
            catch (Exception ex)
            {
                ConsoleHelper.Warning($"Could not access clipboard: {ex.Message}");
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                File.WriteAllText(inputFile, content, Encoding.UTF8);
                ConsoleHelper.Info("Read AI response from clipboard → saved to ai-response.xml.");
                return true;
            }

            // Fall back to stdin
            ConsoleHelper.Info("Paste your entire AI response XML below and then press Enter:");
            Console.WriteLine();
            content = ClipboardHelper.ReadXmlFromStdin();

            if (!string.IsNullOrWhiteSpace(content))
            {
                File.WriteAllText(inputFile, content, Encoding.UTF8);
                ConsoleHelper.Info("Read AI response from stdin → saved to ai-response.xml.");
                return true;
            }

            ConsoleHelper.Error("Error: No content received.");
            ConsoleHelper.Info("Save the AI response to 'aiArtifacts/ai-response.xml' and run 'ai-bridge apply'.");
            return false;
        }

        /// <summary>
        /// Resets ai-response.xml to the placeholder content to prevent accidental re-application.
        /// </summary>
        public static void ResetInputFile(string inputFile)
        {
            File.WriteAllText(inputFile, Placeholder + "\n");
            ConsoleHelper.Success("✅ Cleared ai-response.xml to prevent accidental re-application.");
        }
    }
}
