# Meta Operations Audit

Date: 2026-07-31

## Current Resolution

The implementation audited below was discarded. Commits `1c2648e` through
`1359734` were reverted locally by `f4a01ea`, `6232a1c`, `e4ea54b`, and
`b8bc364`. The replacement working tree does not retain the rejected stores,
sessions, providers, planners, typed facade, temporary identities, or partial
CLI migrations.

The replacement is deliberately smaller:

- The replacement kernel is the separate `Meta.Operations` project. It has no
  project references, package references, or source dependency on legacy
  workspace, services, serialization, configuration, generation, or snapshot
  code. Existing `Meta.Core` references `Meta.Operations`, never the reverse.
- `InMemoryWorkspace` contains only a `GenericModel` and `GenericInstance`.
- Twenty-two concrete model, instance, and atomic refactor operations are
  nested under `Operation`
  (`Operation.AddEntity`, `Operation.InsertRecord`, and so on). Universal name
  and identity constraints are enforced when each operation is constructed;
  the operations then run in order through one in-memory target and validate
  after each operation. Relationship
  operations cover role rename, target change, and requiredness directly, so
  populated relationships do not need a lossy remove-and-add sequence. Atomic
  property-to-relationship and relationship-to-property refactors preserve
  optionality and return structured semantic outcomes on both in-memory and
  SQL surfaces.
- The operation language expresses model, instance, and refactor operations as
  base-type relationships. SQL uses those relationships to re-read and validate
  the catalog after every model-changing or refactor operation; there is no
  textual operation `Kind` or behavior flag.
- Workspace reads are eight independent primitives: read the model name,
  enumerate entity names, enumerate one entity's properties, enumerate one
  entity's relationships, stream one entity's records, and read one record by
  identity, count one entity's records, and run one bounded record query with
  an exact total count. Record queries support only the existing CLI semantics:
  conjunctive case-insensitive equality and contains conditions, ordered by
  identity. The condition alternatives are concrete types rather than a text
  `Kind` or generic expression tree. SQL record lookup, count, filtering, and
  limiting execute in SQL rather than scanning instance tables into memory.
  There is no snapshot read or special full-copy operation.
- `WorkspaceComposition` builds an in-memory destination by composing those
  reads with the existing `AddEntity`, `AddProperty`, `AddRelationship`,
  `InsertRecord`, and `SetRelationship` operations. Required references are
  inserted in dependency order and optional references are applied after all
  records exist. The fresh private destination is validated once at the end.
- XML, C#, and SQL have bounded readers and writers over the same
  semantic state.
- XML workspace configuration and shard placement remain private XML concerns.
- SQL operations execute typed transactional DDL/DML directly and rely on SQL
  constraints for RI; they do not materialize instance tables.
- SQL implements the read primitives directly against catalog queries and a
  streamed data reader. `MetaSqlReader` is only the explicit composition from
  those primitives to an `InMemoryWorkspace`; normal SQL operations never call
  it.
- Model, entity, relationship, and record renames return structured outcomes
  whose counts describe semantic changes rather than provider-specific rows,
  shards, or files. Rename operations name both the expected current identity
  and the replacement identity, so they fail against the wrong source state.
- SQL source primitives verify the structural RI they rely on: every entity
  has a non-null `NVARCHAR(450)` case-insensitive `Id` primary key, and every
  modeled foreign key is single-column, enabled, trusted, and targets `Id`.
  Relationship columns use that same identity representation, while property
  columns use `NVARCHAR(MAX)`. Arbitrary SQL scalar types are rejected instead
  of being silently converted to text during a lossy read. These are catalog
  checks, not instance-table scans.
- SQL workspaces also reject active database behavior that the common model
  cannot preserve: computed/defaulted columns, active check constraints and
  triggers, non-primary unique indexes, cascading foreign-key actions,
  cross-schema foreign keys, system-versioned tables, and active row-security
  policies. Ordinary non-unique indexes and other physical tuning remain
  allowed because they do not change operation meaning.
