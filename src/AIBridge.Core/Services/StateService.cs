using System.Reflection;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public enum WorkspaceState
{
    NotInitialized,
    Outdated,
    UpToDate
}

public class StateService(string projectRoot)
{
    public static string GetCurrentVersion()
    {
        // Always track the version of AIBridge.Core where the templates actually live.
        var version = typeof(StateService).Assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    private string GetStateFilePath()
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        return Path.Combine(aiWorkspace, "state.xml");
    }

    private XmlDocument LoadOrCreateState()
    {
        var stateFile = GetStateFilePath();
        var doc = new XmlDocument();

        if (File.Exists(stateFile))
        {
            try
            {
                doc.Load(stateFile);
                if (doc.DocumentElement != null && doc.DocumentElement.Name == "ai-bridge-state")
                    return doc;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"XML Parse Error: {ex.Message}");
            }
        }

        var root = doc.CreateElement("ai-bridge-state");
        doc.AppendChild(root);
        return doc;
    }

    private void SaveState(XmlDocument doc)
    {
        var stateFile = GetStateFilePath();
        var dir = Path.GetDirectoryName(stateFile);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        doc.Save(stateFile);
    }

    private static void SetAttribute(XmlDocument doc, string name, string value)
    {
        doc.DocumentElement?.SetAttribute(name, value);
    }

    public WorkspaceState CheckState()
    {
        var stateFile = GetStateFilePath();
        if (!File.Exists(stateFile))
        {
            return WorkspaceState.NotInitialized;
        }

        var stateDoc = LoadOrCreateState();
        var localVersion = stateDoc.DocumentElement?.GetAttribute("version") ?? "";
        var currentVersion = GetCurrentVersion();

        if (localVersion != currentVersion)
        {
            return WorkspaceState.Outdated;
        }

        return WorkspaceState.UpToDate;
    }

    public void InitState()
    {
        var doc = LoadOrCreateState();
        SetAttribute(doc, "version", GetCurrentVersion());
        SaveState(doc);
    }
}
