---
name: use-meta-mesh
description: Inspect, validate, author, and run MetaMesh operations that coordinate reproducible CLI work across modeled workspaces.
---

# Use MetaMesh

Use this skill when a workflow spans multiple workspaces or CLI invocations.
MetaMesh is the modeled construction record; do not replace it with a shell
script that duplicates its ordered steps.

## Inspect without executing

```powershell
meta-mesh show --workspace <mesh-workspace>
meta-mesh workspaces --workspace <mesh-workspace>
meta-mesh operations --workspace <mesh-workspace>
meta-mesh steps --operation <name> --workspace <mesh-workspace>
meta-mesh validate --operation <name> --workspace <mesh-workspace>
```

Use `meta-mesh help <command>` before forming exact invocations. Resolve the
mesh root and workspace locations shown by `show`; do not assume paths are
relative to the caller's current directory.

## Compose model workspaces

MetaMesh composes a system by naming its participating workspaces and recording
the CLI operations that create, transform, validate, deploy, or execute them.
It does not merge their models into a super-model and does not become semantic
truth for any product workspace.

Design the mesh from the workspace boundaries outward:

1. Inventory durable inputs, authored workspaces, generated workspaces,
   implementation/options workspaces, and external SQL workspaces.
2. Give each workspace a stable mesh name, model name, description, and exactly
   one surface location.
3. Set the mesh root so every relative path has one stable coordinate system.
4. Define operations as recognizable outcomes such as `sync-source-schema`,
   `create-raw-vault`, `bind-transforms`, `run-etl`, or `verify-stack`.
5. Add deterministic steps that call the owning CLI and pass declared workspace
   locations using mesh substitutions such as `{workspace:name.location}` and
   `{mesh.root}`.
6. Chain steps with `--previous-step` when one consumes the prior step's output.

An operation's step order is enough for construction sequencing. Do not add a
second planning or dependency model for facts already implied by consuming an
earlier output. Separate operations may represent lifecycle choices that a
caller invokes when appropriate; they do not need one universal run order.

Keep reusable definition workspaces separate from project-specific instances,
options, and generated outputs. A large set of transformations normally belongs
inside one owning TransformScript, PatternInstance, Pipeline, or MetaWeave
workspace rather than becoming one mesh workspace per transformation.

## Understand the operation

Before running, classify every step that can:

- create, overwrite, or regenerate tracked workspaces;
- connect to SQL Server or Analysis Services;
- deploy, process, restore, drop, or clean external objects;
- consume environment variables or credentials;
- delete transient evidence.

Validation proves references and command shape; it does not authorize external
side effects. Obtain the required authority for the selected operation.

## Run

```powershell
meta-mesh run --operation <name> --workspace <mesh-workspace>
```

Use `--verbose` when child-process output is needed for diagnosis. Otherwise
preserve the normal compact progress presentation. Let MetaMesh substitute
declared workspace locations and root paths; do not manually expand an
operation into a parallel command transcript.

## Author or update

Use `meta-mesh create`, `add-workspace`, `add-operation`, and `add-step` through
the CLI. Workspace names are stable references used by step templates. Prefer
`--arguments-stdin` for long argument templates. Keep commands deterministic
and non-interactive. Put environment-specific secrets in named environment
variables, never in mesh instance values.

Use explicit dependency order only when one step consumes another step's
output. Do not encode ordinary common-sense sequencing as a separate planning
theory.

## Verify

Validate first, run the smallest relevant operation, inspect produced
workspaces through their owning CLIs, and review `git status --short` afterward.
Generated changes must be expected outputs of the modeled operation. Run
`git diff --check` before publication.