- `SqlWorkspaceSource.OpenAsync` builds and validates the complete model from
  catalog metadata once inside a serializable read transaction held until the
  source is disposed. Direct SQL count/query/stream calls therefore cannot
  bypass model-level rules such as member collision and required-reference
  cycle detection or combine a cached model with rows from another database
  state, while instance rows remain in SQL.
- SQL mutation reads the same catalog model inside its serializable transaction
  before applying operations and after each model-changing/refactor operation.
  SQL constraints continue to own instance RI. If SQL accepts DDL that violates
  the Meta model contract, kernel validation fails before commit and the DDL is
  rolled back.
- C# sources compile before they are read. The bounded authorable dialect must
  expose one static `BuiltIn` root, return every entity collection exactly
  once, and avoid collection mutations or nested relationship assignments that
  cannot be interpreted faithfully.
- Structural names use the portable 128-character contract and
  case-insensitive uniqueness while preserving authored spelling.
- Record identities are values, not object names. They use the 450-character
  key contract on every surface and explicit case-insensitive SQL collation.
- The atomic property-to-relationship refactor no longer contains the old
  repeated-source-value gate. Repetition can be evidence for suggesting a
  refactor, but it is not part of the validity of an explicitly requested
  refactor.
- The kernel validator no longer guesses that a scalar `<Entity>Id` property
  is a pending relationship. Naming-based suggestions belong in model
  suggestion tooling, not in the representation-neutral validity contract.
- XML, SQL, C#, generation, workspace persistence, CLI, and application
  boundaries call the kernel `WorkspaceValidator` directly. The duplicate
  `ValidationService` contract and adapter have been removed.

The full `meta` solution builds with no warnings or errors. The current focused
verification passes Meta.Operations 18/18, MetaCli 28/28, MetaDocs 43/43,
MetaMesh 12/12, MetaWeave 21/21, and the changed live-SQL read, model-contract,
transaction-rollback, refactor, and unsupported-behavior tests. Every
non-CLI Meta.Core test class passes when run in focused groups, including the
complete live-SQL class at 21/21. The monolithic Meta.Core run and one broad
multi-class subset did not complete and left `dotnet test`/`vstest` child
processes that had to be stopped, even though the same classes pass in focused
runs. This is an aggregate runner/shared-process-state problem; the previous
pre-hardening baseline passed all 389 tests, but that aggregate result is not
claimed for the current working tree.
The canonical meta-bi CLI build packs `Meta.Operations`, `Meta.Core`, and
`Meta.Adapters` in dependency order and builds all ten standard CLI executables
with zero warnings or errors.
The relationship-contract operations are exercised against both in-memory
state and live SQL Server. The primitive-read and composition tests cover
independent model, entity, property, relationship, record-stream, and
record-by-identity reads,
required-reference ordering, optional-reference deferral, and semantic
equality after materialization. The live SQL round trips exercise the streamed
SQL source. Cross-repository CLI builds pass against locally packed foundation
packages. Focused `meta-bi` tests run so far also pass, but the complete
`meta-bi` test catalog has not been rerun in this recovery pass.

SQL Server database rename cannot participate in the transaction used for
ordinary DDL and DML. `Operation.RenameModel` is therefore accepted by the SQL
target only as a single operation. Every other SQL operation sequence is one
transaction.

The remaining work is intentionally outside this kernel: several direct model
and instance CLI commands now use typed operations, while legacy refactor,
merge, diff, import, query, and representation-emission paths remain to be
migrated. C# support is currently a bounded workspace dialect rather than
arbitrary C#, and no host/session architecture has been introduced. The legacy
`Workspace` object still carries an internal
`XmlWorkspaceLayout` so the old XML load/save API can preserve shard placement
across renames. That state is absent from `InMemoryWorkspace` and every new
operation/source contract, but removing it requires replacing the old XML API
with an explicit XML document boundary.

