using System.Reflection;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class StateService(string projectRoot, IAIBridgeLogger logger)
{
    public static string GetCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version
            ?? Assembly.GetExecutingAssembly().GetName().Version;
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

    public bool EnsureUpToDate()
    {
        var stateFile = GetStateFilePath();
        if (!File.Exists(stateFile))
        {
            logger.Warning("AI Bridge is not initialized in this project.");
            logger.Info("Please run 'ai-bridge init' first.");
            return false;
        }

        var stateDoc = LoadOrCreateState();
        var localVersion = stateDoc.DocumentElement?.GetAttribute("version") ?? "";
        var currentVersion = GetCurrentVersion();

        if (localVersion != currentVersion)
        {
            logger.Warning($"Version mismatch! Tool version is {currentVersion}, but local templates are version {localVersion}.");
            logger.Info("Please run 'ai-bridge update' to sync the templates with the latest tool implementation.");
            logger.Info("Note: This will overwrite any custom changes in the template directories to ensure compatibility.");
            return false;
        }

        return true;
    }

    public void InitState()
    {
        var doc = LoadOrCreateState();
        SetAttribute(doc, "version", GetCurrentVersion());
        var workspaceName = new DirectoryInfo(projectRoot).Name;
        SetAttribute(doc, "workspaceName", workspaceName);

        if (string.IsNullOrEmpty(doc.DocumentElement?.GetAttribute("initializedAt")))
            SetAttribute(doc, "initializedAt", DateTime.UtcNow.ToString("o"));

        SaveState(doc);
    }

    public void UpdateEcosystem(string ecosystem)
    {
        var doc = LoadOrCreateState();
        SetAttribute(doc, "ecosystem", ecosystem);
        SaveState(doc);
    }

    public void UpdateLastPacked()
    {
        var doc = LoadOrCreateState();
        SetAttribute(doc, "lastPackedAt", DateTime.UtcNow.ToString("o"));
        SaveState(doc);
    }
}
