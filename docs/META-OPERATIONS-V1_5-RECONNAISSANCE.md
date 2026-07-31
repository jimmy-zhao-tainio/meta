# Meta Operations v1.5 Reconnaissance

## Status

This began as architecture reconnaissance across `meta` and `meta-bi` on
2026-07-28. The operation contract and its bounded vertical proof were
implemented in `meta` on 2026-07-29.

The intended scope is a layer below MetaCli that gives workspace mutations one
semantic contract across XML, SQL, an owned C# source workspace, and generated
typed C# where those surfaces can represent the operation naturally.

The proof changes no product model, MetaCli runtime, or MetaHost behavior. The
generic `meta` CLI commands that fit the operation vocabulary now use the XML
operation session. Larger rename and property/relationship conversion
refactors remain on their dedicated services until the operation vocabulary
can represent them across the intended surfaces.

The SQL interpreter now covers the complete current operation vocabulary.
Opening a SQL operation session first verifies that the database uses Meta's
SQL workspace encoding: case-insensitive `nvarchar(128)` identities,
`nvarchar(max)` property text, matching relationship identity columns, and a
single-column primary key on each entity's `Id`. Identity and relationship
columns carry enabled, trusted checks that restrict SQL identities to
non-empty printable ASCII without leading or trailing spaces. Within that
explicit repertoire, the SQL collation preserves Meta's
`OrdinalIgnoreCase` identity semantics; SQL does not silently substitute
broader linguistic Unicode equivalence. Foreign keys must also be enabled,
trusted, enforced for replication, and use `NO ACTION`. Identity checks must
likewise be enforced for replication. Defaults, computed columns, triggers,
secondary indexes, temporal table behavior, row-level security, additional
checks, and cross-schema foreign keys are rejected before mutation. Generic
SQL import remains a separate permissive path for interpreting ordinary
database tables as text.

The C# source provider now covers the same complete operation vocabulary over a
bounded natural C# form. Roslyn compiles and semantically reads entity classes,
automatic properties, nullable object relationships, object initializers,
entity collections, and the `BuiltIn` object graph. The session applies the
normative operation semantics, emits a canonical C# workspace, asks Roslyn to
read it back, compares the complete abstract state, and only then publishes it.
It never executes workspace source.

## Implemented vertical proof

The first slice now includes:

- concrete operation classes with separate model, model-and-instance refactor,
  and instance-operation families
- an ordered, atomic `MetaOperationPlan`
- one normative `MetaOperationInterpreter` over `GenericModel` and
  `GenericInstance`
- copy-and-publish in-memory execution
- an XML session with exact-path loading, explicit commit/discard, and
  fingerprint-based stale-write rejection
- a SQL Server session that discovers the Meta model without loading instance
  rows and executes operations directly inside one serializable transaction
- a savepoint around each SQL operation plan, preserving accepted earlier
  plans when a later plan is rejected
- a Roslyn-backed C# reader and owned-source session with explicit
  create/open/apply/commit/discard, stale-write rejection, semantic round-trip
  verification, and direct object-reference relationships

The implementation follows the same boundaries:

- operation types are grouped into model, model-and-instance refactor, and
  instance files
- the reference interpreter is split into dispatch, model operations, instance
  operations, and state helpers
- the SQL Server session is split into lifecycle, model operations, instance
  operations, SQL mechanics, metadata discovery, and storage validation

The operation vocabulary is:

- `AddEntityOperation`
- `RemoveEntityOperation`
- `AddPropertyOperation`
- `RemovePropertyOperation`
- `RenamePropertyOperation`
- `SetPropertyRequiredOperation`
- `AddRelationshipOperation`
- `RemoveRelationshipOperation`
- `InsertRecordOperation`
- `SetPropertyOperation`
- `ClearPropertyOperation`
- `SetRelationshipOperation`
- `ClearRelationshipOperation`
- `DeleteRecordOperation`

The conformance proof applies the same ordered instance-mutation and
model-refactor plans through the reference interpreter, XML, live SQL Server,
and a compiled C# source workspace, then compares the complete abstract model
and instance graph. It also proves:

- rejected in-memory and XML plans publish no partial state
- rejected SQL DML and DDL plans roll back to their savepoint
- discarding a SQL session rolls back both DDL and DML
- every accepted operation leaves a conforming state; plans cannot rely on a
  later operation to repair an invalid intermediate state
- XML stale commits fail before overwriting a newer workspace
- generated SQL workspaces encode case-insensitive identity and relationship
  columns and their supported identity repertoire explicitly
- SQL operation sessions reject ordinary database storage before mutation,
  while generic SQL import continues to accept it
- SQL operation sessions reject disabled, untrusted, or replication-bypassed
  identity checks and foreign keys, altered identity checks, row-level
  security, hidden table behavior, and referential actions without scanning
  all instance rows
- direct SQL model operations are checked against the same per-operation model
  invariants as the normative interpreter and roll back when an operation
  produces an invalid model
- relationship operations store the target record's canonical Id spelling
- C# decoding follows the actual `BuiltIn` factory, constructor, returned
  collections, automatic-property defaults, and object-reference assignments
- C# decoding rejects disconnected factories, hidden mutations, unsupported
  control flow, additional executable source, unknown instance members, and
  unrepresentable source names before publication
- C# relationship lookup indexes must be populated before a modeled
  relationship assignment uses them
- C# publication holds one sibling write lock from stale-state comparison
  through directory replacement, fingerprints the exact source bytes Roslyn
  decoded, and checks the live directory again at the publication boundary
- the checked-in Enterprise BI workspace completes both
  `XML -> C# -> SQL -> XML` and `XML -> SQL -> C# -> XML` through a live
  SQL Server database, with semantic equality checked at every boundary and
  byte-identical canonical XML, generated C#, and generated SQL after each
  cycle

### Generic CLI migration

The generic `meta` CLI now executes these public mutations as concrete
operation plans:

- add/drop entity
- add/drop/rename property and change property requiredness
- add/drop relationship
- insert/delete record
- update record properties and relationships
- set a relationship
- bulk insert

The migration also:

- removes the text-discriminated `WorkspaceOp` property bag and its applier
- removes unused snapshot-based undo/redo history
- removes implicit post-command normalization
- removes the separate row-patch parser and relationship resolver
- carries structured validation diagnostics through rejected operation plans
- makes `bulk-insert` insert-only and removes the misleading `--key` upsert path
- parses quoted CSV and TSV input without splitting quoted delimiters or line
  breaks, and rejects both empty and header-only bulk input

The MetaCli workspace was changed through the `meta` CLI. Its save also brought
older empty-element formatting onto the current canonical self-closing form;
that is a one-time physical normalization, not a semantic command-surface
change.

