using System;
using System.IO;
using System.Text;

namespace AIBridge
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
        /// Priority: file (if real content) → clipboard → stdin.
        /// When --paste is used, file check is skipped.
        /// Returns true if content was resolved successfully; false otherwise.
        /// </summary>
        public static bool Resolve(string inputFile, bool paste)
        {
            var artifactsDir = Path.GetDirectoryName(inputFile)!;
            if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

            // 1. If not --paste, check if the file already has real content
            if (!paste && File.Exists(inputFile))
            {
                var fileText = File.ReadAllText(inputFile);
                if (!string.IsNullOrWhiteSpace(fileText) && !fileText.Trim().StartsWith(Placeholder.TrimEnd()))
                {
                    ConsoleHelper.Info("Read AI response from ai-response.xml.");
                    return true;
                }
            }

            // 2. Try clipboard
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

            // 3. Fall back to stdin
            ConsoleHelper.Info("Paste your AI response XML below and press Enter after the closing tag:");
            Console.WriteLine();
            content = ClipboardHelper.ReadXmlFromStdin();

            if (!string.IsNullOrWhiteSpace(content))
            {
                File.WriteAllText(inputFile, content, Encoding.UTF8);
                ConsoleHelper.Info("Read AI response from stdin → saved to ai-response.xml.");
                return true;
            }

            ConsoleHelper.Error("Error: No content received.");
            ConsoleHelper.Info("To apply changes, save the AI response to 'aiArtifacts/ai-response.xml' and run 'ai-bridge apply'.");
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
