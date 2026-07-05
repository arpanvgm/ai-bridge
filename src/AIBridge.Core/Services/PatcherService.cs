using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AIBridge.Core.Abstractions;

namespace AIBridge.Core.Services;

public class PatcherService(IAIBridgeLogger logger)
{
    public async Task<bool> ApplyPatchAsync(
        XmlNode node,
        string projectPath,
        List<string> failedFiles,
        List<XmlNode> failedPatchNodes)
    {
        var relPath = node.Attributes?["path"]?.Value?.Trim();
        if (string.IsNullOrEmpty(relPath))
        {
            logger.Error("Patch failed: missing 'path' attribute on <patch> tag.");
            failedPatchNodes.Add(node);
            return false;
        }

        var absPath = Path.Combine(projectPath, relPath.Replace('/', Path.DirectorySeparatorChar));
        var searchNode = node.SelectSingleNode("search");
        var replaceNode = node.SelectSingleNode("replace");

        if (!File.Exists(absPath) || searchNode == null || replaceNode == null)
        {
            logger.Error($"Patch failed: File not found or invalid XML -> {relPath}");
            failedFiles.Add(relPath);
            failedPatchNodes.Add(node);
            return false;
        }

        var targetContent = Normalize(await File.ReadAllTextAsync(absPath));
        var search = TrimCDATA(Normalize(searchNode.InnerText));
        var replace = TrimCDATA(Normalize(replaceNode.InnerText));

        if (targetContent.Contains(search))
        {
            var index = targetContent.IndexOf(search, StringComparison.Ordinal);
            var updated = string.Concat(
                targetContent.AsSpan(0, index),
                replace,
                targetContent.AsSpan(index + search.Length));

            await File.WriteAllTextAsync(absPath, updated, Encoding.UTF8);
            logger.Success($"Patched: {relPath}");
            return true;
        }
        else if (TryFuzzyPatch(targetContent, search, replace, out var fuzzyResult))
        {
            await File.WriteAllTextAsync(absPath, fuzzyResult, Encoding.UTF8);
            logger.Warning($"Patched (fuzzy): {relPath}");
            return true;
        }
        else
        {
            logger.Error($"Patch failed: Match not found -> {relPath}");
            failedFiles.Add(relPath);
            failedPatchNodes.Add(node);
            return false;
        }
    }

    public static async Task RebuildResponseWithFailedPatchesAsync(string inputFile, List<XmlNode> failedPatchNodes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ai-response>");
        sb.AppendLine("<ai-edits>");
        sb.AppendLine();

        foreach (var node in failedPatchNodes)
        {
            sb.AppendLine(node.OuterXml);
            sb.AppendLine();
        }

        sb.AppendLine("</ai-edits>");
        sb.AppendLine("</ai-response>");
        await File.WriteAllTextAsync(inputFile, sb.ToString(), Encoding.UTF8);
    }

    internal static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");
    internal static string TrimCDATA(string text) => Regex.Replace(Regex.Replace(text, @"^\r?\n", ""), @"\r?\n[ \t]*$", "");

    private static string NormalizeWhitespace(string text) => Regex.Replace(text, @"[ \t]+", " ").Trim();
    private static string NormalizeLineWhitespace(string line) => Regex.Replace(line, @"[ \t]+", " ").Trim();

    private static bool TryFuzzyPatch(string fileContent, string search, string replace, out string result)
    {
        result = fileContent;

        var normalizedFile = NormalizeWhitespace(fileContent);
        var normalizedSearch = NormalizeWhitespace(search);

        if (!normalizedFile.Contains(normalizedSearch))
            return false;

        var fileLines = fileContent.Split('\n');
        var searchLines = search.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToArray();

        if (searchLines.Length == 0) return false;

        var normalizedSearchLines = searchLines.Select(NormalizeLineWhitespace).ToArray();

        for (int i = 0; i <= fileLines.Length - normalizedSearchLines.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < normalizedSearchLines.Length; j++)
            {
                if (NormalizeLineWhitespace(fileLines[i + j].TrimEnd()) != normalizedSearchLines[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                var before = string.Join('\n', fileLines.Take(i));
                var after = string.Join('\n', fileLines.Skip(i + normalizedSearchLines.Length));

                if (before.Length > 0) before += '\n';
                if (after.Length > 0) replace += '\n';

                result = before + replace + after;
                return true;
            }
        }

        return false;
    }
}