An adoption audit on 2026-07-30 confirmed that the current vocabulary is fully
adopted by the public generic mutation surface:

- model add/drop entity, property, and relationship commands use operation
  plans
- model property rename and requiredness commands use operation plans
- instance insert, bulk insert, update, relationship set, and delete commands
  use operation plans
- no public command handler mutates `Workspace.Model` or
  `Workspace.Instance` directly
- CSV import composition and preflight were moved out of the command handler
  and into `IImportService`; the CLI now loads, delegates, validates, saves,
  and presents the structured result

Rename-model, rename-entity, rename-relationship, property/relationship
conversion, rename-instance-id, diff/merge, workspace merge, import, workspace
creation, and lifecycle commands do not have an equivalent operation in the
current vocabulary. They deliberately remain structured service flows. The
audit did not invent operations merely to make that inventory uniform.

Verification on 2026-07-29:

- `dotnet build Metadata.Framework.sln --nologo -m:1 -nr:false`
  completed with zero warnings and zero errors
- `dotnet test Metadata.Framework.sln --no-build --nologo -m:1 -nr:false`
  passed all 345 tests, including live SQL Server operation and rollback tests
- `meta check --workspace Meta\Cli\meta.MetaCli` returned `Ok`

Verification after the Roslyn C# source provider was added on 2026-07-29:

- the focused C# provider suite passed all 19 tests
- the shared interpreter plus XML, SQL Server, and C# operation suites passed
  all 36 tests
- `dotnet build Metadata.Framework.sln --nologo -m:1 -nr:false`
  completed with zero warnings and zero errors
- `dotnet test Metadata.Framework.sln --no-build --nologo -m:1 -nr:false`
  passed all 365 tests after the composed representation proofs were added
- both mixed XML/C#/SQL cycles passed against local SQL Server; together they
  exercise all six directed transitions between the three representations
- `meta check --workspace Meta\Cli\meta.MetaCli` returned `Ok`

Pre-commit hardening verification on 2026-07-30:

- the focused C# source session suite passed all 25 tests
- the full SQL Server operation session suite passed all 21 tests
- the SQL storage contract rejected identities outside its explicit
  repertoire, altered or missing identity constraints, disabled foreign keys,
  replication-bypassed checks and foreign keys, row-level security, default
  constraints, computed columns, triggers, secondary indexes, additional
  checks, cascade actions, and a required-reference cycle without scanning
  instance tables
- the printable one- and two-character ASCII repertoire produced no
  unexpected equality groups under the selected SQL identity collation
- `dotnet test Meta\Tests\Meta.Core.Tests.csproj --nologo -m:1 -nr:false`
  passed all 292 tests
- `dotnet build Metadata.Framework.sln --nologo -m:1 -nr:false`
  completed with zero warnings and zero errors
- `dotnet test Metadata.Framework.sln --no-build --nologo -m:1 -nr:false`
  passed all 396 tests: Meta.Core 292, MetaDocs 43, MetaMesh 12, MetaCli 28,
  and MetaWeave 21
- live SQL test databases and C# publication test directories were removed

### Limits that remain explicit

- No generated typed operation facade exists yet.
- MetaCliRuntime does not own operation sessions. The generic `meta` handlers
  own the XML session for the migrated command family.
- Rename-model, rename-entity, rename-relationship,
  property-to-relationship, relationship-to-property, rename-instance-id,
  instance diff/merge, workspace merge, and import flows still use their
  existing structured services.
- SQL session opening validates the represented model shape but does not scan
  all existing instance rows for conformance. Required, trusted SQL
  constraints carry identity, nullability, uniqueness, and reference
  integrity; the operation layer rejects storage where those guarantees are
  absent. Checks that cannot be represented as bounded SQL constraints remain
  outside session opening rather than causing hidden full materialization.
- SQL schema refactors reject known table behavior outside the encoded
  workspace contract when the session opens. Other external database
  dependencies remain in place and can cause SQL Server to reject a change;
  the operation layer does not silently drop them.
- The existing XML writer rejects stale writes and restores caught write
  failures, but its multi-file publication is not yet crash-atomic.
- The C# session owns and canonicalizes a directory containing only marked
  Meta C# workspace files. It does not preserve arbitrary project files,
  comments, methods, custom accessors, or source spelling.
- C# directory publication restores the previous directory after ordinary
  swap failures and treats post-publication backup cleanup as cleanup, not a
  failed commit. Like XML, it does not yet claim recovery from abrupt process
  or machine loss between filesystem renames.

## Executive conclusion

The proposed layer is viable, and the repositories already contain most of its
parts in incomplete forms:

- a representation-neutral model and instance state
- generic mutation and refactor services
- generated typed POCO models with object-reference integrity
- a comparatively defensive generic XML writer
- a fast typed XML serializer
- a bounded generic SQL representation
- MetaCli parsing and handler dispatch
- MetaMesh ordering and process execution

The missing part is shared ownership of an operation from application through
commit. Today commands, handlers, services, serializers, and providers each own
different pieces of loading, mutation, validation, rollback, and saving.

The current `WorkspaceOp` should not become the public v1.5 contract. It is a
string-discriminated property bag with unrelated optional fields. The useful
behavior behind it should be retained as concrete operations with explicit
inputs and results.

The implemented vertical proof runs the same operation plans against generic
XML, live SQL, and C# source, then proves that all three decode to the same
abstract state.

## Abstract state

Let a Meta workspace state be:

```text
S = (M, I)
```

where:

- `M` is the structural model
- `I` is an instance graph that conforms to `M`

Workspace paths, XML shard placement, connection references, and generated C#
ownership are representation and packaging concerns. They belong to the
location/session side of the architecture unless a product model explicitly
makes one of them part of its meaning.

There are two validity boundaries:

- structural conformance to the Meta kernel
- additional validity rules owned by a product

The generic reference interpreter owns the first. A typed product service owns
the second and emits an operation plan only after its domain rules accept the
requested action. The shared layer should not turn product validation into
generic string-based callbacks.

An operation is a partial deterministic function. It may reject a state when
its preconditions are not satisfied.

The three mutation families discussed for v1.5 are:

```text
Model operation
  f : M -> M'

Model and instance refactor
  r : (M, I) -> (M', I')

Instance operation
  g_M : I -> I'
```

An implementation will normally return a structured result as well:

```text
apply : (operation, state) -> success(new state, result) | failure(reason)
```

For the successful case, let `next(o, S)` name the `new state` returned by
`apply(o, S)`.

The distinction matters.

- A model operation changes only the structural signature.
- A refactor changes the signature and rewrites affected instance data as one
  semantic action.
