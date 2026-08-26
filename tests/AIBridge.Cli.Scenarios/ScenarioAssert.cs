namespace AIBridge.Cli.Scenarios;

public sealed class ScenarioFailureException(string message) : Exception(message);

public static class ScenarioAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new ScenarioFailureException(message);
    }

    public static void False(bool condition, string message)
    {
        if (condition)
            throw new ScenarioFailureException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new ScenarioFailureException(
                $"{message}{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual: {actual}");
        }
    }

    public static void NotEqual<T>(T unexpected, T actual, string message)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            throw new ScenarioFailureException($"{message}{Environment.NewLine}Unexpected: {unexpected}");
    }

    public static void Contains(string expected, string actual, string message)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioFailureException(
                $"{message}{Environment.NewLine}Expected to contain: {expected}{Environment.NewLine}Actual:{Environment.NewLine}{actual}");
        }
    }

    public static void DoesNotContain(string unexpected, string actual, string message)
    {
        if (actual.Contains(unexpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioFailureException(
                $"{message}{Environment.NewLine}Did not expect: {unexpected}{Environment.NewLine}Actual:{Environment.NewLine}{actual}");
        }
    }

    public static void FileExists(string path)
    {
        True(File.Exists(path), $"Expected file to exist: {path}");
    }

    public static void DirectoryExists(string path)
    {
        True(Directory.Exists(path), $"Expected directory to exist: {path}");
    }

    public static void FileDoesNotExist(string path)
    {
        False(File.Exists(path), $"Expected file to not exist: {path}");
    }
}
