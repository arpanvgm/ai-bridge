
using AIBridge.Core.Helpers;

namespace AIBridge.Core.Services;

public enum WorkspaceStatus
{
    NotInitialized,
    VersionMismatch,
    Valid
}

public class WorkspaceValidator(StateService stateService)
{
    public WorkspaceStatus Check(string projectRoot)
    {
        var state = stateService.CheckState();

        return state switch
        {
            WorkspaceState.NotInitialized => WorkspaceStatus.NotInitialized,
            WorkspaceState.Outdated       => WorkspaceStatus.VersionMismatch,
            _                             => WorkspaceStatus.Valid
        };
    }
}
