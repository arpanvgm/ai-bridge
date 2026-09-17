# AI Bridge CLI Workflows

This document visualizes the internal workflows of all `ai-bridge` CLI commands. It uses Mermaid flowcharts to illustrate the system state, decisions, and processes triggered by each command.

## 1. `init` Flow
The `init` command scaffolds the AI Bridge workspace for a new project, setting up ignores, templates, and the initial codebase index.

```mermaid
flowchart TD
    Start["User runs 'ai-bridge init'"] --> InitService["WorkspaceInitService"]
    InitService --> CheckState{"Check Workspace State"}
    
    CheckState -- "Not Initialized" --> InitForceFalse["Initialize force=false"]
    CheckState -- "Outdated" --> InitForceTrue["Initialize force=true"]
    CheckState -- "OK" --> CheckTemplates{"Check Missing Templates"}

    InitForceFalse --> CreateArtifacts["Create /artifacts Folder"]
    InitForceTrue --> CreateArtifacts

    CreateArtifacts --> CreateResponse["Create empty ai-response.xml (if not exists)"]
    CreateResponse --> CreateGitignore["Create .gitignore (internal)"]
    CreateGitignore --> AppendDockerignore["Append to .dockerignore if exists"]
    AppendDockerignore --> HandleAiIgnore["Create or Update .aiignore (appends missing default rules)"]

    HandleAiIgnore --> CheckForceDelete{"Force == true?"}
    CheckForceDelete -- "Yes" --> DeleteModes["Delete old Mode folders"]
    CheckForceDelete -- "No" --> ExtractTemplates["Extract AI Templates"]
    DeleteModes --> ExtractTemplates

    CheckTemplates -- "Yes" --> ExtractTemplates

    ExtractTemplates --> GenerateIndex["Generate initial index.xml"]
    GenerateIndex --> UpdateState["Update Local State"]
    UpdateState --> EndInit(("Done"))
    
    CheckTemplates -- "No" --> EndInit
```

## 2. `pack` Flow
The `pack` command reads source files, respects ignore rules, and bundles the codebase into a single text context file suitable for AI consumption.

```mermaid
flowchart TD
    Start["User runs 'ai-bridge pack'"] --> EnsureWorkspace["Ensure Workspace Ready"]
    EnsureWorkspace --> DetectProjects["Detect Projects/Sub-projects"]
    DetectProjects --> GetTrackedFiles["Get all tracked files"]

    GetTrackedFiles --> IterateFiles["Iterate Files"]
    IterateFiles --> FilterFiles{"Filter File"}
    
    FilterFiles -- "Skip" --> SkipFile["Binary / .aiignore / Always Excluded"]
    FilterFiles -- "Include" --> ReadFile["Read File Content"]
    
    ReadFile --> WrapContent["Wrap in 'file' XML tags"]
    WrapContent --> GroupProjects["Group content by Project Name"]

    GroupProjects --> WriteFull["Write project-name-context.txt for each project"]
    WriteFull --> PackDone2(("End: Pack Successful"))
```

## 3. `apply` Flow
The `apply` command is the core engine for executing AI-generated instructions. It parses XML, applies code patches, manages files, and handles metadata indexing.

```mermaid
flowchart TD
    Start["User runs 'ai-bridge apply'"] --> InitCheck["Ensure Workspace Ready"]
    InitCheck --> CheckOptions{"Flags?"}
    
    CheckOptions -- "--paste" --> ReadClipboard["Read from Clipboard"]
    CheckOptions -- "--watch" --> WatchMode["Start FileSystemWatcher on ai-response.xml"]
    CheckOptions -- "None" --> ReadFile["Read ai-response.xml"]

    WatchMode -. "File Change Detected" .-> Debounce["Debounce / Wait for lock"]
    Debounce --> ReadFile

    ReadClipboard --> StripMarkdown["Strip Markdown Fences"]
    ReadFile --> StripMarkdown

    StripMarkdown --> ParseXML{"Parse XML"}
    ParseXML -- "Invalid" --> Abort(("Abort Transaction"))
    ParseXML -- "Valid" --> CheckRoot{"Root Node?"}

    CheckRoot -- "ai-request tag" --> HandleRequest["RequestService: Extract Context"]
    HandleRequest --> SetClipboard["Copy Payload to Clipboard"]
    SetClipboard --> ResetInput

    CheckRoot -- "ai-response tag" --> ProcessEdits["Process Edits/Operations"]

    ProcessEdits --> EditFile["'file' tag: Create/Overwrite Full File"]
    ProcessEdits --> EditPatch["'patch' tag: Apply Search/Replace"]
    ProcessEdits --> EditDelete["'delete' tag: Delete File"]

    EditFile --> SyncPoint
    EditPatch --> SyncPoint
    EditDelete --> SyncPoint

    SyncPoint --> CheckPatchSuccess{"Patch Failed?"}
    CheckPatchSuccess -- "No" --> HandleIndex["Update/Create index.xml"]
    HandleIndex --> PostProcess["Post-Processing"]
    
    CheckPatchSuccess -- "Yes" --> LogErrors["Log Failed Patches"]
    LogErrors --> PostProcess

    PostProcess --> HandleTracker["Handle 'tracker' tag"]
    HandleTracker --> CleanEmptyFolders["Clean Empty Folders"]
    CleanEmptyFolders --> ResetInput

    ResetInput["Reset ai-response.xml"] --> EndApply(("Done"))
```
