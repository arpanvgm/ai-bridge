namespace AIBridge.Core.Abstractions;

public interface IAIBridgeLogger
{
    void Success(string message);
    void Warning(string message);
    void Error(string message);
    void Info(string message);
    void Output(string message);
}