- An instance operation leaves the signature fixed.

Here, "model operation" means editing the Meta structural model itself. A
product command such as adding a `PipelineTask`, `Table`, or `Measure` edits a
product document and is therefore an instance operation against that product's
fixed model.

A model operation applied to a workspace with existing instances is legal only
when the existing instance remains conformant. Adding a required property to
populated rows therefore cannot be represented honestly as a model-only edit.
It needs a refactor that also supplies values, or it must fail.

### The operation layer is a small language

A concrete operation is syntax. An ordered operation plan is a program. The
meaning of that program is the state transition defined above.

The implementation should have one normative reference interpreter over
`GenericModel` and `GenericInstance`. XML, SQL, and C# execution paths are other
interpreters of the same operation language. They may execute very
differently, but they do not get to define different meanings.

This distinction prevents the provider layer from becoming three independent
sets of business rules. A SQL interpreter may compile an insertion directly to
`INSERT`, for example, without loading every row into memory. Its conformance
tests still compare the resulting abstract state with the reference
interpreter.

Operations should also be closed over their inputs. Current directory,
environment variables, timestamps, random values, connection lookup, and CLI
defaults are resolved before an operation is constructed. This keeps replay
and cross-surface comparison meaningful.

A CLI command is therefore not automatically an operation. It is a language
front end that may resolve user input and then request a query, operation plan,
transformation, generation, or external action.

## Provider law

For a supported surface `s`, let:

```text
encode_s : S -> Representation_s
decode_s : Representation_s -> S
execute_s : (operation, Representation_s) -> Representation_s
```

The central operation law is:

```text
decode_s(execute_s(o, encode_s(S))) = next(o, S)
```

This law is evaluated only for the declared supported subset of that provider.

Equality here is equality of the Meta graph, not equality of representation
bytes or physical row order. It preserves the model declarations, record IDs,
present versus absent text, exact stored text, relationship targets and roles,
and every order represented by relationships. It ignores XML whitespace,
shard placement, SQL row order, and C# collection order when that order is not
modeled.

Failure has a second law:

```text
if apply(o, S) fails,
execute_s(o, encode_s(S)) leaves encode_s(S) unchanged
```

For an ordered operation sequence:

```text
[o1, o2, ..., on]
```

the provider must preserve that order:

```text
decode_s(execute_s([o1, ..., on], encode_s(S)))
  = next(on, ... next(o2, next(o1, S)))
```

These laws are the acceptance target. Matching method names across providers is
not enough.

## Related functions outside the mutation layer

Several important repository functions should keep their own contracts.
In the signatures below, `S_P` means a workspace state for product `P`.

### Query

```text
q : S -> R
```

Queries read the current session state and do not create pending mutations.

### Model transformation

```text
t : S_P1 x ... x S_Pn -> S_Q
```

Examples include:

- MetaAnalytics to MetaTabular
- MetaDataWarehouse to MetaSql
- MetaTransformScript to MetaSql

A transformation creates a target workspace state from one or more source
workspace states. It is not an edit to its sources.

### Extraction and evidence-based derivation

```text
d : Inputs x ExplicitContext -> S_Q
```

Transform binding derives a binding workspace from modeled transform, schema,
and conversion inputs plus declared command options. Schema extraction derives
a schema workspace by observing an external database. These are related
construction processes, but the database is not an implicit input to binding.

### Rendering and generation

```text
c : S_P -> Artifact
```

Generated C#, SQL scripts, SSDT projects, and documentation sites are artifacts.

### External execution

```text
x : S_P x Environment -> Effects
```

Pipeline execution, orchestration, SQL deployment, analytical processing, and
MetaMesh process execution affect systems outside the metadata workspace.

These functions may open read sessions or require committed input. They should
not be forced into the workspace mutation vocabulary.

## Current foundation findings

### 1. `WorkspaceOp` mixes the three operation families

Relevant files:

- `Meta/Core/Operations/WorkspaceOp.cs`
- `Meta/Core/Operations/WorkspaceOperationApplier.cs`
- `Meta/Core/Services/OperationService.cs`

`WorkspaceOp` contains a text `Type` plus fields for every possible operation.
Most fields are meaningless for any one operation. `WorkspaceOperationApplier`
switches on `Type`.

Concrete findings:

- schema edits and row edits share one payload
- `BulkUpsertRows` combines insert, update, and replace behavior
- `RowPatch.ReplaceExisting` changes the meaning of an otherwise identical row
  patch
- `TransformInstances` is dispatched as a no-op and has no caller
- the `RenameEntity` operation path has no caller; the CLI uses
  `ModelRefactorService.RenameEntity`
- `WorkspaceOp.Description` has no consumer
- relationship and property defaults are fields on the common bag rather than
  part of the operations that require them

This is the same under-modeling pattern as a model entity with a `Kind`
property. Each semantic operation needs its own type and required data.

### 2. Operation ownership is split across services

The current generic mutation behavior is divided among:

- `WorkspaceOperationApplier`
- `ModelRefactorService`
- `InstanceRefactorService`
- `InstanceDiffService`
- `WorkspaceMergeService`
- `NormalizationService`

There is overlap. Entity rename exists in both the operation applier and model
refactor service. Some CLI commands use `WorkspaceOp`; others perform a
refactor directly and pass an empty operation list to common diagnostics.

The services already reveal the desired families:

- model and instance refactors live mostly in `ModelRefactorService`
- ID rename is an instance refactor
- diff application is an instance operation with explicit preconditions and
  postconditions
- workspace merge is a multi-input transformation into a new workspace

The v1.5 layer should classify and consolidate this behavior. It should not
wrap every existing service and preserve the split.

### 3. Normal execution copies the whole workspace repeatedly

`OperationService.Execute` captures a full before snapshot, applies one
operation, and captures a full after snapshot for undo and redo.

`Meta/Cli/CliRuntime.Core.cs` also captures the full workspace around an
operation sequence. Every implicit normalization operation goes through
`OperationService` and therefore creates two more complete snapshots.

Repository search found no production undo or redo caller. The feature is
covered only by `OperationServiceTests`.

For `n` operations, the normal command path can copy the complete model and
instance at least `2n + 1` times before persistence. This is the wrong cost
shape for a host session and becomes prohibitive for large instances.

Undo history is an editor concern. It should not impose full snapshots on CLI
and host execution.

### 4. Transaction ceremony is duplicated in CLI code

Generic refactor and diff commands repeatedly implement this sequence:

1. load
2. snapshot
3. mutate
4. normalize
5. validate
6. restore on failure
7. save
8. format output

Representative files:

