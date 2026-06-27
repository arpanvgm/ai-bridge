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
    }
}
