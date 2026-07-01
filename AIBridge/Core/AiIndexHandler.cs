using System;
using System.IO;
using System.Text;
using System.Xml;
using AIBridge.Helpers;
using AIBridge.Constants;

namespace AIBridge.Core
{
    public static class AiIndexHandler
    {
        public static void HandleCreate(XmlNode root, string projectPath)
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
                Encoding = new UTF8Encoding(false) // No BOM
            };

            using (var writer = XmlWriter.Create(indexFile, settings))
            {
                doc.Save(writer);
            }

            ConsoleHelper.Success("✅ Generated new ai-bridge-index.xml successfully.");
        }

        public static void HandleUpdate(XmlNode root, string projectPath)
        {
            var aiWorkspace = WorkspaceHelper.GetAiWorkspacePath(projectPath);
            var indexFileName = WorkspaceHelper.GetIndexFileName(projectPath);
            var indexFile = Path.Combine(aiWorkspace, indexFileName);

            if (!File.Exists(indexFile))
            {
                // Create an empty index if it doesn't exist (e.g., brand new project)
                var emptyDoc = new XmlDocument();
                var emptyRoot = emptyDoc.CreateElement("ai-bridge-index");
                emptyDoc.AppendChild(emptyRoot);
                emptyDoc.Save(indexFile);
            }

            var xml = new XmlDocument();
            try
            {
                xml.Load(indexFile);
            }
            catch (Exception ex)
            {
                ConsoleHelper.Error($"Error parsing existing {indexFileName}: {ex.Message}");
                return;
            }

            var indexRoot = xml.DocumentElement;
            if (indexRoot == null || indexRoot.Name != "ai-bridge-index")
            {
                ConsoleHelper.Error($"Error: {indexFileName} is malformed (missing <ai-bridge-index> root).");
                return;
            }

            int updatedCount = 0;
            int addedCount = 0;
            int deletedCount = 0;

            // Process all <delete> nodes anywhere inside <update-ai-bridge-index>
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

                            // Clean up empty module
                            if (moduleNode != null && moduleNode.SelectNodes(XmlTags.File)?.Count == 0)
                            {
                                indexRoot.RemoveChild(moduleNode);
                            }
                        }
                    }
                }
            }

            // Process <file> additions/updates inside <module> tags
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
                        {
                            purpose = fileNode.InnerText.Trim();
                        }

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

            // Process floating <file> nodes not in a module (only for updates of existing files)
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
                        ConsoleHelper.Warning($"Cannot add '{path}' without a <module>. Please wrap new files in a <module>.");
                    }
                }
            }

            // Save the updated XML with nice formatting
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(false) // No BOM
            };

            indexRoot.SetAttribute("lastUpdated", DateTime.UtcNow.ToString("o"));

            using (var writer = XmlWriter.Create(indexFile, settings))
            {
                xml.Save(writer);
            }

            ConsoleHelper.Success($"✅ Updated {indexFileName}: {addedCount} added, {updatedCount} updated, {deletedCount} deleted.");
        }
    }
}