- `Meta/Cli/Commands/Model/Schema/ModelRenameEntityCommand.cs`
- `Meta/Cli/Commands/Model/Schema/ModelRefactorPropertyToRelationshipCommand.cs`
- `Meta/Cli/Commands/Instance/Mutations/InstanceRenameIdCommand.cs`
- `Meta/Cli/Commands/Instance/Diff/InstanceMergeCommand.cs`

The CLI should own parsing and presentation. A session below the handler should
own state, rollback, conflict detection, and commit.

### 5. Generic XML persistence is stronger than typed XML persistence

`Meta/Core/Services/WorkspaceService.cs` currently provides:

- structural validation before save
- a workspace write lock
- staging and backup with rollback for caught model and instance write failures
- an optional expected fingerprint for optimistic conflict detection

The expected fingerprint exists but no production CLI caller supplies it.
A write lock prevents simultaneous writers during the save itself; it does not
prevent a stale process from overwriting changes loaded earlier.

The generic writer is not a proven crash-atomic workspace commit. It writes
`workspace.xml` separately, then publishes the staged `model.xml` and instance
directory in sequence. Its catch path can restore backups after an ordinary
exception, but a process or machine failure between moves can expose a mixed
generation. Staging, rollback, writer exclusion, stale-write detection, and
crash recovery are separate guarantees.

`Meta/Core/Serialization/TypedWorkspaceXmlSerializer.cs` currently provides:

- typed object-reference validation
- duplicate ID and required member validation
- deterministic ID ordering
- write-if-changed behavior

It writes `model.xml` and instance shards directly. It has no workspace lock,
staging transaction, or expected fingerprint. A failure after an earlier shard
write can leave a partially updated typed workspace.

The typed save also validates all rows and serializes every populated shard to
compare bytes, even when one command changed one row.

A shared session cannot promise one commit contract until these persistence
guarantees are stated precisely and reconciled.

### 6. MetaCli runtime hardcodes XML loading

Relevant files:

- `MetaCli/Core/MetaCliRuntime.cs`
- `Meta/Core/Serialization/IMetaWorkspaceModel.cs`

`MetaCliRuntime<TModel>` currently performs two XML-specific loads:

- `MetaCliModel.LoadFromXmlWorkspace(commandWorkspacePath)` loads the read-only
  command-surface document used for parsing and help
- `TModel.LoadFromXmlWorkspace(workspacePath)` loads the domain document for a
  workspace-bound handler

The second path is currently:

```text
TModel.LoadFromXmlWorkspace(workspacePath)
```

The runtime passes the loaded model to a workspace-bound handler, then forgets
it. It does not own mutation state or saving.

`IMetaWorkspaceModel<TModel>` itself includes XML-specific static load and save
methods. This makes XML selection part of the runtime type constraint.

Surface selection needs to move behind a workspace session/provider boundary.
The fluent handler binding API can remain small.

The command-surface workspace and domain workspace have different roles. A
host may cache the command-surface document as application registration data.
It should open a domain session only when the resolved handler acquires one.
Authoring the MetaCli workspace itself is an ordinary product mutation in a
separate invocation.

### 7. SQL is a bounded import/export path, not a live operation surface

Relevant files:

- `Meta/Adapters/ImportService.cs`
- `Meta/Adapters/SqlServerImportReader.cs`
- `Meta/Core/Services/SqlGenerationArtifacts.cs`
- `Meta/Adapters/SqlServerDeploymentService.cs`

The current generic SQL path can:

- inspect a SQL schema and load all rows into a generic workspace
- generate complete schema and data scripts from a generic workspace
- execute generated script batches

It cannot open a SQL-backed session and apply one semantic workspace operation.
Deployment executes batches individually without a transaction spanning the
generated deployment.

The SQL representation also includes location context. The current importer
uses the connected database name as the Meta model name and imports one
selected schema. A database/schema pair can therefore denote a Meta state, but
some model operations affect that mapping. `RenameModel`, for example, must be
declared unsupported in place, treated as an explicit database relocation, or
given another modeled representation. A provider must not silently reinterpret
it as an ordinary row update.

A SQL-backed Meta workspace is distinct from an external database described by
a product document. AdventureWorks is an input to MetaSchema extraction; it is
not thereby a SQL representation of a MetaSchema workspace. The latter would
store MetaSchema entity instances as relational rows. Session APIs and CLI
wording must keep those two uses of SQL separate.

The generic SQL representation uses text columns for generic properties.
That matches the current Meta kernel. The operation layer must not reintroduce
generic scalar datatypes. Product datatype meaning remains modeled through
MetaDataType and MetaDataTypeConversion.

### 8. C# has distinct source and compiled-object boundaries

`GenericModel` and `GenericInstance` are the representation-neutral in-memory
state used by the normative interpreter. They are not a C# source
representation.

The bounded C# source workspace is a real representation. Its model is expressed
as public sealed entity classes with automatic scalar and object-reference
properties. Its instance is expressed as constructed entity objects, entity
collections, and direct references between those objects. Roslyn supplies the
C# syntax, symbols, nullable annotations, constant semantics, and operation
tree needed to decode that form without executing it.

The current C# operation session supports all three operation families by:

1. decoding the owned source directory to abstract state through Roslyn
2. applying the normative operation plan to a private state
3. generating canonical C# source with direct object references
4. compiling and decoding the staged source through Roslyn
5. comparing the staged and pending abstract states before publication

This is semantic C# workspace support, not arbitrary source-preserving
refactoring. The writable directory may contain only marked Meta C# workspace
files. Constant string expressions are understood through Roslyn, but custom
methods, accessors, control flow, list mutations, project dependencies, and
unrelated source files are outside the accepted form.

Separately, generated product POCOs naturally support typed instance
operations:

- add and remove typed rows
- set text properties
- assign object relationships
- rename an ID while retaining the existing object references

Their compiled structure cannot gain or lose entity classes, properties, or
relationships at runtime. A schema operation against an already compiled typed
object graph must report an unsupported capability before changing state.
Regeneration and recompilation are the natural boundary for a changed product
model.

This is a real capability distinction. It should be explicit rather than
hidden behind a lowest-common-denominator API.

### 9. Implicit normalization changes more than representation

`Meta/Cli/CliRuntime.Core.cs` calls
`NormalizationService.BuildNormalizeOperations` after every generic operation
sequence. With no entity filter, the service scans every entity and may:

- trim record IDs
- trim relationship target IDs
- remove null scalar dictionary entries
- remove empty relationship entries
- replace records through `BulkUpsertRows`

These changes are not XML indentation or row sorting. They change the abstract
state, can affect records the command did not name, and incur the snapshot cost
of additional operations.

