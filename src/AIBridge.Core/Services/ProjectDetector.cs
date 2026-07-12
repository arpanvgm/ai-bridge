using AIBridge.Core.Abstractions;
using AIBridge.Core.Models;

namespace AIBridge.Core.Services;

public class ProjectDetector(IAIBridgeLogger logger)
{
    private static List<ProjectInfo>? TryDetect(string projectPath, string fileName, Func<string, string> nameSelector, bool excludeRoot = false)
    {
        var files = Directory.GetFiles(projectPath, fileName, SearchOption.AllDirectories);
        if (excludeRoot)
            files = files.Where(p => Path.GetDirectoryName(p) != projectPath).ToArray();

        if (files.Length == 0) return null;
        return files
            .Select(p => new ProjectInfo(
                nameSelector(p),
                Path.GetDirectoryName(p)! + Path.DirectorySeparatorChar))
            .OrderByDescending(p => p.DirectoryPrefix.Length)
            .ToList();
    }

    public (List<ProjectInfo> projects, string ecosystem) DetectProjects(string projectPath)
    {
        var dotnetProjects = TryDetect(projectPath, "*.csproj", p => Path.GetFileNameWithoutExtension(p));
        if (dotnetProjects != null)
        {
            logger.Info("Detected ecosystem: .NET (found .csproj files)");
            return (dotnetProjects, "dotnet");
        }

        var nodeProjects = TryDetect(projectPath, "package.json", p => new DirectoryInfo(Path.GetDirectoryName(p)!).Name, excludeRoot: true);
        if (nodeProjects != null)
        {
            logger.Info("Detected ecosystem: Node.js (found package.json in subfolders)");
            return (nodeProjects, "node");
        }

        var pythonProjects = TryDetect(projectPath, "pyproject.toml", p => new DirectoryInfo(Path.GetDirectoryName(p)!).Name);
        if (pythonProjects != null)
        {
            logger.Info("Detected ecosystem: Python (found pyproject.toml)");
            return (pythonProjects, "python");
        }

        var goProjects = TryDetect(projectPath, "go.mod", p => new DirectoryInfo(Path.GetDirectoryName(p)!).Name);
        if (goProjects != null)
        {
            logger.Info("Detected ecosystem: Go (found go.mod)");
            return (goProjects, "go");
        }

        var rustProjects = TryDetect(projectPath, "Cargo.toml", p => new DirectoryInfo(Path.GetDirectoryName(p)!).Name);
        if (rustProjects != null)
        {
            logger.Info("Detected ecosystem: Rust (found Cargo.toml)");
            return (rustProjects, "rust");
        }

        var topLevelDirs = Directory.GetDirectories(projectPath)
            .Where(d =>
            {
                var name = new DirectoryInfo(d).Name;
                return !name.StartsWith(".") && !name.StartsWith("ai-bridge-")
                    && name != "bin" && name != "obj" && name != "node_modules";
            })
            .Select(d => new ProjectInfo(
                new DirectoryInfo(d).Name,
                d + Path.DirectorySeparatorChar))
            .OrderByDescending(p => p.DirectoryPrefix.Length)
            .ToList();

        logger.Info("No specific ecosystem detected — grouping by top-level folders.");
        return (topLevelDirs, "generic");
    }
}
