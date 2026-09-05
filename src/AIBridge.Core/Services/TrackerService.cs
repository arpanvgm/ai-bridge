using System;
using System.IO;
using System.Text;
using AIBridge.Core.Abstractions;
using System.Xml;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public interface ITrackerService
{
    void HandleTracker(XmlNode root, string projectRoot);
}

public class TrackerService(IAIBridgeLogger logger) : ITrackerService
{
    public void HandleTracker(XmlNode root, string projectRoot)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectRoot);
        var artifactsDir = Path.Combine(aiWorkspace, FolderNames.Artifacts);
        if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

        var trackerFile = Path.Combine(artifactsDir, FileNames.TrackerXml);
        var doc = new XmlDocument();
        XmlElement trackerRoot;
        bool isNew = false;

        if (File.Exists(trackerFile))
        {
            try { doc.Load(trackerFile); }
            catch (Exception ex)
            {
                logger.Error($"Error reading tracker.xml: {ex.Message}");
                return;
            }
            trackerRoot = doc.DocumentElement!;
        }
        else
        {
            isNew = true;
            trackerRoot = doc.CreateElement(XmlTags.Tracker);
            doc.AppendChild(trackerRoot);
        }

        ProcessTrackerNodes(doc, trackerRoot, root);

        SaveTrackerXml(doc, trackerFile);

        if (isNew)
        {
            logger.Success("✅ Tracker created.");
            logger.Info($"   Saved to: {Path.GetRelativePath(projectRoot, trackerFile)}");
        }
        ShowProgress(trackerRoot);
    }

    private static void ProcessTrackerNodes(XmlDocument doc, XmlElement trackerRoot, XmlNode sourceContainer)
    {
        foreach (XmlNode node in sourceContainer.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;

            switch (node.Name)
            {
                case "scope":
                case "focus":
                    UpsertSimpleElement(doc, trackerRoot, node.Name, node.InnerText.Trim());
                    break;
                case "decisions":
                case "tasks":
                    ProcessTrackerNodes(doc, trackerRoot, node); // Recurse to handle children
                    break;
                case "decision":
                    UpsertChildById(doc, trackerRoot, "decisions", "decision", node);
                    break;
                case "task":
                    UpsertTask(doc, trackerRoot, node);
                    break;
            }
        }
    }

    private static void UpsertSimpleElement(XmlDocument doc, XmlElement trackerRoot, string elementName, string value)
    {
        var existing = trackerRoot.SelectSingleNode(elementName) as XmlElement;
        if (existing != null)
        {
            existing.InnerText = value;
        }
        else
        {
            var newElement = doc.CreateElement(elementName);
            newElement.InnerText = value;
            trackerRoot.AppendChild(newElement);
        }
    }

    private static void UpsertChildById(XmlDocument doc, XmlElement trackerRoot, string parentName, string childName, XmlNode sourceNode)
    {
        var id = sourceNode.Attributes?["id"]?.Value;
        if (string.IsNullOrEmpty(id)) return;

        var parentNode = trackerRoot.SelectSingleNode(parentName);
        if (parentNode is null)
        {
            parentNode = doc.CreateElement(parentName);
            var scopeNode = trackerRoot.SelectSingleNode("scope");
            if (scopeNode != null)
                trackerRoot.InsertAfter(parentNode, scopeNode);
            else
                trackerRoot.PrependChild(parentNode);
        }

        var existing = parentNode.SelectSingleNode($"{childName}[@id='{id}']") as XmlElement;
        var newText = sourceNode.InnerText.Trim();
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(newText))
                existing.InnerText = newText;
        }
        else
        {
            var newElement = doc.CreateElement(childName);
            newElement.SetAttribute("id", id);
            newElement.InnerText = newText;
            parentNode.AppendChild(newElement);
        }
    }

    private static void UpsertTask(XmlDocument doc, XmlElement trackerRoot, XmlNode sourceNode)
    {
        var id = sourceNode.Attributes?["id"]?.Value;
        if (string.IsNullOrEmpty(id)) return;

        var tasksNode = trackerRoot.SelectSingleNode("tasks");
        if (tasksNode is null)
        {
            tasksNode = doc.CreateElement("tasks");
            var focusNode = trackerRoot.SelectSingleNode("focus");
            if (focusNode != null)
                trackerRoot.InsertBefore(tasksNode, focusNode);
            else
                trackerRoot.AppendChild(tasksNode);
        }

        var existing = tasksNode.SelectSingleNode($"task[@id='{id}']") as XmlElement;
        var newStatus = sourceNode.Attributes?["status"]?.Value;
        var newText = sourceNode.InnerText.Trim();

        if (existing != null)
        {
            if (!string.IsNullOrEmpty(newStatus))
                existing.SetAttribute("status", newStatus);
            
            if (!string.IsNullOrEmpty(newText))
                existing.InnerText = newText;
        }
        else
        {
            var newTask = doc.CreateElement("task");
            newTask.SetAttribute("id", id);
            newTask.SetAttribute("status", string.IsNullOrEmpty(newStatus) ? "todo" : newStatus);
            if (!string.IsNullOrEmpty(newText))
                newTask.InnerText = newText;
            tasksNode.AppendChild(newTask);
        }
    }

    private void ShowProgress(XmlElement trackerRoot)
    {
        var allTasks = trackerRoot.SelectNodes("tasks/task");
        var totalTasks = allTasks?.Count ?? 0;
        var completedTasks = 0;

        if (allTasks != null)
        {
            foreach (XmlNode t in allTasks)
            {
                if (t.Attributes?["status"]?.Value == "done")
                    completedTasks++;
            }
        }

        var currentFocus = trackerRoot.SelectSingleNode("focus")?.InnerText.Trim();
        var focusDesc = string.Empty;

        if (currentFocus != null)
        {
            var focusTask = trackerRoot.SelectSingleNode($"tasks/task[@id='{currentFocus}']");
            if (focusTask != null)
                focusDesc = $" ({focusTask.InnerText.Trim()})";
        }

        logger.Success($"📋 Tracker: {completedTasks}/{totalTasks} done → Focus: Task {currentFocus}{focusDesc}");
    }

    private static void SaveTrackerXml(XmlDocument doc, string filePath)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        using var writer = XmlWriter.Create(filePath, settings);
        doc.Save(writer);
    }
}