Explicit SQL-to-XML or SQL-to-C# conversion materializes the complete workspace
in memory. That is the accepted boundary for those destinations; normal SQL
queries and mutations remain database-native and do not acquire that memory
cost. A `SqlWorkspaceSource` holds serializable read locks for consistency, so
callers must keep its existing `await using` lifetime tight.

The sanctioned SQL contract rejects active database behavior that the Meta
model cannot represent while allowing harmless physical details such as
non-unique indexes. It deliberately does not require byte-for-byte generated
SQL shape.

Normalization, import/upsert, instance diff and merge, and workspace merge are
higher-level planning algorithms. Their effects are expressible by composing
the current primitive reads and operations; they should not become opaque
operation-language terms. The legacy relationship `--existing-column` path is
an XML representation repair for temporarily malformed stored state and also
does not belong in the common semantic operation language.

The legacy `WorkspaceSnapshots` rollback utility now composes the single deep
copies owned by `GenericModel` and `GenericInstance`; it no longer duplicates
their field structure or contains the unused row-patch conversion.

Everything after this section is the historical red-team record of the
discarded implementation. Its findings explain why that implementation was
removed; they do not describe the current replacement tree.

## Scope

This was a red-team audit of the discarded Meta Operations changes in `meta`,
including the XML, C#, and SQL workspace stores, the common operation session
API, CLI migrations, and the temporary MetaMesh adoption work.

No implementation repair is included in this audit. The user-owned untracked
files `docs/META-LANGUAGE-KERNEL.md` and
`../meta-bi/docs/video-series/` were not changed.

The audited working tree contains 78 modified or deleted tracked files and 10
new implementation or test files. The tracked diff is 1,197 insertions and
2,987 deletions.

## Verification

The full solution does not compile.

`dotnet build Metadata.Framework.sln --no-restore --nologo -m:1 -nr:false`
fails with:

1. `MetaDocs/Core/MetaDocsWorkspaceFactory.cs`: `Workspace.WorkspaceConfig`
   no longer exists.
2. `MetaDocs/Core/MetaDocsWorkspaceInstanceImporter.cs`: two references to
   `GenericRecord.SourceShardFileName` no longer compile.
3. `MetaWeave/Core/MetaWeaveService.cs`: `Workspace.WorkspaceConfig` no longer
   exists.

The same removed members remain in unchanged consumers under:

- `MetaWeave/Tests`
- `../meta-bi/MetaSchema/Core/MetaSchemaWorkspaceFactory.cs`
- `../meta-bi/MetaDataVault/Tests`

`dotnet test Meta/Tests/Meta.Core.Tests.csproj --nologo -m:1 -nr:false`
compiled and ran 303 tests: 297 passed and 6 failed.

The failures prove:

1. `model rename-relationship --existing-column` no longer completes its
   sanctioned one-shot recovery.
2. entity rename no longer updates XML `EntityStorage`.
3. collision detail disappeared from `model rename-relationship`.
4. duplicate-lookup detail disappeared from property-to-relationship.
5. conflicting existing-relationship detail disappeared from
   property-to-relationship.
6. successful property-to-relationship output no longer reports whether the
   property was dropped.

`git diff --check` reports no whitespace errors. It does report broad
LF-to-CRLF working-copy warnings across the changed files.

## Stop-Ship Findings

### 1. The cross-repo tree is broken

Removing XML representation fields from the semantic domain is directionally
correct, but the replacement was not completed before their public members were
removed. Foundation and BI consumers now fail to compile.

Disposition: repair every consumer and run both repositories before any commit.

### 2. XML workspace creation claims ownership of the whole directory

`Meta/Core/Operations/XmlMetaWorkspaceStore.cs:51` requires the destination to
be absent or empty, stages a complete sibling directory, and moves that
directory into place.

`meta init .` historically initializes workspace files inside an ordinary
project directory. The new store rejects any directory that already contains
unrelated files.

Disposition: redesign XML creation/publication around the files owned by the
XML representation. Do not give it ownership of an arbitrary project folder.

### 3. XML layout was hidden, then callers hardcoded the default layout

After `WorkspaceConfig` was removed from `Workspace`, these callers began
assuming `model.xml` and `instances`:

