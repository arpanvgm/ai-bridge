using System.Globalization;
using System.Text;
using AIBridge.Core.Abstractions;

namespace AIBridge.Mcp.Providers;

public class StringLogger : IAIBridgeLogger
{
    private readonly StringBuilder _sb = new();

    public void Success(string message) => Log("SUCCESS", message);
    public void Warning(string message) => Log("WARNING", message);
    public void Error(string message) => Log("ERROR", message);
    public void Info(string message) => Log("INFO", message);
    public void Output(string message) => Log("OUTPUT", message);

    private void Log(string level, string message)
    {
        _sb.AppendLine(CultureInfo.InvariantCulture, $"[{level}] {message}");
    }

    public string GetLogs() => _sb.ToString();
    public void Clear() => _sb.Clear();
}
