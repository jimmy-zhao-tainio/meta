# Roadmap

This roadmap is for the sanctioned model/tool split around:

- `MetaType`
- `MetaSchema`
- `MetaTypeConversion`
- `MetaWeave`

The goal is not a monolith. The goal is a set of cooperating sanctioned models and CLIs that stay isomorphic across XML, SQL, and generated C# by keeping cross-model references as scalar properties and expressing the meaning of those properties through weave metadata.

## Principles

- Keep semantic models separate. Do not collapse everything into one giant sanctioned model.
- Keep cross-model references as scalar properties such as `TypeId`.
- Put cross-model meaning in `MetaWeave`, not in medium-specific runtime hacks.
- Keep connectors responsible only for discovery, not type conversion policy.
- Keep `MetaTypeConversion` responsible for conversion rules, not source discovery.
- Keep every first slice create-only and deterministic unless update/merge semantics are explicit.

## Target Shape

### `MetaSchema`

Owns source-discovery metadata such as:

- `System`
- `Schema`
- `Table`
- `Field`

Current state:
- `Field` carries `TypeId` as a scalar property.

Long term:
- `Field` should carry enough structured source-type identity to resolve into `MetaType` cleanly.

### `MetaType`

Owns sanctioned data-type metadata such as:

- `TypeSystem`
- `Type`
- `TypeSpec`
- facets and facet values if needed

This is the shared type vocabulary that both `MetaSchema` and `MetaTypeConversion` consume.

### `MetaTypeConversion`

Owns sanctioned conversion/mapping metadata such as:

- source and target type correspondences
- conditions
- transforms
- conversion implementations

This should consume `MetaType`, not duplicate its core concepts.

### `MetaWeave`

Owns sanctioned cross-model binding metadata such as:

- model references
- property bindings
- validation rules for cross-model scalar references

Example:
- `MetaSchema.Field.TypeId`
- resolves to
- `MetaType.Type.Id`

The weave is explicit metadata, not hidden runtime convention.

## Phase 1: Stabilize Current `MetaSchema`

Goal:
- get the current extraction path stable enough to build on
- keep the extraction primitive intentionally small

Deliverables:
- `meta-schema extract sqlserver --new-workspace ... --system ... --schema ... --table ...`
- deterministic `MetaSchema` workspace output
- no stale command/docs drift

Tasks:
1. Narrow the command contract to one explicit extraction unit:
   - one system
   - one schema
   - one table
2. Make `--system` required so modeled system identity is deliberate rather than inferred from connection details.
3. Restrict SQL Server extraction to the requested table only.
4. Commit the current `MetaSchema` extraction pass.
5. Add focused tests around:
   - missing `--connection`
   - missing `--system`
   - missing `--schema`
   - missing `--table`
   - invalid schema name
   - invalid table name
   - deterministic row counts/order on a fixture database if feasible
6. Remove any remaining stale command/docs drift.

Exit condition:
- SQL Server extraction is boring, reliable, and scoped to exactly one table per invocation.

## Phase 2: Define `MetaType`

Goal:
- introduce the sanctioned shared type model

Deliverables:
- new sanctioned `MetaType` model
- seedable empty workspace
- first sanctioned instances for SQL Server source types and core canonical types

Tasks:
1. Define the minimum viable entities.
   - likely:
     - `TypeSystem`
     - `Type`
     - `TypeSpec`
2. Decide whether `TypeSpec` belongs in Phase 2 or Phase 3.
   - keep Phase 2 minimal if needed.
3. Add sanctioned model XML on disk.
4. Add a dedicated `meta-type init --new-workspace <path>` path for `MetaType`.
5. Keep ids stable and non-database-specific.

Key rule:
- `bit` must not become `sqlserver:EnterpriseBIPlatform:fieldtype:bit`.
- shared source-type instances should be shaped at the type-system level, not database level.

Exit condition:
- one sanctioned type vocabulary exists independently of schema extraction.

## Phase 3: Make `MetaSchema` Target `MetaType`

Goal:
- stop minting ad hoc extracted `FieldType` identities

Deliverables:
- `MetaSchema` updated so field-level type identity is compatible with `MetaType`

Tasks:
1. Decide the immediate bridge shape.
   - simplest first slice:
     - `MetaSchema.Field.TypeId` scalar property
2. Update extraction so SQL Server types land as shared type ids, not workspace-local field-type ids.
3. Remove or retire `FieldType` from `MetaSchema` if the new shape makes it redundant.
4. Update extractor output and stub catalogs.

Decision point:
- either:
  - `FieldType` is transitional and later removed
- or:
  - `FieldType` is replaced now by direct `TypeId`

Recommended bias:
- prefer direct `TypeId` and fewer redundant concepts.

Exit condition:
- extracted fields identify types in the shared `MetaType` universe.

## Phase 4: Define `MetaWeave`

Goal:
- make cross-model property meaning explicit

Deliverables:
- sanctioned generic `MetaWeave` model
- no source-specific hardcoding in core runtime

