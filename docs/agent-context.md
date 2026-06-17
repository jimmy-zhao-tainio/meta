# Agent Context

## Current State - 2026-06-17

Branch: `codex/remove-stale-meta-surface`

This branch contains an architecture refactor pass for `meta` with no intended functional behavior changes.

Completed refactors:

- Removed the stale `random create` CLI implementation.
- Recovered unrelated helpers that had lived in the random command file and moved them into focused CLI runtime partials.
- Split `GenerationService` into:
  - public facade: `Meta/Core/Services/GenerationService.cs`
  - C# rendering: `Meta/Core/Services/GenerationService.CSharp.cs`
  - shared C# support: `Meta/Core/Services/GenerationService.CSharpSupport.cs`
  - SQL/SSDT artifacts: `Meta/Core/Services/SqlGenerationArtifacts.cs`
  - output writing/manifests: `Meta/Core/Services/GenerationOutputWriter.cs`
- Made `WorkspaceService` validation dependency explicit through `IValidationService`.
- Extracted workspace path validation into `WorkspacePathResolver`.
- Extracted staged workspace save/rollback mechanics into `WorkspaceStagingWriter`.
- Split typed workspace path discovery/resolution into `TypedWorkspacePathResolver`.
- Split import implementation into:
  - `ImportService` orchestration
  - `CsvImportSupport`
  - `SqlServerImportReader`
- Split CLI command registration into `CliRuntime.CommandRegistry`.
- Started splitting the oversized CLI strict-mode test file with `CliStrictMode.RemovedCommandsTests`.

Verification already run:

```powershell
dotnet build Metadata.Framework.sln --no-incremental
dotnet test Meta\Tests\Meta.Core.Tests.csproj --no-restore
git diff --check
```

Results:

- Full solution build passed.
- `Meta.Core.Tests` passed: 210/210.
- `git diff --check` passed, with only line-ending warnings.

## Remaining Architectural Debt

Leave `HelpTopics` alone for now per user direction.

Good next candidates:

- Split `GenerationService.CSharp.cs` further into tooling serializer generation and consumer model/entity generation.
- Continue reducing `WorkspaceService` by extracting read/load, shard planning, workspace config serialization, and backup helpers.
- Split `CliRuntime.InstanceMutationHelpers.cs` further into row patch construction and relationship resolution.
- Continue splitting `CliStrictModeTests.cs` by command family.

## Notes

- `Meta.Core` still builds without depending on `Meta.Adapters` or `Meta.Cli`.
- Generated tooling contracts should remain stable unless coordinated with `meta-bi`.
- Avoid keeping stale public APIs without clear functional callers.
