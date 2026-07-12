namespace AIBridge.Core.Constants;

public static class Timings
{
    // Milliseconds to wait before accepting a new file change event
    public const int WatchDebounceMs = 1000;

    // Milliseconds to pause to allow file locks to release before reading
    public const int FileLockWaitMs = 500;
}
