using System.Text;
using System.Xml;
using AIBridge.Core.Abstractions;
using AIBridge.Core.Constants;
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public class IndexService(IAIBridgeLogger logger)
{
    public void HandleCreate(XmlNode root, string projectPath)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
        var indexFile = Path.Combine(aiWorkspace, WorkspaceHelper.GetIndexFileName(projectPath));

        var doc = new XmlDocument();
        var indexRoot = doc.CreateElement("ai-bridge-index");
        indexRoot.SetAttribute("lastUpdated", DateTime.UtcNow.ToString("o"));

        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType == XmlNodeType.Element)
            {
                var importedNode = doc.ImportNode(node, true);
                indexRoot.AppendChild(importedNode);
            }
        }
        doc.AppendChild(indexRoot);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(false)
        };

        using (var writer = XmlWriter.Create(indexFile, settings))
        {
            doc.Save(writer);
        }

        logger.Success("✅ Generated new ai-bridge-index.xml successfully.");
    }

    public void HandleUpdate(XmlNode root, string projectPath)
    {
        var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
        var indexFileName = WorkspaceHelper.GetIndexFileName(projectPath);
        var indexFile = Path.Combine(aiWorkspace, indexFileName);

        if (!File.Exists(indexFile))
        {
            var emptyDoc = new XmlDocument();
            var emptyRoot = emptyDoc.CreateElement("ai-bridge-index");
            emptyDoc.AppendChild(emptyRoot);
            emptyDoc.Save(indexFile);
        }

        var xml = new XmlDocument();
        try { xml.Load(indexFile); }
        catch (Exception ex)
        {
            logger.Error($"Error parsing existing {indexFileName}: {ex.Message}");
            return;
        }

        var indexRoot = xml.DocumentElement;
        if (indexRoot == null || indexRoot.Name != "ai-bridge-index")
        {
            logger.Error($"Error: {indexFileName} is malformed (missing <ai-bridge-index> root).");
            return;
        }

        int updatedCount = 0, addedCount = 0, deletedCount = 0;

        var deleteNodes = root.SelectNodes($"//{XmlTags.Delete}");
        if (deleteNodes != null)
        {
            foreach (XmlNode delNode in deleteNodes)
            {
                var path = delNode.Attributes?["path"]?.Value;
                if (!string.IsNullOrEmpty(path))
                {
                    var targetNode = indexRoot.SelectSingleNode($"//file[@path='{path}']");
                    if (targetNode != null)
                    {
                        var moduleNode = targetNode.ParentNode;
                        moduleNode?.RemoveChild(targetNode);
                        deletedCount++;
                        if (moduleNode != null && moduleNode.SelectNodes(XmlTags.File)?.Count == 0)
                            indexRoot.RemoveChild(moduleNode);
                    }
                }
            }
        }

        var moduleNodes = root.SelectNodes(XmlTags.Module);
        if (moduleNodes != null)
        {
            foreach (XmlNode moduleNode in moduleNodes)
            {
                var moduleName = moduleNode.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(moduleName)) continue;

                var targetModule = indexRoot.SelectSingleNode($"{XmlTags.Module}[@name='{moduleName}']");
                if (targetModule == null)
                {
                    targetModule = xml.CreateElement(XmlTags.Module);
                    var nameAttr = xml.CreateAttribute("name");
                    nameAttr.Value = moduleName;
                    targetModule.Attributes?.Append(nameAttr);
                    indexRoot.AppendChild(targetModule);
                }

                foreach (XmlNode fileNode in moduleNode.SelectNodes(XmlTags.File)!)
                {
                    var path = fileNode.Attributes?["path"]?.Value;
                    if (string.IsNullOrEmpty(path)) continue;

                    var purpose = fileNode.Attributes?["purpose"]?.Value;
                    if (string.IsNullOrEmpty(purpose))
                        purpose = fileNode.InnerText.Trim();

                    var targetFile = targetModule?.SelectSingleNode($"file[@path='{path}']") as XmlElement;
                    if (targetFile != null)
                    {
                        targetFile.SetAttribute("purpose", purpose);
                        updatedCount++;
                    }
                    else
                    {
                        targetFile = xml.CreateElement("file");
                        targetFile.SetAttribute("path", path);
                        targetFile.SetAttribute("purpose", purpose);
                        targetModule?.AppendChild(targetFile);
                        addedCount++;
                    }
                }
            }
        }

        var floatingFiles = root.SelectNodes(XmlTags.File);
        if (floatingFiles != null)
        {
            foreach (XmlNode fileNode in floatingFiles)
            {
                var path = fileNode.Attributes?["path"]?.Value;
                if (string.IsNullOrEmpty(path)) continue;
                var purpose = fileNode.Attributes?["purpose"]?.Value;
                if (string.IsNullOrEmpty(purpose)) purpose = fileNode.InnerText.Trim();

                var targetFile = indexRoot.SelectSingleNode($"//file[@path='{path}']") as XmlElement;
                if (targetFile != null)
                {
                    targetFile.SetAttribute("purpose", purpose);
                    updatedCount++;
                }
                else
                {
                    logger.Warning($"Cannot add '{path}' without a <module>. Please wrap new files in a <module>.");
                }
            }
        }

        indexRoot.SetAttribute("lastUpdated", DateTime.UtcNow.ToString("o"));

        var settings = new XmlWriterSettings
        {
            Indent = true, IndentChars = "  ",
            OmitXmlDeclaration = false, Encoding = new UTF8Encoding(false)
        };
        using (var writer = XmlWriter.Create(indexFile, settings))
        {
            xml.Save(writer);
        }

        logger.Success($"✅ Updated {indexFileName}: {addedCount} added, {updatedCount} updated, {deletedCount} deleted.");
    }
}
