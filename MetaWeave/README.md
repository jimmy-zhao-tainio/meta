# MetaWeave

MetaWeave is model-to-model conversion for Meta workspaces. A MetaWeave
workspace is a first-class correspondence whose directions map one or more
source workspace contracts to one target workspace contract. It contains the
independent directions that are supported, the requirements for entering each
direction, named relations that express reusable source-derived rowsets, and
the transformations that populate target entities.

The correspondence is the authored conversion truth. It is stored as ordinary
Meta workspace data and can use XML, SQL, or C# like any other workspace. A
complete sanctioned conversion therefore remains one inspectable, versionable
artifact rather than converter logic hidden in an application assembly.

## WeaveScript

WeaveScript is a modeled language for transforming models, distilled from
MetaTransformScript and deliberately bounded to the needs of workspace
conversion. Its recognizable T-SQL-shaped text is an authoring and inspection
surface, not a collection of stored scripts: the MetaWeave workspace contains
the typed semantic query graph. MetaWeave executes that graph directly with
its in-memory relational engine and does not delegate conversion queries to
SQL Server.

A direction requirement is a violation query: zero result rows satisfy the
requirement, while every returned row is a concrete diagnostic with projected
evidence. Requirements run against the immutable source before target
construction begins.

A direction relation gives a modeled query a name within that direction.
Requirements, transformations, and other relations can read its rowset as an
ordinary table. Relations are pure source-derived values: they share the
direction's source workspaces and parameters, cannot read the target, and are
evaluated once per execution. This keeps substantial conversions composed from
readable semantic parts without introducing stored procedures or function
declarations.

Each transformation projects the records for one target entity. MetaWeave
derives execution order from the target model's dependency graph, starts with
an empty target instance, and submits each insertion through normal Meta Core
operations. Every successful operation becomes the next valid target state.
The state after the last scheduled transformation is the completed workspace.

## Execute a weave

```text
meta-weave execute \
  --workspace <weave-workspace> \
  --source-workspace warehouse=<populated-warehouse> \
  --source-workspace implementation=<populated-implementation> \
  --parameter databaseName=<name> \
  --target-workspace <target-contract> \
  --xml <new-target-workspace>
```

`forward` is the default direction. `--target-workspace` supplies the target
model contract; its instances are not copied. Each repeated
`--source-workspace name=path` supplies one source role declared by the
direction. A one-source direction also accepts a bare path. Declared string
parameters use repeated `--parameter name=value`. Exactly one of `--xml`,
`--csharp`, or `--sql` selects the representation of the new workspace. No
output is created when requirements or transformations fail.

Use `meta-weave show` to inspect the correspondence and
`emit-requirement`, `emit-relation`, or `emit-transformation` to view modeled
queries as readable WeaveScript. `add-relation` and the corresponding
`update-*` commands accept replacement queries through standard input and
store them as semantic workspace data.

The precise language boundary is documented in
[WeaveScript Surface](WEAVESCRIPT-SURFACE.md). Runtime and construction
semantics are documented in
[WeaveScript Execution](WEAVESCRIPT-EXECUTION.md).
