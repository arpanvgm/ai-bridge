# AI Bridge CLI Scenarios

This project runs process-level scenario tests against the real `AIBridge.Cli`.

It is not a unit test project. It creates temporary dummy repositories, runs the
CLI through a real process, and verifies command output plus filesystem effects.

## Run All Scenarios

```bash
dotnet run --project tests/AIBridge.Cli.Scenarios
```

## Run a Subset

```bash
dotnet run --project tests/AIBridge.Cli.Scenarios -- --filter apply
```

## List Scenarios

```bash
dotnet run --project tests/AIBridge.Cli.Scenarios -- --list
```

## Keep Temporary Projects

```bash
dotnet run --project tests/AIBridge.Cli.Scenarios -- --keep-artifacts
```

Temporary projects are created under:

```text
/tmp/ai-bridge-scenarios/
```

## Exit Codes

- `0`: all scenarios passed
- `1`: one or more scenarios failed
- `2`: invalid runner arguments or no scenarios matched

## Failure Output

Example:

```text
PASS init creates workspace and templates
FAIL apply rejects invalid xml
     Expected to contain: not valid XML
     Actual:
     ...

Result: 21 passed, 1 failed
```
