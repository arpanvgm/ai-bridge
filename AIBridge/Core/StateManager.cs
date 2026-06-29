using System;
using System.IO;
using System.Reflection;
using System.Xml;
using AIBridge.Helpers;

namespace AIBridge.Core
{
    public static class StateManager
    {
        public static string GetCurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        private static string GetStateFilePath()
        {
            var projectPath = WorkspaceHelper.GetProjectRoot();
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
            return Path.Combine(aiWorkspace, "state.xml");
        }
        

        private static XmlDocument LoadOrCreateState()
        {
            var stateFile = GetStateFilePath();
            var doc = new XmlDocument();
            
            if (File.Exists(stateFile))
            {
                try 
                {
                    doc.Load(stateFile);
                    if (doc.DocumentElement != null && doc.DocumentElement.Name == "ai-bridge-state")
                    {
                        return doc;
                    }
                }
                catch { } // Ignore XML parse errors, create fresh
            }

            var root = doc.CreateElement("ai-bridge-state");
            doc.AppendChild(root);
            return doc;
        }
        
        private static void SaveState(XmlDocument doc)
        {
            var stateFile = GetStateFilePath();
            var dir = Path.GetDirectoryName(stateFile);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            doc.Save(stateFile);
        }

        private static void SetAttribute(XmlDocument doc, string name, string value)
        {
            var root = doc.DocumentElement;
            if (root != null)
            {
                root.SetAttribute(name, value);
            }
        }

        public static bool EnsureUpToDate()
        {
            var projectPath = WorkspaceHelper.GetProjectRoot();
            var stateFile = GetStateFilePath();
            if (!File.Exists(stateFile))
            {
                if (Directory.Exists(Path.Combine(projectPath, "aiSkills")))
                {
                    ConsoleHelper.Warning("Version mismatch! Your local aiSkills/ and aiPrompts/ are from an older version of AI Bridge.");
                    ConsoleHelper.Info("Please run 'ai-bridge update' to sync the templates with the latest tool implementation.");
                    return false;
                }
                
                ConsoleHelper.Warning("AI Bridge is not initialized in this project.");
                ConsoleHelper.Info("Please run 'ai-bridge init' first.");
                return false;
            }

            var stateDoc = LoadOrCreateState();
            var localVersion = stateDoc.DocumentElement?.GetAttribute("version") ?? "";
            var currentVersion = GetCurrentVersion();

            if (localVersion != currentVersion)
            {
                ConsoleHelper.Warning($"Version mismatch! Tool version is {currentVersion}, but local templates are version {localVersion}.");
                ConsoleHelper.Info("Please run 'ai-bridge update' to sync the templates with the latest tool implementation.");
                ConsoleHelper.Info("Note: This will overwrite any custom changes in aiSkills/ and aiPrompts/ to ensure compatibility.");
                return false;
            }

            return true;
        }

        public static void InitState()
        {
            var doc = LoadOrCreateState();
            
            SetAttribute(doc, "version", GetCurrentVersion());
            
            var projectPath = WorkspaceHelper.GetProjectRoot();
            var workspaceName = new DirectoryInfo(projectPath).Name;
            SetAttribute(doc, "workspaceName", workspaceName);
            
            if (string.IsNullOrEmpty(doc.DocumentElement?.GetAttribute("initializedAt")))
            {
                SetAttribute(doc, "initializedAt", DateTime.UtcNow.ToString("o"));
            }
            
            SaveState(doc);
        }

        public static void UpdateEcosystem(string ecosystem)
        {
            var doc = LoadOrCreateState();
            SetAttribute(doc, "ecosystem", ecosystem);
            SaveState(doc);
        }

        public static void UpdateLastPacked()
        {
            var doc = LoadOrCreateState();
            SetAttribute(doc, "lastPackedAt", DateTime.UtcNow.ToString("o"));
            SaveState(doc);
        }
    }
}