v1.5 must decide which conditions are loader invariants and which are explicit
semantic repairs. An interpreter must not append an invisible whole-workspace
mutation after every requested operation. Representation-only canonicalization
belongs in a codec and must not change `S`.

## Current Meta-BI findings

A focused scan excluding generated tooling, tests, object directories, and
demos found:

- 55 typed workspace save call sites
- 32 source files that participate in typed saving
- 53 typed workspace load call sites
- 26 source files that participate in typed loading

The exact count will change as code moves. The architectural point is that
persistence ownership is distributed.

### 1. Six product authoring services implement a generic API over typed POCOs

The following services use reflection, entity-name strings, property-name
strings, and relationship assignment dictionaries:

- `MetaAnalytics/Core/AnalyticsAuthoringService.cs`
- `MetaDataWarehouse/Core/DataWarehouseAuthoringService.cs`
- `MetaDataVault/Core/RawDataVaultAuthoringService.cs`
- `MetaDataVault/Core/BusinessDataVaultAuthoringService.cs`
- `MetaTabular/Core/TabularAuthoringService.cs`
- `MetaMultiDimensional/Core/MultiDimensionalAuthoringService.cs`

These are useful consolidation attempts, but their public contract is the
generic low-level surface wearing product names. Product code should author
typed entities and typed relationships. Shared reflection may remain an
internal tooling implementation detail; it should not define product
operations.

### 2. Services own different portions of persistence

Current examples:

- analytics, warehouse, vault, tabular, and multidimensional authoring services
  load and save by path
- `MetaPipelineWorkspaceService` receives a runtime-loaded model but also
  receives a workspace path and saves inside each mutation
- MetaOrchestration handlers receive a runtime-loaded model, call a mostly
  in-memory planning service, and save in the handler
- `MetaDataQualityPromotionService` has both a clean in-memory `Promote`
  operation and a `PromoteWorkspace` wrapper that saves
- MetaTransformScript SQL services load and save internally
- converters and extractors create complete target workspaces

`MetaDataQualityPromotionService.Promote` and
`MetaOrchestrationRunPlanningService` are close to the desired semantic service
boundary. They take a typed model, perform domain work, and return structured
results without formatting output.

### 3. Current process lifetime masks partial in-memory mutation

Several services append rows before running all domain validation. If a later
rule fails, the current command does not save and the short-lived process drops
the mutated model.

Verified examples:

- `AnalyticsAuthoringService.Add` appends `rowToAdd`, then calls
  `ValidateDomainRules`
- `DataWarehouseAuthoringService.Add` follows the same order
- `BusinessDataVaultAuthoringService.Add` appends the row before domain and
  satellite-specialization validation
- `BusinessDataVaultAuthoringService.AddSatellite` appends the base and
  specialization before validating the complete specialization set

That accidental rollback disappears in a long-lived host session. A failed
operation could poison the retained model unless the operation layer provides
strong exception safety.

Every operation must therefore:

- resolve and validate all preconditions before mutation where practical
- build a change plan before committing complex edits
- leave the session at its previous state on any failure

### 4. Converters and binders should remain transformations

MetaConvert, MetaSchema extraction, MetaTransformBinding, and most
MetaTransformScript import paths create a new product document from other
inputs. Their target persistence should use the same session/provider
infrastructure, but their semantic contract remains transformation or
elaboration.

Treating them as a long list of row edits would lose their useful whole-result
contract and produce poor SQL implementations.

## Preferred operation shape

### Concrete operation types

The operation vocabulary should use concrete types with required constructor
data. A conceptual shape is:

```text
ModelOperation
  AddEntity
  RemoveEntity
  RenameEntity
  AddProperty
  RemoveProperty
  AddRelationship
  RemoveRelationship

WorkspaceRefactor
  RenameModel
  RenameProperty
  RenameRelationship
  MakePropertyRequiredWithValue
  PropertyToRelationship
  RelationshipToProperty

InstanceOperation
  InsertRecord
  UpdateRecord
  UpsertRecord
  DeleteRecord
  RenameRecord
  SetProperty
  ClearProperty
  SetRelationship
  ClearRelationship
```

This list is illustrative. Existing behavior and tests must determine the
accepted first vocabulary.

There should be no `Operation.Type`, `Operation.Kind`, or common bag of optional
fields. Batch is composition of operations in order; it is not a discriminator
on one operation object.

Insert, update, replace, and upsert should have distinct contracts. They should
not remain modes on `BulkUpsertRows`.

The contracts must say what must already exist:

- insert requires the identity to be absent
- update requires the identity to be present and changes only named members
- replace requires the identity to be present and supplies the complete record
- upsert states explicitly what happens to an existing record instead of
  inheriting that choice from a boolean mode

Property values remain opaque text to the generic operation layer. It must not
infer or enforce scalar datatypes that are not modeled.

Large changes must not require one retained C# operation object per row.
Homogeneous `InsertRecords`, `UpdateRecords`, `ReplaceRecords`, and
`DeleteRecords` forms, or an equivalent streamed plan, can share the exact
preconditions and state transition of their scalar operations. A provider may
then use SQL bulk/set-based execution or write only affected XML shards. The
bulk form is an execution shape, not a mode that changes insert into replace or
upsert.

The bulk contract must state whether repeated target identities are rejected or
evaluated in submitted order. SQL table-valued or set-based execution must not
make that choice accidentally.

### Generic low-level entry point

The generic API operates on `GenericModel`, `GenericInstance`, entity names,
property names, IDs, and relationship roles. It serves:

- the `meta` CLI
- model refactoring tools
- imports and migrations
- provider implementation
- low-level recovery and diagnostics

This is the correct home for string-addressed metadata operations.

### Typed product entry point

Product CLIs should work through generated types:

- typed entities
- typed property selectors
- typed object relationships
- typed operation results

A typed operation facade can lower these calls to the generic operation
vocabulary by using generated model descriptors. Product services should not
construct generic entity/property dictionaries.

For example, a typed insertion conceptually supplies a real `Table`,
`PipelineTask`, or `DocumentationNarrative` object. The adapter extracts its ID,
text values, and relationship target IDs using sanctioned generated metadata.

Complex product commands may produce an ordered atomic plan of typed primitive
operations. `MetaPipeline add-step`, for example, creates several related rows
and dependencies as one domain action.

An operation plan is atomic within one workspace session. A plan that writes
several workspaces is a higher-level workflow and does not acquire
cross-workspace atomicity merely by sharing an API.

Internal steps in one plan may construct mutually dependent rows, but no
intermediate invalid state may be observable or committed. The complete plan
must produce a conforming final state or leave the session unchanged. Public
commands should not rely on staged-invalid workspaces between invocations.

