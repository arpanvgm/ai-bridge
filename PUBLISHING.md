# Publishing `ai-bridge` to NuGet

This document outlines the exact steps required to build and publish new versions of the `ai-bridge` (.NET Global Tool) to the public NuGet registry.

---

## 1. Update the Version Number

Before publishing a new version, you must update the version number in the project file.
Open `AIBridge/AIBridge.csproj` and increment the `<Version>` tag.

```xml
<!-- Example: Bumping version from 1.0.0 to 1.0.1 -->
<Version>1.0.1</Version>
```

## 2. Pack the Tool

Open your terminal, navigate to the `AIBridge` directory, and run the pack command:

```bash
dotnet pack .\AIBridge
```

This will compile the tool and generate a new `.nupkg` file in the `bin/Release` directory.
*(Example output: `bin\Release\Tools.AIBridge.1.0.1.nupkg`)*

## 3. Generate a NuGet API Key (If you don't have one)

If you already have a valid API Key saved, you can skip this step.
1. Go to [nuget.org](https://www.nuget.org/) and sign in.
2. Click on your username in the top right and select **API Keys**.
3. Click **+ Create**.
4. Give it a name (e.g., "AIBridge Publisher").
5. Under "Select Scopes", ensure **Push** is selected.
6. Under "Glob Pattern", enter `Tools.AIBridge`.
7. Click **Create** and **Copy** the generated API Key. *(Keep this secret!)*

## 4. Push the Package to NuGet

From the `AIBridge` directory, run the `dotnet nuget push` command using the `.nupkg` file you just generated.

Replace `YOUR_API_KEY` with your actual NuGet API Key, and ensure the version number in the filename matches the version you packed.

```bash
dotnet nuget push .\AIBridge\bin\Release\Tools.AIBridge.1.0.1.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

---

## What happens next?

- **Indexing:** It usually takes NuGet 5 to 10 minutes to index the new package. During this time, the package page might say "Validating".
- **Availability:** Once indexing is complete, users around the world will immediately receive the update by running:
  ```bash
  dotnet tool update --global Tools.AIBridge
  ```