- `Meta/Cli/Commands/Instance/Diff/InstanceDiffModelFiles.cs`
- `Meta/Cli/Runtime/Workspace/CliRuntime.WorkspaceStatus.cs`

Valid XML workspaces can configure different model and instance paths.

Disposition: expose XML document/layout information through the XML provider,
not through semantic state and not through hardcoded defaults.

### 4. XML shard placement is hidden object-attached state

`WorkspaceService` stores `XmlWorkspaceStorage` in a static
`ConditionalWeakTable<Workspace, XmlWorkspaceStorage>`. Two otherwise identical
`Workspace` objects can therefore save differently depending on how they were
constructed or loaded. The state is not inspectable through the object or a
session.

Disposition: use an explicit XML document/session object that owns semantic
state and private XML layout together.

### 5. Entity and record renames lose shard placement

`XmlWorkspaceStorage` keys shard placement by
`entityName + "\0" + recordId`. Semantic rename operations change those values,
but no XML provider logic moves the private placement entries.

The old entity rename also updated `workspace.xml` `EntityStorage`; that update
was removed. The failing test confirms the stale row remains.

Disposition: XML must project semantic renames into its private layout state in
the same transaction.

### 6. XML layout state mutates before file publication succeeds

`WorkspaceService.BuildInstanceShardWritePlans` changes shard assignments and
removes old assignments before staged instance files have been published. A
write failure can therefore leave the in-memory XML layout disagreeing with the
unchanged files. Snapshot rollback does not include the hidden sidecar.

Disposition: build a candidate layout, publish successfully, then replace the
provider state.

### 7. New XML records have an undocumented placement policy

The provider sends a new record to the lexically first existing shard for the
entity. `workspace.xml` `EntityStorage` is normalized and persisted but is not
used for placement. There is no public XML-specific API for selecting a shard
after `SourceShardFileName` was removed.

Disposition: either define one honest XML placement contract or remove the
inert storage configuration.

### 8. The one-shot relationship recovery command was broken

`--existing-column` loads malformed declared/storage state through a temporary
relationship-column mapping. The new `XmlMetaOperationSession` computes its
baseline from that recovered state, but commit reloads the disk workspace
without the mapping to check the fingerprint. That reload fails before the
repair can be saved.

The existing focused test now returns exit code 4 instead of 0.

Disposition: optimistic concurrency must fingerprint the same XML document
view that the session opened.

### 9. Relationship identity spelling has three different meanings

- `MetaOperationPlanner` compares relationship target IDs case-insensitively.
- `CSharpMetaStateSignature` canonicalizes them to the target record's spelling.
- `GenericMetadataStateComparer` compares relationship values ordinally.

A transition can therefore be ignored by the planner, accepted by the C#
writer, and rejected by the final generic comparison.

Disposition: decide once whether a relationship stores text or a reference.
Because it is a reference, provider readers should likely resolve it to the
target identity before neutral comparison.

### 10. Domain diagnostics and results were erased by generic CLI execution

The migrated refactor commands previously returned rows touched, fields
renamed, rows rewritten, property-dropped state, and precise conflict causes.
`IMetaOperationSession` now returns only `AppliedOperationCount`, and generic
validation/error formatting replaces several specific causes with
`Cannot complete ...`.

Four of the six current test failures demonstrate this regression.

Disposition: operations need structured, operation-specific outcomes, or the
domain service must remain the command boundary. Architectural uniformity
cannot make the CLI less truthful.

### 11. MetaMesh bypasses the new operation boundary

`MetaMeshWorkspaceService` again mutates generated POCO lists directly and
`MetaMesh/Cli/Program.cs` calls `SaveToXmlWorkspace` directly.

Disposition: record this as deliberately unmigrated. Do not present MetaMesh as
proof of the common operation architecture.

### 12. Existing-workspace CSV import also bypasses the boundary

`Meta/Cli/Commands/Pipeline/Import/ImportCommand.cs:107` directly saves the
mutated workspace returned by the import service.