The write side is easier than the read side. Existing product services receive
a complete typed POCO root and traverse `List<T>` collections. Returning that
same shape from a SQL-backed session requires full materialization and loses
the large-instance benefit.

The first typed facade should support bounded reads needed to plan a mutation,
such as identity lookup, relationship lookup, and selected entity scans. A
service may request an explicit full snapshot when its algorithm genuinely
needs one. This is not a reason to invent a universal query language, and it
does mean that some product services need real read-side refactoring before
they become efficient on SQL.

### Generated descriptors

The typed XML serializer already builds a private reflection map containing:

- model name
- entity collections
- ID members
- scalar members
- relationship members
- requiredness
- target entity types

This information should become a sanctioned generated-tooling descriptor or a
shared cached descriptor API. It should support typed operation lowering and
provider execution without adding CLI-specific code to generated tooling.

## Session boundary

A workspace session should own:

- one concrete workspace location
- the loaded model contract
- the compiled model type for a typed session
- the provider
- a baseline revision token
- ordered pending operations
- a current-state read capability
- commit and discard

Conceptually:

```text
openExisting(location)
create(location, initial state)
query(query)
apply(operation)
commit()
discard()
```

Opening and creating are different lifecycle contracts. `openExisting` requires
one valid represented state. `create` requires an available target and
publishes one supplied initial state. Product `--new-workspace` commands and
transformations use creation; they should not simulate it by opening an empty
or malformed workspace and applying row insertions.

At the generic boundary, "valid" means structurally conforming with identity
and relationship integrity. A typed product may add admission rules when its
service acquires the state. This does not require a generic public `check`
command.

Surface-specific target checks remain below the CLI:

- XML decides whether the target directory is available for creation
- SQL decides whether the selected database/schema target is available
- C# creates a new owned root

The provider does not have to materialize all of `S` to satisfy this contract.
An XML or in-memory session may hold a working graph. A SQL session may answer
queries and execute operations directly against a transaction. The abstract
state is the meaning of the session, not a mandatory in-memory data structure.

The public provider-neutral path should not expose a mutable model and then hope
that the session notices changes. Mutation must pass through `apply`.

A temporary migration adapter may expose the loaded model to existing services.
That adapter should be named as transitional because arbitrary mutation cannot
be translated to SQL or audited by a host.

### Concrete locations

Surface selection should use concrete location types, for example:

```text
XmlWorkspaceLocation(path)
SqlWorkspaceLocation(connection reference, database, schema)
InMemoryWorkspaceLocation(state owner)
CSharpWorkspaceLocation(path)
```

This is an implementation concept, not an approved API. A text `SurfaceKind`
plus unrelated optional fields would recreate the same modeling problem as
`WorkspaceOp.Type`.

The CLI syntax for selecting a non-filesystem workspace remains an open design
question. Current `--workspace` means a directory and defaults to the current
directory. That contract cannot identify a SQL-only workspace by itself.

The in-memory reference session works on a private copy. The C# source session
owns a marked source directory, keeps a private pending state, and publishes a
replacement directory on commit. A future typed object-graph facade still
needs an explicit ownership rule; allowing callers to mutate the same graph
would make discard and conflict detection impossible to guarantee.

One session has one authoritative location. It does not keep XML and SQL as
hidden live replicas. A team may choose either representation as its working
source of truth and request an explicit representation conversion when it
wants the other form. Synchronizing two independently edited representations
is a merge problem and remains outside the session contract.

## Atomicity, failure, and concurrency

### Operation atomicity

An operation must either produce its complete semantic result or leave the
session unchanged.

Preferred implementation techniques:

- validate and resolve first, then perform a small no-fail mutation block
- build a change plan before applying a multi-row refactor
- retain an ordered operation journal
- on an unexpected in-memory failure, rebuild the working state from the
  baseline and replay previously successful operations

Replay on failure is acceptable because it is the exceptional path. Full
workspace snapshots before and after every normal operation are not.

### Commit publication

Provider expectations:

- XML: stage a complete generation, exclude concurrent writers, reject stale
  baselines, and publish or recover the generation according to an explicit
  crash-consistency contract
- SQL: execute the operation batch in one database transaction
- C# source workspace: stage and compile a complete owned source directory,
  decode it through Roslyn, compare its semantic state, then swap the directory
- C# object graph: apply to a private working state, then publish the new root
  at commit

For XML, replacing several files or directories one after another is not one
filesystem-atomic action. v1.5 must either add a recoverable commit protocol
such as a durable journal/generation marker, choose a representation layout
with one atomic publication point, or state a weaker crash guarantee. A catch
block that restores backups handles ordinary exceptions but not abrupt process
or machine loss.

### Conflict detection

Every writable session needs a baseline token:

- XML can use the existing whole-workspace fingerprint
- SQL can keep the reads and writes for one command or batch in one transaction;
  a cache that outlives that transaction needs an explicit revision check
- C# source uses a whole-directory fingerprint
- a C# object graph can use object ownership or a caller-supplied revision token

The commit must fail before overwriting a state that changed after the session
opened.

### Invariant enforcement

The generic operation semantics and provider commit should enforce:

- identity uniqueness
- presence of required properties and relationships
- relationship target integrity
- operation-specific preconditions

Product rules remain in the product service that plans the operation. The
provider must preserve their accepted result, not rediscover product meaning
from generic rows.

Meta identity is case-insensitively unique. A SQL provider must enforce that
rule explicitly or reject a storage collation that cannot preserve it; ambient
database collation must not redefine identity.

This is commit and provider integrity. It does not require a public `check`
command to compensate for a weak persistence surface.

## Surface capability matrix

| Surface | Model operation | Model + instance refactor | Instance operation |
|---|---:|---:|---:|
| Generic XML workspace | Yes | Yes | Yes |
| Generic SQL workspace | Yes | Yes | Yes |
| C# source workspace | Yes | Yes | Yes |
| In-memory reference state | Yes | Yes | Yes |
| Generated typed C# state | No, requires regeneration | No, requires regeneration | Yes |
| XML workspace through typed tooling | No schema mutation of compiled type | No schema mutation of compiled type | Yes |
| SQL workspace for a known product model | Use generic schema path | Use generic schema path | Yes through typed facade |

The `Yes` entries cover the current fourteen-operation vocabulary, not every
future operation that might be added.

Providers should expose their capabilities and reject unsupported operations
before any mutation. The common API should not pretend every surface can do
everything.

## MetaCli integration

The desired command path is:

```text
tokens
  -> MetaCli parser
  -> bound handler
  -> product service or generic meta service
  -> semantic query, operation plan, transformation, or external action
  -> typed facade or generic reference semantics where applicable
  -> workspace session/provider
  -> structured result
  -> CLI presentation
```

