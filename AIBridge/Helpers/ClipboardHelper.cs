using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AIBridge.Helpers
{
    /// <summary>
    /// Zero-dependency clipboard access across Windows, macOS, Linux (X11/Wayland), and WSL2.
    /// Falls back to stdin when no clipboard provider is available (e.g. headless / interop-disabled WSL2).
    /// </summary>
    public static class ClipboardHelper
    {
        private enum Platform { Windows, MacOS, Wsl2, LinuxWayland, LinuxX11, Unsupported }

        private static readonly Platform CurrentPlatform = DetectPlatform();

        private static Platform DetectPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Platform.Windows;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Platform.MacOS;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // WSL2 check — must come before Wayland/X11.
                // When interop is disabled this file won't exist, so we correctly
                // fall through to the Wayland / X11 paths (which will also fail
                // in a headless WSL2, but the caller handles that via stdin fallback).
                if (File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop"))
                    return Platform.Wsl2;

                // Wayland check
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                    return Platform.LinuxWayland;

                // Default to X11
                return Platform.LinuxX11;
            }

            return Platform.Unsupported;
        }

        /// <summary>
        /// Reads text from the system clipboard. Returns null if clipboard is empty.
        /// Throws if the clipboard provider is missing or fails.
        /// </summary>
        public static string? GetText()
        {
            var (fileName, args) = CurrentPlatform switch
            {
                Platform.Windows      => ("powershell.exe", "-NoProfile -Command Get-Clipboard"),
                Platform.MacOS        => ("pbpaste", ""),
                Platform.Wsl2         => ("powershell.exe", "-NoProfile -Command Get-Clipboard"),
                Platform.LinuxWayland => ("wl-paste", "--no-newline"),
                Platform.LinuxX11     => ("xclip", "-selection clipboard -o"),
                _ => throw new PlatformNotSupportedException(
                    "Clipboard access is not supported on this platform.")
            };

            return RunProcess(fileName, args);
        }

        /// <summary>
        /// Writes text to the system clipboard.
        /// Throws if the clipboard provider is missing or fails.
        /// </summary>
        public static void SetText(string text)
        {
            var (fileName, args) = CurrentPlatform switch
            {
                Platform.Windows      => ("clip.exe", ""),
                Platform.MacOS        => ("pbcopy", ""),
                Platform.Wsl2         => ("clip.exe", ""),
                Platform.LinuxWayland => ("wl-copy", ""),
                Platform.LinuxX11     => ("xclip", "-selection clipboard"),
                _ => throw new PlatformNotSupportedException(
                    "Clipboard access is not supported on this platform.")
            };

            WriteToProcess(fileName, args, text);
        }

        /// <summary>
        /// Returns a human-readable name of the detected clipboard provider.
        /// Useful for diagnostics in error messages.
        /// </summary>
        public static string GetProviderName() => CurrentPlatform switch
        {
            Platform.Windows      => "Windows (powershell.exe / clip.exe)",
            Platform.MacOS        => "macOS (pbpaste / pbcopy)",
            Platform.Wsl2         => "WSL2 (powershell.exe / clip.exe)",
            Platform.LinuxWayland => "Linux/Wayland (wl-paste / wl-copy)",
            Platform.LinuxX11     => "Linux/X11 (xclip)",
            _                     => "Unsupported"
        };

        /// <summary>
        /// Reads XML from stdin line-by-line until the closing &lt;/ai-response&gt; or &lt;/ai-request&gt; tag is found.
        /// The user simply pastes the XML and presses Enter — no Ctrl+D needed.
        /// </summary>
        public static string ReadXmlFromStdin()
        {
            var sb = new System.Text.StringBuilder();
            string? line;
            while ((line = Console.ReadLine()) != null)
            {
                sb.AppendLine(line);
                var trimmed = line.Trim();
                if (trimmed.Equals("</ai-response>", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("</ai-request>", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            return sb.ToString();
        }

        private static string? RunProcess(string fileName, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)
                ?? throw new Exception($"Could not start '{fileName}'. Is it installed and in PATH?");

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                var error = proc.StandardError.ReadToEnd().Trim();
                throw new Exception(
                    $"Clipboard read failed via '{fileName}' (exit code {proc.ExitCode}): {error}");
            }

            // Trim trailing newline that powershell/pbpaste may add
            return string.IsNullOrEmpty(output) ? null : output.TrimEnd('\r', '\n');
        }

        private static void WriteToProcess(string fileName, string args, string text)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)
                ?? throw new Exception($"Could not start '{fileName}'. Is it installed and in PATH?");

            proc.StandardInput.Write(text);
            proc.StandardInput.Close();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                var error = proc.StandardError.ReadToEnd().Trim();
                throw new Exception(
                    $"Clipboard write failed via '{fileName}' (exit code {proc.ExitCode}): {error}");
            }
        }
    }
}
