---
name: work-in-meta
description: Navigate and change the meta foundation repository while preserving workspace, generation, build, and ownership conventions.
---

# Work in the meta repository

Use this skill for repository-level implementation, review, diagnosis, or
verification in `meta`. For generic workspace operations, MetaMesh, MetaDocs,
or conversion work, also read the more specific skill in this pack.

## Establish context

1. Resolve the repository root with `git rev-parse --show-toplevel`; do not
   assume the current directory.
2. Read `docs/REPOSITORY-ORIENTATION.md` and the nearest applicable instructions.
3. Inspect `git status --short` before changing anything. Preserve unrelated
   edits and confirm the current branch and HEAD before publication.
4. If the work crosses into BI products, locate the companion `meta-bi`
   checkout and read its `AGENTS.md` and `docs/META-MODELING-GUARDRAILS.md`.

## Locate ownership

- `Meta/` owns representation-neutral operations, typed models, workspace
  surfaces, integration services, and the `meta` CLI.
- `MetaCli/` owns modeled command surfaces and the `meta-cli` authoring CLI.
- `MetaDocs/` owns the documentation model, CLI, authored documentation
  workspaces, combined suite, and rendered site.
- `MetaMesh/` owns modeled multi-workspace command execution.
- `MetaWeave/` owns correspondence, WeaveScript, execution, and `meta-weave`.
- BI models, conversions, transforms, execution products, and demos belong in
  the sibling `meta-bi` repository.

Put behavior under the product that owns the outcome. Do not move domain policy
into a shared foundation merely because several consumers need navigation or
presentation support.

CLI applications are modeled through MetaCli workspaces such as
`Meta/Cli/meta.MetaCli`. When changing a command surface, keep the modeled
workspace, generated/runtime command definition, handlers, tests, and imported
MetaDocs reference coherent. Use `meta-cli show` and runtime `help` as separate
views of the same command contract.

## Work with workspaces

- Treat `workspace.meta` as the surface descriptor.
- Treat XML, SQL, and C# as supported representations of one modeled workspace.
- Use the surface API or owning CLI; do not parse or patch instance files as an
  application interface.
- Do not add a second authoritative representation beside an existing
  workspace.
- Use absolute generation outputs and check for repeated directory segments.

## Build and verify

Choose the smallest owning solution or project first. The principal foundation
checks are:

```powershell
dotnet build Metadata.Framework.sln --nologo -m:1 -nr:false
dotnet test Meta\Tests\Meta.Core.Tests.csproj --nologo -m:1 -nr:false
dotnet test Meta\Operations.Tests\Meta.Operations.Tests.csproj --nologo -m:1 -nr:false
```

Use `MetaDocs.sln`, `MetaMesh.sln`, or `MetaWeave.sln` when the change belongs
there. Build and test serially when projects share `bin` or `obj` outputs. End
with `git diff --check` and review every changed path, especially generated
workspace and documentation output.

`Directory.Build.props` and `Directory.Build.targets` carry shared build and
package behavior. `pack-internal.cmd` creates local development packages; run
it only when the consuming task needs refreshed packages, and do not reinterpret
the `-internal` development suffix as the public release contract.

Do not commit, push, deploy, or run destructive demo cleanup merely because the
technical workflow reaches that step; obtain the authorization required for
the current task.