Minimum viable entities:
1. `ModelReference`
   - identifies a referenced model/workspace
2. `PropertyBinding`
   - source model/entity/property
   - target model/entity/property

Example binding:
- source model: `MetaSchema`
- source entity: `Field`
- source property: `TypeId`
- target model: `MetaType`
- target entity: `Type`
- target property: `Id`

Tasks:
1. Define the sanctioned generic `MetaWeave` model XML.
2. Create the first weave workspace:
   - `Weave-MetaSchema-MetaType`
3. Keep references scalar and explicit.

Exit condition:
- cross-model meaning is metadata, not convention.

## Phase 5: Build `meta-weave`

Goal:
- provide a CLI that evaluates weave metadata

Initial command surface:
1. `meta-weave check --workspace <WeaveWorkspace>`
2. optional later:
   - `meta-weave explain --workspace <WeaveWorkspace>`
   - `meta-weave materialize --workspace <WeaveWorkspace> --new-workspace <path> --model <name>`

First slice should only do:
- load weave workspace
- load referenced workspaces
- verify 100% RI for every property binding
- report unresolved values clearly

Tasks:
1. Create `MetaWeave.Cli` with create-only/check-first behavior.
2. Implement binding evaluation generically.
3. Produce deterministic diagnostics.

Non-goal for first slice:
- do not start with a merged workspace emitter.

Exit condition:
- weave semantics are checkable and trustworthy.

## Phase 6: Rebuild `MetaTypeConversion` on `MetaType`

Goal:
- make conversion rules consume the shared type model instead of parallel duplicated concepts

Deliverables:
- `MetaTypeConversion` sanctioned model aligned to `MetaType`
- `meta-type-conversion check`
- `meta-type-conversion resolve`

Tasks:
1. Review current `TypeConversionCatalog` concepts and rename/reframe as needed.
2. Remove duplicate type identity concepts if `MetaType` now owns them.
3. Bind conversion rules to shared `Type` / `TypeSpec` concepts.
4. Keep conversion logic separate from schema extraction.
5. Keep runtime conversion code out of `MetaTypeConversion`; downstream tools can consume `ConversionImplementationId` and implement execution themselves.

Exit condition:
- type conversion consumes shared type metadata instead of a parallel copy.

## Phase 7: Optional Materialization

Goal:
- support downstream tooling that wants a merged view

Possible command:
- `meta-weave materialize --workspace <WeaveWorkspace> --new-workspace <path> --model <name>`

Important rule:
- merged output is a generated view/artifact, not necessarily canonical source metadata

This phase should only start after:
- `MetaType` is stable
- `MetaSchema` points at it
- `MetaWeave check` is solid

## Recommended Execution Order

1. Narrow and stabilize `MetaSchema extract sqlserver` to one system, one schema, one table
2. Introduce `MetaType`
3. Repoint `MetaSchema` field type identity into `MetaType`
4. Introduce sanctioned `MetaWeave`
5. Implement `meta-weave check`
6. Rework `MetaTypeConversion` to consume `MetaType`
7. Only then consider merged/materialized views

## Risks

1. Monolith drift
- trying to solve `MetaSchema`, `MetaType`, `MetaTypeConversion`, and `MetaWeave` in one schema will collapse concerns and slow iteration

2. Hidden runtime conventions
- if `TypeId` resolution is hardcoded instead of woven, the model split becomes fake

3. Type duplication
- if `MetaSchema` keeps its own `FieldType` world while `MetaTypeConversion` keeps another, the architecture will remain muddy

4. Overbuilding weave
- `meta-weave materialize` is tempting, but `meta-weave check` is the real first necessity

## Current Status

- `MetaSchema` exists as a focused extraction boundary.
- `MetaType` exists as a sanctioned shared type vocabulary.
- `MetaSchema.Field.TypeId` points into `MetaType` at the `Type` level.
- `MetaWeave` exists with sanctioned weave instances and façade/check commands.
- `MetaTypeConversion` exists with sanctioned mappings plus `check` and `resolve`.

## Immediate Next Step

Do this next:

1. Tighten `MetaTypeConversion` semantics where needed, but keep it at `Type` level for now.
2. Keep runtime conversion implementations out of `MetaTypeConversion`; let downstream tools interpret `ConversionImplementationId`.

## Deferred Scope Note

- Data quality is a planned sanctioned modeling area.
- It should be introduced as its own explicit model boundary, not mixed into `MetaSchema` or `MetaRawDataVault` first slices.
- It must cover more than rule checks and RI: at minimum accuracy, completeness, consistency, timeliness, uniqueness, reliability, usefulness, and controlled differences.
- It should model both:
  - quality definitions/expectations (what "good" means for a dataset/use-case),
  - quality outcomes/incidents (what actually happened, severity, ownership, resolution state).
- Issue tracking, confidence/trust signals, and decision-impact metadata should be first-class parts of that model boundary.

