# AGENTS.md

## Repository orientation

Read `docs/REPOSITORY-ORIENTATION.md` before substantial work. `meta` is the
representation-neutral metadata foundation; XML, SQL, and C# are workspace
surfaces rather than competing sources of truth.

## Operational skills

Reusable repository and CLI guidance lives under `Skills/`. Read the
matching `SKILL.md` completely before acting:

- `work-in-meta` for repository structure, ownership, builds, and tests;
- `work-in-meta-bi` when work spans the companion BI repository;
- `build-meta-bi-stack` for requirements-to-system composition;
- `use-meta-workspaces` for generic workspace inspection and surface work;
- `author-meta-models` for choosing and using an owning model CLI;
- `use-meta-transform-flow` for schema, TransformScript, Binding, Data Quality,
  Pipeline, and Orchestration workflows;
- `use-meta-conversions` for sanctioned MetaConvert paths;
- `use-meta-weave` for correspondence and modeled WeaveScript;
- `use-meta-mesh` for composing modeled multi-workspace operations;
- `use-meta-bi-demos` for selecting and operating executable BI witnesses;
- `author-meta-docs` for the combined Meta/meta-bi documentation workflow.

Use current runtime help for exact command syntax. The skills explain ownership
and workflow; they do not replace the modeled MetaCli command surface or
MetaDocs reference.

## Repository rules

- Do not hand-edit generated MetaDocs workspaces, `docs.html`, or generated
  workspace artifacts.
- Use the owning CLI and the selected workspace surface.
- Resolve output paths before generation and stop if a path repeats logical
  directory segments.
- Build and test serially when projects share output directories.
- Preserve unrelated worktree changes.
