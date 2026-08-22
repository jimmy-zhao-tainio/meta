---
name: use-meta-conversions
description: Run and verify sanctioned MetaConvert workspace-to-workspace transformations while keeping model authorship and artifact realization separate.
---

# Use MetaConvert

Use this skill when an existing sanctioned conversion connects known product
models. Use [Use MetaWeave](../use-meta-weave/SKILL.md) when authoring,
inspecting, or changing the correspondence itself.

## Select the direct path

Run `meta-convert help` to see the supported source-to-target paths and
`meta-convert help <command>` for its exact inputs. Current paths cover schema,
Data Vault, warehouse, Data Quality, transforms, reusable transform patterns,
and analytical realizations.

Use the direct sanctioned path that matches the source and target contracts.
Do not introduce an intermediate model merely because another conversion also
reaches the target. Authored business meaning between layers is not an absent
converter.

## Supply semantic inputs

Conversion commands may require more than the populated source workspace:

- an implementation-policy workspace for physical realization;
- options controlling modeled inclusion or naming behavior;
- multiple source workspaces when the correspondence genuinely has multiple
  inputs;
- a target database or model identity that belongs in the target contract.

Read command-specific help rather than assuming every converter uses the same
`--workspace` or `--source-workspace` spelling. Preserve option workspaces as
modeled inputs; do not encode repeatable semantic options into a delimiter
string for convenience.

## Create one output workspace

Select exactly one output surface through the command's `--output-xml`,
`--output-csharp`, or `--output-sql` option. The result is a new authoritative
target workspace, not a sidecar serialization of the source.

Inspect it through the target product CLI and generic `meta` commands. When the
next step is deployment, emission, or runtime operation, hand the target
workspace to the owning product CLI; artifact realization remains outside the
model-to-model conversion.

## Inspect the sanctioned implementation when needed

Sanctioned correspondence workspaces live under `meta-bi/MetaConvert/Weaves/`.
Normal consumers need not open them. Inspect the matching MetaWeave workspace
when diagnosing conversion semantics, migrating converter truth, or changing
the sanctioned path.

## Verify

Compare source evidence, target semantic structure, and any required legacy
compatibility witness at the correct abstraction. Preserve exact output where
the public contract requires it; otherwise distinguish semantic changes from
surface formatting. Run focused converter tests, strict weave validation when
the correspondence changed, the closest integration demo, and
`git diff --check`.
