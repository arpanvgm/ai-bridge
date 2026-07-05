using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AIBridge.Core.Abstractions;

namespace AIBridge.Cli;

public class ConsoleInputProvider : IInputProvider
{
    private enum Platform { Windows, MacOS, Wsl2, LinuxWayland, LinuxX11, Unsupported }

    private static readonly Platform CurrentPlatform = DetectPlatform();

    private static Platform DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Platform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return Platform.MacOS;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop")) return Platform.Wsl2;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return Platform.LinuxWayland;
            return Platform.LinuxX11;
        }

        return Platform.Unsupported;
    }

    public Task<string?> GetClipboardTextAsync()
    {
        var (fileName, clipArgs) = CurrentPlatform switch
        {
            Platform.Windows => ("powershell.exe", "-NoProfile -Command Get-Clipboard"),
            Platform.MacOS => ("pbpaste", ""),
            Platform.Wsl2 => ("powershell.exe", "-NoProfile -Command Get-Clipboard"),
            Platform.LinuxWayland => ("wl-paste", "--no-newline"),
            Platform.LinuxX11 => ("xclip", "-selection clipboard -o"),
            _ => throw new PlatformNotSupportedException("Clipboard access is not supported on this platform.")
        };
        return Task.FromResult(RunProcess(fileName, clipArgs));
    }

    public Task<string?> ReadFromStdinAsync(string prompt)
    {
        Console.WriteLine();
        Console.WriteLine(prompt);
        Console.WriteLine();

        var sb = new StringBuilder();
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            sb.AppendLine(line);
            var trimmed = line.Trim();
            if (trimmed.Equals("</ai-response>", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("</ai-request>", StringComparison.OrdinalIgnoreCase))
                break;
        }
        var result = sb.ToString();
        return Task.FromResult<string?>(string.IsNullOrWhiteSpace(result) ? null : result);
    }

    public Task SetClipboardTextAsync(string text)
    {
        var (fileName, clipArgs) = CurrentPlatform switch
        {
            Platform.Windows => ("clip.exe", ""),
            Platform.MacOS => ("pbcopy", ""),
            Platform.Wsl2 => ("clip.exe", ""),
            Platform.LinuxWayland => ("wl-copy", ""),
            Platform.LinuxX11 => ("xclip", "-selection clipboard"),
            _ => throw new PlatformNotSupportedException("Clipboard access is not supported on this platform.")
        };
        WriteToProcess(fileName, clipArgs, text);
        return Task.CompletedTask;
    }

    private static string? RunProcess(string fileName, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName, Arguments = args,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new Exception($"Could not start '{fileName}'.");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            var error = proc.StandardError.ReadToEnd().Trim();
            throw new Exception($"Clipboard read failed via '{fileName}' (exit code {proc.ExitCode}): {error}");
        }
        return string.IsNullOrEmpty(output) ? null : output.TrimEnd('\r', '\n');
    }

    private static void WriteToProcess(string fileName, string args, string text)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName, Arguments = args,
            RedirectStandardInput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new Exception($"Could not start '{fileName}'.");
        proc.StandardInput.Write(text);
        proc.StandardInput.Close();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            var error = proc.StandardError.ReadToEnd().Trim();
            throw new Exception($"Clipboard write failed via '{fileName}' (exit code {proc.ExitCode}): {error}");
        }
    }
}