For a mutating handler:

- MetaCli resolves the workspace location
- the runtime acquires a session
- the handler creates or selects a semantic operation
- the session applies it
- the runtime commits after successful handler completion
- the presenter formats the result

No handler or domain service should call `SaveToXmlWorkspace`.

The primary `--workspace` is only the common case. Binding, conversion,
extraction, merge, and generation commands may read several workspaces and
create a different target. MetaCli currently models their tokens and value
shapes, but it does not model whether an arbitrary path is a read dependency,
write target, or output location.

MetaHost must not guess those roles from option names. A bound handler should
receive a small execution context through which it explicitly acquires:

- its primary read or write session
- additional read sessions
- a new target location for a transformation
- external resources required by an interpreter

This acquisition is operational information supplied by the handler, not a
reason to add workspace-role concepts to every MetaCli option. It also lets a
host cache repeated reads and detect visibility barriers without turning a
multi-input transformation into a mutation of one workspace.

The existing fluent `.Bind(...)` surface can remain. Handler parameter types or
binding overloads can distinguish:

- invocation-only commands
- read-only workspace commands
- mutating session commands
- new-workspace or transformation commands

The final API should remain as small as the current runtime.

## MetaHost and MetaMesh implications

### What the operation layer enables

A host can retain descriptors, immutable read snapshots, and a commit
coordinator keyed by concrete workspace location. It can interpret MetaCli
invocations and avoid repeated workspace loads.

A mutable session belongs to one invocation or one explicit batch. Pending
state must not be shared merely because two clients address the same location.
Commands inside an explicit batch can reuse that session and commit once.

The execution context records the locations each handler actually acquires.
That gives the host an honest read/write set without parsing domain option
names or teaching MetaCli the meaning of every product command.

A retained host entry is not a permanently open database
transaction. SQL transactions should live only for an explicit command or
batch scope; otherwise an idle host would retain locks and stale snapshots.
The host may retain descriptors, connections, or read caches between scopes
without pretending that uncommitted state remains live.

The host can also commit several ordered mutations once when an explicit batch
scope permits it.

### What cannot be inferred safely

A host must not silently reorder commands or infer a batch solely because two
commands use the same executable.

MetaMesh operation steps preserve explicit predecessor order. That order must
remain exact in hosted execution.

The first host should execute invocations sequentially. Parallel scheduling is
a separate feature that would need declared read/write sets, deterministic lock
ordering, and an explicit meaning for concurrently ready MetaMesh steps.

External steps create visibility barriers. A process outside the host cannot
observe pending in-memory changes. Before such a step reads an affected
workspace, the host must commit it.

### Failure semantics need a decision

Current MetaMesh behavior persists every successful child command before the
next step. If step eight fails, the first seven changes remain.

Deferring all saves to the end of a MetaMesh operation would roll back the first
seven on failure. That may be desirable, but it is a behavior change.

Before MetaHost batching, choose and document one of these contracts:

1. commit after every command while retaining loaded state
2. commit contiguous hosted segments and flush at external visibility barriers
3. treat a complete MetaMesh operation as one transaction where all involved
   providers support that contract

A useful form of option 2 preserves the current successful-prefix behavior for
ordinary failures:

- retain each successful command in the session
- discard only the failed command
- commit the successful prefix when a later command fails
- commit once at the end of a fully successful segment
- commit before an external visibility barrier

This reduces clean-run persistence while keeping prior successful commands
after a reported child-command failure. It does not preserve current durability
if the host process or machine dies before the checkpoint. That stronger
promise requires a durable operation journal that can recover and publish the
successful prefix.

The third option cannot provide a real atomic transaction across arbitrary
filesystems, SQL databases, and external executables. It should not be claimed
without an explicit distributed transaction design.

Even a contiguous hosted segment may touch several workspace sessions. Each
session can commit atomically according to its own provider contract, while
the segment as a whole can still be partially committed if a later location
fails. The runtime must report that boundary plainly and must not label a
multi-location flush as one transaction.

### MetaMesh changes can remain small

MetaMesh already owns:

- workspace names
- operation names
- exact step order
- preflight
- child process outcomes

A future executor strategy can route MetaCli-aware steps through MetaHost and
retain the existing process executor as a fallback. MetaMesh does not need to
model every CLI command or workspace operation.

## Performance expectations

The layer has three separate performance effects.

### Same workspace, many commands

This is the strongest case:

- load once
- apply many operations
- validate at semantic boundaries
- save once

CLI-authored workspaces and long MetaMesh authoring operations can improve
substantially.

### Many independent workspaces

The public documentation regeneration touches dozens of distinct source
workspaces before merging them. Session reuse removes process and repeated
loader setup, but each changed workspace still has to be read or written.

Further gains there require incremental import, unchanged-input detection, or a
coarser transformation command. MetaHost alone cannot remove the real work.

### Very large instances

XML still requires file IO proportional to the affected shards and, for a full
export, proportional to the whole representation.

A SQL provider is the scalable operational surface for large instance sets:

- targeted DML for instance operations
- set-based refactors
- transactional DDL and DML
- XML export only when a team requests a versioned XML representation

Git can remain the selected source of truth for teams that choose it. SQL can
be the selected source of truth for other teams. The operation contract should
not decide that policy.

## Recommended implementation plan

### Phase 0: approve the contract (completed)

Agree on:

- the three mutation families
- concrete operation types with no discriminator string
- one normative reference interpreter
- closed operation inputs with no ambient dependencies
- operation and failure laws
- generic and typed entry points
- provider capability reporting
- session and commit ownership
- the distinction between semantic atomicity, writer isolation, stale-write
  detection, and crash consistency

No product model changes are required for this phase.

### Phase 1: build one vertical provider proof (completed)

Implement only enough operations to exercise all three families:

- one model operation
- one model and instance refactor
- insert/update/delete plus a relationship operation

Apply the same sequence to:

- a generic XML workspace
- a live SQL workspace
- a C# source workspace decoded by Roslyn

Decode all three and compare the complete abstract state.

Also prove:

- a rejected operation leaves every surface unchanged
- operation order is preserved
- stale XML commit is rejected
- SQL execution uses one transaction
- IDs remain case-insensitively unique

This proof should use a small existing test model or fixture. It should not
change a product model.

### Phase 2: replace the generic operation prototype (completed for public generic CLI operations)

- replace `WorkspaceOp.Type` and its property bag with concrete operation types
- remove the no-op `TransformInstances`
- consolidate duplicate entity/property/relationship refactors
- give insert, update, replace, and upsert separate contracts
- remove implicit whole-workspace normalization from the mutation tail and
  classify each current normalization rule explicitly