Disposition: the import service should return an explicit operation plan or
another owned structured mutation result. Do not infer a generic plan from an
arbitrary after-state.

### 13. `CreateState` is not a complete or self-contained creation operation

`MetaOperationPlanner.CreateState` omits model identity and assumes the caller
has already constructed an empty state with exactly the target model name. It
does not verify its own reconstruction. The SQL store contains a second block
of setup and verification to make it work.

Disposition: remove it from store creation or give it explicit source
preconditions and internal reconstruction proof.

### 14. SQL creation is operation-per-schema-item and operation-per-row

`SqlServerMetaWorkspaceStore.CreateAsync` turns the entire state into a long
operation plan and applies every entity, property, relationship, and record
individually. This is not a credible creation path for the large SQL-backed
workspaces that motivated the SQL surface.

Disposition: use provider-owned set-based or bulk creation, followed by semantic
readback verification.

### 15. SQL property-to-relationship rewriting is row-by-row

The SQL refactor loads affected rows into memory, runs the generic service, and
issues one update per source row in `WriteRelationshipValuesAsync`.

The centralized semantics are good. The physical application strategy is not a
finished SQL implementation.

Disposition: retain the semantic service, but apply the result set-wise or
through a bulk staging relation.

### 16. Documentation and active context now describe code that is being deleted

`docs/META-OPERATIONS-V1_5-RECONNAISSANCE.md` still describes the generated
typed facade, MetaMesh adoption, complete operation coverage, and stronger
round-trip fixtures that are no longer present in this working tree.

`docs/SERVICES_API.md` and `../meta-bi/docs/ACTIVE_CONTEXT.md` also retain the
rejected architecture as current truth.

Disposition: rewrite the durable documentation after the architecture is
settled. Do not stack another progress narrative on top of the obsolete one.

## Explicit Limitations Requiring Decisions

### 17. The common store interface hides incompatible ownership contracts

`IMetaWorkspaceStore.CreateAsync` means:

- create files inside an XML workspace root,
- own and replace an entire C# source directory,
- create and own a SQL Server database.

The interface has no ownership or capability contract, while SQL cannot perform
model rename and each provider has different representability limits.

Disposition: tighten the contract before adding more consumers. Capabilities
must be modeled through real API structure, not a string `Kind`.

### 18. C# is a bounded canonical dialect, not arbitrary natural C#

The reader requires the emitted `BuiltIn` factory/root/collection shape. The
session owns a directory containing only marked `.cs` files and replaces it as
a unit. It does not preserve arbitrary user source, projects, comments, or
equivalent alternative C# constructions.

This can be a valid first-class representation if named honestly.

Disposition: define the supported C# workspace language explicitly.

### 19. Valid Meta names can be rejected by C#

`CSharpMetaLanguage.RequireIdentifier` rejects C# keywords and contextual
keywords instead of emitting escaped identifiers such as `@class`.

Disposition: support reversible escaped identifiers if C# is a serious
first-class representation.

### 20. The C# writer still belongs to `GenerationService`

Both create and commit call `GenerationService.GenerateCSharpWorkspace`, and
`CSharpMetaWorkspaceStore.CreateAsync` creates a session only to discard it.
This retains the old generated-tooling framing inside the representation layer.

Disposition: extract a C# workspace writer and separate creation from session
opening.

### 21. SQL cannot represent every currently valid Meta identity

Generic Meta accepts any nonblank trimmed text identity. The SQL contract limits
IDs to 128 printable ASCII characters and uses a case-insensitive collation.
Unicode and longer record IDs are valid in XML but rejected by SQL.

This restriction predates the current uncommitted diff, but the new isomorphism
claims make it a direct blocker.

Disposition: either narrow Meta identity globally through a deliberate language
decision or make SQL carry the full identity repertoire.

### 22. SQL model rename is unsupported

The SQL session explicitly rejects `RenameModelOperation` because the model
identity is the database identity and database rename cannot occur inside the
current transaction.

This is an honest failure.

