using System;
using System.Collections.Generic;

namespace AIBridge.Helpers
{
    public static class FileFilterHelper
    {
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
            var folders = new List<string>();
            var files = new List<string>();

            if (System.IO.File.Exists(aiIgnorePath))
            {
                foreach (var line in System.IO.File.ReadAllLines(aiIgnorePath)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")))
                {
                    var rule = line.Replace("\\", "/");
                    bool isFolder = rule.EndsWith("/");
                    if (isFolder) rule = rule.TrimEnd('/');

                    var regexRule = System.Text.RegularExpressions.Regex.Escape(rule).Replace(@"\*", ".*").Replace(@"\?", ".");
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
                if (aiIgnoreExcludeFolders.Any(f => System.Text.RegularExpressions.Regex.IsMatch(paddedPath, f, System.Text.RegularExpressions.RegexOptions.IgnoreCase))) return true;
                if (aiIgnoreExcludeFilePatterns.Any(p => System.Text.RegularExpressions.Regex.IsMatch(fileName, p, System.Text.RegularExpressions.RegexOptions.IgnoreCase))) return true;
            }
            return false;
        }
    }
}