- move normal CLI execution off full before/after snapshots
- keep undo/redo outside the headless execution path

### Phase 3: introduce generic sessions (in progress)

- open existing generic XML and SQL locations
- create and open owned C# source locations
- maintain one baseline and ordered operation journal
- commit with explicit provider-specific guarantees
- use the existing XML fingerprint as an expected revision
- use a whole-directory C# fingerprint as an expected revision
- define and test the selected XML crash-consistency contract
- expose structured operation results

Migrate the generic `meta` mutation and refactor commands to this session.

### Phase 4: introduce the typed facade

- expose sanctioned generated model descriptors
- add typed entity/property/relationship operation builders
- add the smallest bounded typed read surface required by the selected proofs
- preserve object references as the C# integrity surface
- define exclusive ownership or copy-and-publish semantics for compiled typed
  C# state
- make typed XML commits staged, locked, and conflict-aware
- prove typed instance operations lower to the same generic semantics

Good first product proofs:

- `meta-data-quality promote`, because its core mutation is already separated
  from persistence
- MetaMesh `add-workspace`, `add-operation`, and `add-step`, because the service
  already mutates a runtime-loaded typed model and the handler currently owns
  saving

### Phase 5: migrate product authoring services

Suggested order:

1. MetaDataQuality promotion
2. MetaMesh authoring
3. MetaPipeline authoring aggregates
4. MetaOrchestration planning mutations
5. MetaCli authoring
6. reflection-driven analytics, warehouse, vault, tabular, and
   multidimensional authoring services
7. remaining MetaDocs and MetaWeave mutations
8. transformation output persistence

Each migrated service should:

- accept typed state or a typed read view
- create semantic operations
- return structured results
- contain no workspace path
- contain no load or save call
- contain no console output

### Phase 6: move session ownership into MetaCliRuntime

- resolve concrete workspace locations
- open read or write sessions according to the bound handler
- provide explicit acquisition of additional read sessions and transformation
  targets
- commit successful mutations
- discard failed mutations
- preserve the existing compact fluent runtime setup

At this point CLI handlers become thin enough for MetaHost to invoke safely.

### Phase 7: add MetaHost

- host MetaCli application registrations
- interpret invocations inside the host
- retain location coordinators and immutable read caches
- scope mutable sessions to one invocation or explicit batch
- preserve command and MetaMesh step order
- define explicit batch and visibility barriers
- add invocation identity and durable result lookup before retrying mutations
- stream ordinary CLI output and exit status back to the caller
- keep process execution for commands that are not host-aware

MetaHost should follow the operation/session contract. It should not become the
place where missing operation semantics are improvised.

The CLI grammar should remain unchanged when a host is present. A client may
forward the original application identity and tokens to MetaHost, which then
uses the same MetaCli model, parser, bindings, and presenters as local
execution. Host absence may cost performance, but it must not change command
meaning for ordinary commands.

Batch scope must be explicit. If a batch promises only coalesced persistence,
local execution may preserve current per-command commits and lose only the
optimization. If it promises all-or-nothing failure semantics, a local runner
must implement the same contract or reject the batch; it cannot silently fall
back to separate commits.

Mutating invocations are not generally idempotent. If a host commits and the
client loses the reply, an automatic retry can repeat or contradict the first
operation. Reliable retry therefore needs a stable invocation ID and a durable
result journal. Insert must not quietly become upsert to hide a transport
failure.

## Decisions required before implementation

1. Should operations initially exist only as C# semantic types, or is there an
   immediate requirement to persist operation documents?

   Recommendation: use executable C# types first. Add a MetaOperations product
   model only when persisted plans, interchange, or audit require it. If such a
   model is added, each operation family and concrete operation must be modeled
   structurally without `Kind`. A crash-durable MetaHost journal would be one
   concrete reason to revisit this decision.

2. How does a CLI identify a SQL-only workspace while preserving the current
   `--workspace` directory default?

3. Does a hosted MetaMesh run commit each command, each contiguous hosted
   segment, or the operation as a whole? What explicit batch identity lets
   separate CLI processes join that scope without sharing pending state with
   unrelated clients?

4. Are generic operation plans accepted as public product input, or are they
   trusted output from a typed product service?

   Recommendation: initially treat them as trusted output from the service that
   owns product validation. Do not create a second generic rules engine that
   attempts to reconstruct product meaning.

5. Which bounded typed read primitives are needed for the first product
   migrations, and which services legitimately require a full snapshot?

   Recommendation: support explicit read snapshots where useful, but never
   expose mutable state as the mutation API or pretend that a snapshot is free
   on SQL.

6. What is the smallest typed descriptor contract needed by XML, SQL, and C#
   providers without generating provider-specific or CLI-specific code into
   product tooling?

7. What ownership contract applies when a typed product facade operates on an
   already compiled C# object graph?

8. What XML crash-consistency guarantee is required, and what durable
   publication or recovery mechanism proves it?

9. How are target creation and commit reported for transformations that read
   several workspaces and produce another? This should reuse location and
   persistence infrastructure without pretending to be one multi-workspace
   mutation.

## Acceptance criteria for v1.5

- The same accepted operation sequence produces the same abstract state through
  generic XML, generic SQL, and the bounded C# source workspace.
- Direct provider interpreters conform to one normative reference interpreter.
- Operations contain all semantic inputs and do not read ambient process state.
- Existing-workspace open and new-workspace creation have distinct
  preconditions on every supported surface.
- Generated typed C# supports natural object-reference instance operations.
- Unsupported schema changes against compiled typed C# fail before mutation.
- No public operation uses `Kind`, `Type`, or a null-heavy common payload.
- Rejected operations and ordinary commit failures leave the prior surface
  unchanged.
- Applying one operation does not normalize unrelated records implicitly.
- Ordered operations execute in the submitted order.
- XML and SQL commits reject or serialize concurrent writers correctly.
- The XML crash-consistency guarantee is explicit and covered by recovery
  tests.
- Product services do not load, save, or format CLI output.
- MetaCliRuntime owns session lifetime for migrated commands.
- A hosted sequence can load once and save once without changing command
  meaning silently.
- Large SQL-backed instance operations execute as SQL operations rather than
  materializing the whole workspace in memory.

## Explicit non-goals for the first slice

- no MetaHost implementation
- no MetaMesh model change
- no product model change
- no generic datatype addition
- no universal query language
- no generic transformation model
- no attempt at distributed transactions across external systems
- no rewrite of converters, binders, renderers, or execution engines as row
  mutations
- no claim that every surface supports every operation
