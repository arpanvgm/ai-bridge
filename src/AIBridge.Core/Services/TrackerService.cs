using System.Text;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;

namespace AIBridge.Core.Services;

public class TrackerService(IAIBridgeLogger logger)
{
    /// <summary>
    /// Creates a new tracker.xml from a full &lt;tracker&gt; XML node.
    /// All tasks are initialized with status="todo".
    /// </summary>
    public void HandleCreate(XmlNode root, string projectRoot)
    {
        var trackerFile = Path.Combine(projectRoot, FileNames.TrackerXml);

        if (File.Exists(trackerFile))
            logger.Info("Overwriting existing tracker for new scope.");

        var doc = new XmlDocument();
        var declaration = doc.CreateXmlDeclaration("1.0", "utf-8", null);
        doc.AppendChild(declaration);

        var trackerRoot = doc.CreateElement("tracker");

        var scopeNode = root.SelectSingleNode("scope");
        if (scopeNode != null)
        {
            var imported = doc.ImportNode(scopeNode, deep: true);
            trackerRoot.AppendChild(imported);
        }

        var decisionsNode = root.SelectSingleNode("decisions");
        if (decisionsNode != null)
        {
            var imported = doc.ImportNode(decisionsNode, deep: true);
            trackerRoot.AppendChild(imported);
        }

        var tasksNode = root.SelectSingleNode("tasks");
        if (tasksNode != null)
        {
            var tasksElement = doc.CreateElement("tasks");
            var taskNodes = tasksNode.SelectNodes("task");
            if (taskNodes != null)
            {
                foreach (XmlNode taskNode in taskNodes)
                {
                    var taskElement = doc.CreateElement("task");
                    var id = taskNode.Attributes?["id"]?.Value;
                    if (id != null) taskElement.SetAttribute("id", id);
                    taskElement.SetAttribute("status", "todo");
                    taskElement.InnerText = taskNode.InnerText.Trim();
                    tasksElement.AppendChild(taskElement);
                }
            }
            trackerRoot.AppendChild(tasksElement);
        }

        var focusNode = root.SelectSingleNode("focus");
        if (focusNode != null)
        {
            var focusElement = doc.CreateElement("focus");
            focusElement.InnerText = focusNode.InnerText.Trim();
            trackerRoot.AppendChild(focusElement);
        }

        doc.AppendChild(trackerRoot);
        SaveTrackerXml(doc, trackerFile);

        var taskCount = tasksNode?.SelectNodes("task")?.Count ?? 0;
        var scope = scopeNode?.InnerText.Trim() ?? "No scope specified";
        logger.Success($"✅ Tracker created: \"{scope}\"");
        logger.Info($"   {taskCount} tasks tracked. Focus: Task {focusNode?.InnerText.Trim() ?? "1"}");
    }

    /// <summary>
    /// Applies semantic updates from a &lt;tracker-update&gt; XML node to an existing tracker.xml.
    /// Supports: done, focus, decision (upsert), task (upsert), scope (update).
    /// </summary>
    public void HandleUpdate(XmlNode root, string projectRoot)
    {
        var trackerFile = Path.Combine(projectRoot, FileNames.TrackerXml);
        if (!File.Exists(trackerFile))
        {
            logger.Error("No tracker.xml found. The AI must create a tracker first using <tracker>.");
            return;
        }

        var doc = new XmlDocument();
        try { doc.Load(trackerFile); }
        catch (Exception ex)
        {
            logger.Error($"Error reading tracker.xml: {ex.Message}");
            return;
        }

        var trackerRoot = doc.DocumentElement;
        if (trackerRoot is null || trackerRoot.Name != "tracker")
        {
            logger.Error("tracker.xml is malformed (missing <tracker> root element).");
            return;
        }

        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;

            switch (node.Name)
            {
                case "done":
                    MarkTaskDone(trackerRoot, node.InnerText.Trim());
                    break;

                case "focus":
                    UpsertSimpleElement(doc, trackerRoot, "focus", node.InnerText.Trim());
                    break;

                case "decision":
                    UpsertChildById(doc, trackerRoot, "decisions", "decision", node);
                    break;

                case "task":
                    UpsertTask(doc, trackerRoot, node);
                    break;

                case "scope":
                    UpsertSimpleElement(doc, trackerRoot, "scope", node.InnerText.Trim());
                    break;
            }
        }

        SaveTrackerXml(doc, trackerFile);
        ShowProgress(trackerRoot);
    }

    private void MarkTaskDone(XmlElement trackerRoot, string taskId)
    {
        var task = trackerRoot.SelectSingleNode($"tasks/task[@id='{taskId}']") as XmlElement;
        if (task != null)
            task.SetAttribute("status", "done");
        else
            logger.Warning($"⚠ Task id=\"{taskId}\" not found in tracker.");
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
        if (existing != null)
        {
            existing.InnerText = sourceNode.InnerText.Trim();
        }
        else
        {
            var newElement = doc.CreateElement(childName);
            newElement.SetAttribute("id", id);
            newElement.InnerText = sourceNode.InnerText.Trim();
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
        if (existing != null)
        {
            existing.InnerText = sourceNode.InnerText.Trim();
            // Preserve existing status
        }
        else
        {
            var newTask = doc.CreateElement("task");
            newTask.SetAttribute("id", id);
            newTask.SetAttribute("status", "todo");
            newTask.InnerText = sourceNode.InnerText.Trim();
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
