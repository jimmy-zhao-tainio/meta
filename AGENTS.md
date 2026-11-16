# AGENTS.md

## Repository orientation

Read `docs/REPOSITORY-ORIENTATION.md` before substantial work. `meta` is the
representation-neutral metadata foundation; XML, SQL, and C# are workspace
surfaces rather than competing sources of truth.

## Repository boundary

This public repository owns the open-source Meta machinery. Agent workflows,
solution-composition guidance, and BI operating expertise are not maintained
in this repository.

## Repository rules

- Do not hand-edit generated workspace or documentation artifacts.
- Resolve output paths before generation and stop if a path repeats logical
  directory segments.
- Build and test serially when projects share output directories.
- Preserve unrelated worktree changes.

## Metadata interpretation

- Read model and instance structure through the selected workspace surface.
  The C# surface uses Roslyn; the semantic state is the generic metadata model
  and instance graph. Do not rediscover that structure through CLR reflection.
- Typed projection, where needed, must use explicit compiler-checked access
  derived from the modeled contract. Do not add reflective discovery or fallback
  for consumers. Keep derived access code outside authored workspace source sets.
- A shipped instance is starting data, not fixed policy. Honor selected workspace
  data and give independently created mutable instances independent state.