Disposition: keep the rejection until store/location-level database rename is
designed. Do not claim complete operation parity.

### 23. The operation vocabulary does not express every model transition

For example, property requiredness can be changed, but relationship requiredness
cannot. Relationship target changes and several other arbitrary contract
transitions also lack an operation.

Disposition: stop claiming a complete mutation language. Add operations only
from concrete semantic requirements.

### 24. `UpdateInstance` is reconciliation, not authored intent

The desired-state planner cannot infer renames and represents them as
delete/insert. It rejects case-only record-ID spelling changes after the
temporary-ID workaround was removed.

Disposition: keep it as a bounded diff/merge adapter if needed. Do not make it
the primary service mutation API.

### 25. Plan validity policy is implicit

The generic interpreter validates the complete source and final state, but only
the model after each non-instance operation. SQL relies on constraints and its
operation implementations between plan boundaries.

Disposition: state clearly whether a plan is atomic and may pass through
temporarily nonconforming instance states, then enforce the same policy on each
provider.

### 26. SQL rejects harmless secondary indexes

The existing SQL storage validator treats every secondary index as
behavior-changing, including ordinary non-unique indexes that change
performance rather than semantic state.

Disposition: distinguish semantic uniqueness/filter behavior from harmless
physical indexing.

### 27. SQL accepts only an exact generated physical contract

The validator requires exact generated check constraints and other catalog
details. That is robust for an owned canonical database dialect, but it is
brittle for a natural SQL authoring surface.

Disposition: decide whether SQL is an owned canonical representation or a
natural equivalent schema. Current wording mixes both.

### 28. Cleanup can hide retained artifacts

- XML and C# staging cleanup can replace the original exception.
- SQL database cleanup swallows all failures and can leave a database without
  reporting it.
- C# backup deletion failures are swallowed and can leave backup directories.

Disposition: preserve the primary failure while explicitly reporting retained
artifacts and cleanup failures.

### 29. The in-memory session is outside the common session API

`InMemoryMetaOperationSession` does not implement `IMetaOperationSession`, while
the common result omits the pending state and all domain-specific outcomes.

Disposition: revisit the session contract as one coherent design rather than
adding adapters around each mismatch.

### 30. The new proof suite is materially narrower than the deleted claims

The current store and composed-round-trip tests use a small constructed state.
The prior checked-in Enterprise BI fixture and artifact-tree comparisons were
removed, while durable documentation still claims them.

Disposition: keep the smaller laws, remove false claims, and add a larger
checked-in fixture later without rewriting model identity to make a provider
fit.

## Sound Decisions Worth Keeping

1. Removing `WorkspaceConfig` and `SourceShardFileName` from neutral semantic
   state is correct. The incomplete XML-provider replacement is the defect.
2. The operation categories are actual code types, not a vague textual `Kind`.
3. SQL model rename fails explicitly instead of silently simulating success.
4. SQL refactors reuse the normative generic refactor semantics; their scale,
   not their meaning, is the problem.
5. Optional relationship cycles are inserted after all rows exist, while
   required cycles are rejected consistently.
6. Staged write, readback, semantic comparison, and publication is the right
   overall persistence shape once ownership and rollback are corrected.
7. The temporary-ID rename workaround is gone. No generated temporary record
   identity remains in the current planner.

## Recommended Recovery Order

1. Restore a compiling cross-repo baseline without reintroducing representation
   fields into neutral semantic state.
2. Replace the hidden XML sidecar with an explicit XML document/session and
   repair XML creation, configured paths, shard rename, rollback, and recovery
   fingerprinting.
3. Restore precise CLI outcomes and diagnostics.
4. Decide the common store ownership/capability contract.
5. Make C# an explicitly named canonical source dialect and remove generated
   tooling ownership from the writer.
6. Make SQL creation and refactors set-based, then settle identity repertoire
   and natural-versus-owned SQL policy.
7. Expand the operation vocabulary only from real CLI/service requirements.
8. Rewrite stale documentation and rerun both repositories serially.
