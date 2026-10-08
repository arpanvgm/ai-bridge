
namespace AIBridge.Core.Constants;

public static class FileNames
{
    public const string AiIgnore = ".aiignore";
    public const string Index = "index.xml";
    public const string ResponseXml = "ai-response.xml";

    public const string RequestedContext = "ai-requested-context.txt";
    public const string TrackerXml = "tracker.xml";

    // Machine-local (gitignored) stamp holding the hash of the templates extracted into ai-bridge/.
    public const string TemplateStamp = ".template-stamp";
}
