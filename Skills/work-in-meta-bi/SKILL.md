---
name: work-in-meta-bi
description: Navigate and change the meta-bi repository using its sanctioned models, owning CLIs, integration demos, and verification conventions.
---

# Work in the meta-bi repository

Use this skill for repository-level work in `meta-bi`. Read the companion
`meta` repository skill as well when changing foundation packages, MetaWeave,
MetaMesh, or the combined documentation site.

For requirements-to-system construction, read
[Build a Meta-BI stack](../build-meta-bi-stack/SKILL.md). This repository skill
locates implementation ownership; the stack skill composes product workspaces
and their handoffs.

## Establish context

1. Resolve the repository root and inspect `git status --short`, the current
   branch, and HEAD.
2. Read `AGENTS.md` and `docs/META-MODELING-GUARDRAILS.md` completely.
3. Identify the sanctioned workspace, service, CLI, tests, and integration
   witness that own the requested behavior before editing.
4. Stop if the source model, generated surfaces, code, and documentation
   disagree; reconcile the authority before extending it.

## Repository shape

Product families normally contain some combination of:

- `Model` or `Workspace`: the authoritative modeled contract;
- `Core`: domain services and algorithms;
- `Cli`: the executable command surface authored through MetaCli;
- `Tests`: focused semantic and CLI witnesses.

`MetaConvert/Weaves/` contains sanctioned model-to-model correspondence
workspaces. `Demos/` contains executable integration witnesses, usually as a
MetaMesh workspace plus its inputs and expected results. Generated workspace
files are evidence, not a shortcut around the owning CLI.

Each executable CLI carries a modeled `<application>.MetaCli` workspace beside
its project. Command-surface changes must keep that workspace, runtime command
definition, handlers, CLI tests, and the combined MetaDocs import coherent.
Do not add a handler-only command or patch generated CLI artifacts to avoid the
modeled command surface.

## Modeling and implementation rules

- Model explicit authored truth, not convenient projections or encoded lists.
- Keep ordering, identity, relationships, and polymorphism structural.
- Preserve representation symmetry across XML, SQL, and natural C# surfaces.
- Do not add heuristic lineage, display-string parsing, blobs, or ad-hoc JSON as
  product truth.
- Do not force parsing, binding, inference, graph algorithms, planning, or
  runtime execution through MetaWeave solely because it is possible.
- CLI presentation belongs in CLI code; services return structured results.

## Use demos as witnesses

Read [Use Meta-BI demos](../use-meta-bi-demos/SKILL.md) when selecting or
running a demo. Start by inspecting its MetaMesh workspace; do not copy commands
out of the mesh and create a second workflow. Database and Analysis Services
operations require the environment and authority stated in the demo README.

## Verify proportionally

Build the owning solution or project, run its focused tests, then run the
integration mesh or broader suite that proves the affected boundary. Use
`--no-restore` or `--no-build` only after the prerequisite step has succeeded.
Do not run concurrent builds against shared outputs. Finish with
`git diff --check` in every changed repository and inspect for generated or
unrelated changes.

`meta-bi` consumes the `meta` foundation as packages. When a cross-repository
change genuinely requires new local packages, build and pack `meta` first and
wait for successful publication before building consumers. Do not change
package names or versions merely to work around stale local outputs.
