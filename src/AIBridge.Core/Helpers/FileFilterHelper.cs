using System.Text.RegularExpressions;

namespace AIBridge.Core.Helpers;

public static class FileFilterHelper
{
    // Paths prefixed with these strings will be unconditionally excluded
    public static readonly string[] AlwaysExcludePrefixes = [$"ai-bridge/", $"ai-bridge-"];

    // Directories to exclude when git is not available
    public static readonly List<string> FallbackExcludeFolders =
    [
        @"[\\/]\.git[\\/]", @"[\\/]\.vs[\\/]", @"[\\/]\.idea[\\/]", @"[\\/]\.vscode[\\/]",
        @"[\\/]bin[\\/]", @"[\\/]obj[\\/]", @"[\\/]node_modules[\\/]",
        @"[\\/]dist[\\/]", @"[\\/]out[\\/]", @"[\\/]build[\\/]",
        @"[\\/]packages[\\/]", @"[\\/]TestResults[\\/]",
        @"[\\/]ai-bridge-[^\\/]+[\\/]",
        @"[\\/]__pycache__[\\/]", @"[\\/]\.mypy_cache[\\/]",
        @"[\\/]target[\\/]", @"[\\/]vendor[\\/]"
    ];

    // File patterns to exclude when git is not available
    public static readonly List<string> FallbackExcludeFilePatterns =
    [
        @"\.g\.cs$", @"\.g\.i\.cs$", @"\.designer\.cs$", @"AssemblyInfo\.cs$",
        @"\.user$", @"\.suo$", @"\.log$", @"\.tmp$"
    ];

    // Binary/non-text extensions to always exclude from packing
    public static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp", ".tiff", ".tif", ".raw",
        // Fonts
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        // Compiled/binary
        ".exe", ".dll", ".pdb", ".so", ".dylib", ".o", ".a", ".lib",
        ".class", ".jar", ".war", ".pyc", ".pyo", ".wasm",
        // Archives
        ".zip", ".tar", ".gz", ".rar", ".7z", ".bz2", ".xz", ".nupkg",
        // Media
        ".mp3", ".mp4", ".avi", ".mov", ".wav", ".flac", ".ogg", ".webm", ".mkv",
        // Documents (binary formats)
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        // Database
        ".db", ".sqlite", ".sqlite3", ".mdb",
        // Certificates & keys
        ".snk", ".pfx", ".p12", ".cer", ".pem",
        // Other binary
        ".bin", ".dat", ".cache", ".coverage"
    };

    // Specific filenames to always exclude (large or not useful for AI context)
    public static readonly HashSet<string> ExcludeFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
        ".DS_Store", "Thumbs.db", ".gitignore", ".dockerignore", ".aiignore", "ai-bridge-index.xml"
    };

    public static (List<string> folders, List<string> files) LoadAiIgnoreRules(string aiIgnorePath)
    {
        List<string> folders = [];
        List<string> files = [];

        if (File.Exists(aiIgnorePath))
        {
            foreach (var line in File.ReadAllLines(aiIgnorePath)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")))
            {
                var rule = line.Replace("\\", "/");
                bool isFolder = rule.EndsWith("/");
                if (isFolder) rule = rule.TrimEnd('/');

                var regexRule = Regex.Escape(rule).Replace(@"\*", ".*").Replace(@"\?", ".");
                if (isFolder) folders.Add($@"[\\/]{regexRule}[\\/]");
                else files.Add($@"^{regexRule}$");
            }
        }

        return (folders, files);
    }

    public static bool IsAiIgnored(string relativePath, string fileName, List<string> aiIgnoreExcludeFolders, List<string> aiIgnoreExcludeFilePatterns)
    {
        if (aiIgnoreExcludeFolders.Count > 0 || aiIgnoreExcludeFilePatterns.Count > 0)
        {
            var paddedPath = "/" + relativePath + "/";
            if (aiIgnoreExcludeFolders.Any(f => Regex.IsMatch(paddedPath, f, RegexOptions.IgnoreCase))) return true;
            if (aiIgnoreExcludeFilePatterns.Any(p => Regex.IsMatch(fileName, p, RegexOptions.IgnoreCase))) return true;
        }
        return false;
    }

    /// <summary>
    /// Retrieves a list of source files to include in the AI context.
    /// It primarily relies on 'git ls-files' to perfectly respect the user's .gitignore 
    /// and avoid packing massive ignored folders (like node_modules or bin/).
    /// If git is not installed or the directory is not a git repository, 
    /// it falls back to a standard recursive directory search using built-in exclusion rules.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the root of the project.</param>
    /// <param name="logger">The logger used to warn if git fallback is activated.</param>
    /// <returns>An array of absolute file paths.</returns>

    public static async Task<string[]> GetTrackedFilesAsync(string projectRoot, AIBridge.Core.Abstractions.IAIBridgeLogger logger)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "ls-files --cached --others --exclude-standard",
                WorkingDirectory = projectRoot,
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    return output
                        .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => Path.GetFullPath(Path.Combine(projectRoot, f)))
                        .ToArray();
                }
            }
        }
        catch { /* ignore */ }

        logger.Warning("⚠ Git not available — using built-in exclusion rules...");
        return Directory.GetFiles(projectRoot, "*.*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var paddedPath = "/" + Path.GetRelativePath(projectRoot, f).Replace("\\", "/") + "/";
                return !FallbackExcludeFolders.Any(pattern => Regex.IsMatch(paddedPath, pattern, RegexOptions.IgnoreCase));
            })
            .Where(f =>
            {
                var fileName = Path.GetFileName(f);
                return !FallbackExcludeFilePatterns.Any(pattern => Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase));
            })
            .ToArray();
    }
}
