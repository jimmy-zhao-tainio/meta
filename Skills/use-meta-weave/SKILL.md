---
name: use-meta-weave
description: Inspect, author, update, execute, and verify MetaWeave correspondence and its modeled WeaveScript transformations.
---

# Use MetaWeave

Use this skill when working with correspondence as a first-class model. Use
`use-meta-conversions` when merely invoking a sanctioned conversion through
`meta-convert`.

## Understand the workspace

A MetaWeave workspace contains one correspondence with independent directions.
Each direction declares:

- one or more named source workspace contracts;
- one target workspace contract;
- optional string execution parameters;
- reusable named relations;
- source-domain requirements;
- target-entity transformations.

The authored truth is the correspondence workspace. Emitted WeaveScript is a
readable projection, not a parallel collection of `.sql` files. WeaveScript is
a modeled transformation language distilled from MetaTransformScript and kept
recognizably T-SQL-shaped.

## Inspect before editing

```powershell
meta-weave show --workspace <weave-workspace>
meta-weave emit-relation --direction <direction> --name <name> --workspace <weave-workspace>
meta-weave emit-requirement --direction <direction> --name <name> --workspace <weave-workspace>
meta-weave emit-transformation --direction <direction> --name <name> --workspace <weave-workspace>
```

Use runtime help for exact parameters. Inspect referenced source and target
model workspaces with `meta` so the transformation is reviewed against real
entity, property, relationship, identity, and ordering contracts.

## Author or update

Use `meta-weave create`, then add directions, parameters, relations,
requirements, and transformations through their owning commands.

- Relations name reusable semantic projections and joins.
- Requirements reject invalid source-domain evidence with diagnostic columns
  sufficient to locate the violation.
- Each transformation constructs instances of one target entity from source
  and relation evidence.
- Use standard input for WeaveScript bodies; do not create authored `.sql`
  sidecars.
- Update the modeled relation, requirement, or transformation through its
  update command rather than patching generated instance XML.

Let the target model and its required-relationship DAG determine safe entity
materialization order. For a self-reference, use the execution semantics
supported by the target operation rather than inventing a global ordering
language.

Stop when the transformation needs semantics outside the supported WeaveScript
language. Discuss the actual model-transformation requirement before extending
the language or hiding logic in host code.

## Execute

```powershell
meta-weave execute `
  --workspace <weave-workspace> `
  --source-workspace <name=path-or-single-path> `
  --target-workspace <target-model-workspace> `
  --xml <new-output-workspace>
```

`forward` is the default direction. Repeat named `--source-workspace` for a
multi-source direction. `--target-workspace` supplies the target model
contract; `--xml`, `--csharp`, or `--sql` selects the new result surface.

## Maintain sanctioned weaves

Sanctioned workspaces live under `meta-bi/MetaConvert/Weaves/`. A sanctioned
weave is production semantic truth, not an illustrative query. Preserve
requirement precedence, deterministic identities and order, evidence relations,
and established target equivalence. Keep any retired C# converter only at a
clearly bounded compatibility-test boundary.

MetaWeave is not the required implementation language for parsing, binding,
corpus inference, graph algorithms, planning, or runtime systems whose natural
owner is C#.

## Verify

Run strict weave validation, focused correspondence tests, and the closest
end-to-end conversion witness. Exercise emitted projections when readability is
part of the contract and compare produced target workspaces semantically. For a
migrated converter, preserve ordinary and adversarial compatibility witnesses
before removing production routing from the old path. Finish with
`git diff --check` in both repositories.
