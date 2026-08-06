# Meta Operations Migration

Date started: 2026-08-03

## Completion Rule

A slice is complete only when:

1. Every selected consumer uses `Meta.Operations` reads and operations.
2. The displaced legacy implementation, dispatch token, and registration are deleted.
3. Repository search finds no remaining reference to the deleted path.
4. Focused behavioral tests pass on the migrated public surface.
5. Foundation and affected downstream projects build against local sources.

External CLI tests spawn the existing executable. Build the affected CLI before running its focused tests; building the test project alone does not refresh that executable.

Compatibility wrappers and deprecated aliases do not count as migration.

## Ownership Target

- `Meta.Operations`: semantic state, constraints, validation, reads, operations, and composition.
- `Meta.Core`: deterministic XML workspace serialization, C# emission, and higher-level algorithms composed from reads and operations.
- `Meta.Adapters`: SQL representation access, Roslyn-based C# ingestion, and import/export integration.
- CLIs: argument handling, delegation, and presentation.

## Slices

| Slice | Consumer | New path | Legacy deletion | Status |
| --- | --- | --- | --- | --- |
| 1. Model add entity | `meta model add-entity` | `Operation.AddEntity` through `InMemoryOperations` | `WorkspaceOpTypes.AddEntity`, old switch branch, old applier method | Complete |
| 2. Model add property | `meta model add-property` | `Operation.AddProperty` through `InMemoryOperations` | `WorkspaceOpTypes.AddProperty`, old switch branch, old applier method | Complete |
| 3. Model drop property | `meta model drop-property` | `Operation.RemoveProperty` through `InMemoryOperations` | `WorkspaceOpTypes.DeleteProperty`, old switch branch, old applier method | Complete |
| 4. Model add relationship | `meta model add-relationship` | `Operation.AddRelationship` through `InMemoryOperations` | `WorkspaceOpTypes.AddRelationship`, old switch branch, old applier method | Complete |
| 5. Model drop relationship | `meta model drop-relationship` | CLI selector resolution followed by `Operation.RemoveRelationship` | `WorkspaceOpTypes.DeleteRelationship`, old switch branch, old applier and resolver methods | Complete |
| 6. Model set property required | `meta model set-property-required` | `Operation.SetPropertyRequired` through `InMemoryOperations` | `WorkspaceOpTypes.ChangeNullability`, old switch branch, old applier method | Complete |
| 7. Model rename property | `meta model rename-property` | `Operation.RenameProperty` through `InMemoryOperations` | `WorkspaceOpTypes.RenameProperty`, old switch branch, old applier method | Complete |
| 8. Model drop entity | `meta model drop-entity` | CLI diagnostics followed by `Operation.RemoveEntity` | `WorkspaceOpTypes.DeleteEntity`, old validation-headline branch, switch branch, and applier method | Complete |
| 9. Dead legacy operation cleanup | No public consumer | No replacement needed | Duplicate `WorkspaceOpTypes.RenameEntity` path and unused no-op `TransformInstances` token/branch | Complete |
| 10. Direct instance mutation | `meta insert`, `meta instance update`, `meta instance relationship set`, `meta delete` | `Operation.InsertRecord`, property/relationship primitives, `Operation.SetRelationship`, and `Operation.DeleteRecord` | Direct `RowPatch`, bulk resolver, and delete-row paths | Complete |
| 11. Bulk insert | `meta bulk-insert` | CLI input planning composed from `InsertRecord`, `SetProperty`, `SetRelationship`, and `ClearRelationship` | `BulkUpsertRows` dispatch and relationship resolver | Complete |
| 12. Legacy mutation engine removal | No remaining public consumer | No replacement needed | `WorkspaceOp`, `WorkspaceOperationApplier`, `OperationService`, dead data-batch parser, and implicit normalization engine | Complete |
| 13. Model rename | `meta model rename-model` | `Operation.RenameModel` with structured result output | Legacy model-refactor wrapper method and option/result records | Complete |
| 14. Relationship conversion refactors | `meta model refactor property-to-relationship`, `meta model refactor relationship-to-property` | `Operation.PropertyToRelationship` and `Operation.RelationshipToProperty` | Legacy model-refactor wrapper methods and option/result records | Complete |
| 15. Relationship rename | `meta model rename-relationship` | `Operation.RenameRelationship` with structured result output | Legacy model-refactor wrapper method and option/result records | Complete |
| 16. Entity and record rename | `meta model rename-entity`, `meta instance rename-id` | `Operation.RenameEntity` and `Operation.RenameRecord`; XML layout/config effects applied after semantic success | Remaining model/instance refactor wrapper methods and option/result records | Complete |
| 17. Refactor service removal | MetaWeave materialization | One atomic batch of `Operation.PropertyToRelationship` values | `ModelRefactorService`, `InstanceRefactorService`, registrations, contracts, and service tests | Complete |
| 18. Common read commands | `meta list entities/properties/relationships`, `meta view entity/instance`, `meta query`, `meta instance relationship list` | `IMetaWorkspaceSource` entity/property/relationship/count/record/query reads | Command-local model traversal and query filtering/limiting | Complete |
| 19. Validation adapter removal | Workspace persistence, CLI commands, MetaWeave, MetaSql, and tests | `WorkspaceValidator.Validate` | `IValidationService`, `ValidationService`, composition-root registration, constructor injection, and service-named tests | Complete |
| 20. Existing-workspace CSV import | `meta import csv ... --workspace` | `IImportService` plans `AddEntity`, `AddProperty`, `InsertRecord`, property mutation, and relationship mutation operations; the CLI executes one atomic batch | CLI-owned direct model/instance merge and preflight mutation | Complete |
| 21. Semantic workspace merge | `meta workspace merge` and MetaWeave materialization | `WorkspaceComposition.MergeAsync` composes `IMetaWorkspaceSource` reads and ordinary operations | Direct semantic model/instance cloning in `WorkspaceMergeService` | Complete |
| 22. Instance diff merge | `meta instance merge` and `meta instance merge-aligned` | `WorkspaceSynchronization.PlanInstanceChanges` emits a referentially valid sequence of primitive instance operations; `InstanceDiffService` verifies the modeled right snapshot without mutating the target | Direct collection replacement and command-specific snapshot/restore/save flows | Complete |
| 23. Generation and CSV export inputs | `meta generate sql/csharp` and `meta export csv` | SQL and C# generation consume `InMemoryWorkspace`; CSV export consumes `IMetaWorkspaceSource`; tooling generation receives its optional source path explicitly | Generation `Workspace` overloads and CSV export object-graph traversal | Complete |
| 24. Representation import and new XML output | `meta import sql`, `meta import csv`, and creation of imported XML workspaces | SQL/CSV import returns `InMemoryWorkspace`; CSV merge planning accepts semantic states; new XML output accepts semantic state and lets the XML adapter choose its default layout | Fabricated temporary workspace paths/configuration on imported data and public `Workspace` import/export signatures | Complete |
| 25. Model suggestion analysis | `meta model suggest` | Analysis consumes `InMemoryWorkspace`; the CLI supplies its workspace path only when rendering suggested commands | `Workspace` analysis input and `WorkspaceRootPath` carried in the service report | Complete |
| 26. Instance diff artifact construction | `meta instance diff`, `diff-aligned`, `merge`, and `merge-aligned` | Diff construction and merge planning consume `InMemoryWorkspace`; modeled diff results are semantic workspaces persisted by the CLI through XML export | Diff-service paths, workspace configuration, physical `Workspace` inputs/results, embedded workspace configuration, and duplicate service-side XML model-file/path helpers | Complete |
| 27. Physical XML workspace writing | New XML workspace export and existing XML workspace save | Shared `XmlWorkspaceWriter` writes semantic state with either default or preserved XML configuration and shard layout | Private legacy `Workspace` construction in `ExportService` and the duplicate physical writer in `WorkspaceService` | Complete |
| 28. XML model-mutation lifecycle | Every public `meta model` mutation command | `XmlWorkspaceReader` opens `OpenedXmlWorkspace`; `InMemoryOperations` produces a candidate; `XmlWorkspaceWriter` validates the baseline fingerprint and publishes the candidate with XML layout effects | `WorkspaceService`, mutable `Workspace`, snapshot rollback, and duplicate command reloads from the model-mutation execution path; XML parsing removed from `WorkspaceService` | Complete |
| 29. XML instance-mutation lifecycle | Direct instance authoring, bulk insert, existing-workspace CSV import, and equal/aligned instance merge | The target opens once as `OpenedXmlWorkspace`; planners read its `InMemoryWorkspace`; one operation batch produces and publishes a validated candidate with stale-write detection | Legacy loaded-workspace executor, mutable target mutation, command rollback snapshots, `WorkspaceSnapshot`, and legacy save calls from every existing-workspace mutation | Complete |
| 30. XML read and creation lifecycle | Status, list/view/query, graph diagnostics, relationship listing, validation, suggestion, generation/export, diff inputs, error hints, and `meta init` | Existing XML inputs open through `XmlWorkspaceReader`; common reads use `IMetaWorkspaceSource`; new workspaces write semantic state through `XmlWorkspaceWriter` | General CLI `LoadWorkspaceForCommandAsync`, read-side `Workspace` wrappers, hidden parent-directory hint discovery, diff-input `WorkspaceService` loads, and init-time legacy workspace construction/save | Complete |
| 31. XML workspace-merge lifecycle | `meta workspace merge` | `WorkspaceMergeService` composes semantic sources; strict validation precedes publication; `XmlWorkspaceWriter` privately reconciles compatible configuration and source shard assignments | Final CLI `WorkspaceService` loads/save, target `Workspace` construction, and XML layout ownership in the command | Complete |
| 32. MetaWeave workspace lifecycle | MetaWeave check, suggestion, authoring reference validation, and materialization | Exact XML references open through `XmlWorkspaceReader`; read-only analysis consumes `InMemoryWorkspace`; materialization semantically merges sources, applies one atomic operation batch, and publishes through `XmlWorkspaceWriter` | `WorkspaceService` injection/loading, mutable `Workspace` materialization results, CLI-owned save, source mutation during read, and legacy merge overload | Complete |
| 33. MetaDocs generic workspace import | MetaDocs model and selected-instance import | Exact XML sources open through `XmlWorkspaceReader`; import and semantic source fingerprints consume `InMemoryWorkspace`; physical source location comes only from the opened XML value | Private `WorkspaceService` loaders, legacy `Workspace` fingerprint input, and unused `MetaDocsWorkspaceFactory` | Complete |
| 34. XML compatibility-shell removal | Foundation XML tests and the checked-in Enterprise BI C# sample | Tests exercise `XmlWorkspaceReader`/`XmlWorkspaceWriter` directly; the sample is regenerated from its sanctioned workspace with the current C# emitter | `Workspace`, `IWorkspaceService`, `WorkspaceService`, composition registration, test conversion helper, and wrapper-only tests | Complete |
| 35. Downstream compatibility-shell removal | MetaSchema, MetaConvert, MetaSql, MetaDataVault tests, and MetaTransform conversion tests | Workspace producers return `InMemoryWorkspace`; XML consumers open through `XmlWorkspaceReader`; persisted output goes through the owning typed writer or `XmlWorkspaceWriter` | All downstream references to `Workspace`, `IWorkspaceService`, and `WorkspaceService` in `meta-bi` | Complete |

## Current Boundary

Every public command that mutates an existing XML workspace now uses the opened XML lifecycle. The CLI opens one semantic baseline, plans ordinary operations without changing that baseline, validates the candidate, and publishes through the XML writer after a fingerprint comparison under the write lock. Validation presentation and success output remain CLI concerns; rollback snapshots and the legacy loaded-workspace executor are gone.

Bulk insert remains a higher-level CLI planning algorithm, but its result is now only a composition of primitive operations. Optional references to rows created later in the same input are deferred until those rows exist. No batch operation was added to the semantic language.

Entity and record renames can affect XML-owned shard addresses and `workspace.meta` storage names. `XmlWorkspaceOperationEffects` consumes semantic operation results after successful execution and updates those XML details before save. The operation language does not know about XML layout.

MetaWeave remains a higher-level algorithm. It validates its authored bindings, constructs an ordered batch of property-to-relationship operations, and accepts the merged model and instance only when the complete batch succeeds.

The migrated read commands open XML through `XmlWorkspaceReader` and adapt the resulting semantic state through `InMemoryWorkspaceSource`. Their command behavior no longer depends on a legacy `Workspace` wrapper, and query filtering, ordering, counting, and limiting are expressed through the common source contract. Opening SQL or C# sources directly is a later CLI representation-selection change.

CSV parsing remains an adapter concern. Importing CSV into an existing workspace now produces a semantic operation plan without changing the target; the CLI applies that plan through the same atomic operation path as other public mutations. Creating a new workspace from CSV remains representation ingestion rather than mutation of an existing workspace.

Semantic workspace merge now reads each source through `IMetaWorkspaceSource` and constructs the merged state through ordinary model and instance operations. The CLI validates that semantic candidate before persistence. `XmlWorkspaceWriter` then reconciles compatible XML configuration and source shard assignments through its private merge-layout helper. Per-record shard assignments are not carried by semantic records, sources, results, or operations.

Instance diff workspaces remain modeled high-level artifacts owned by `Meta.Core`. Diff construction now consumes and returns only `InMemoryWorkspace`; it does not receive source paths, create XML configuration, or decide artifact directories. The equal-diff byte identity preflight remains at the XML CLI edge. The CLI selects the conventional output path and persists the semantic diff through XML export. Applying an equal or aligned diff derives the desired in-memory state, asks `WorkspaceSynchronization` for primitive instance operations, executes that plan against a clone, and verifies the exact modeled right snapshot before returning it. The synchronization planner preserves validity after every operation: required targets are inserted first, retained references are redirected before deletion, optional references on removed rows are cleared, and required dependents are deleted before their targets. The CLI uses its common atomic operation executor; the old direct row replacement and command-local rollback/save path are gone.

SQL and C# generation no longer receive an XML `Workspace`; they receive only `InMemoryWorkspace`. Tooling C# generation receives the source workspace path as an explicit optional string because that path is emitted in generated comments and commands, not because generation owns workspace persistence. CSV export now reads entity structure and an ordinal-identity-ordered record stream through `IMetaWorkspaceSource`, writing each row directly instead of materializing the source. SQL supplies that order in the database, so large SQL-backed exports stay database-native and bounded-memory. Saving an already-open XML workspace remains separate because that operation preserves workspace configuration and shard layout.

SQL and CSV imports now produce only `InMemoryWorkspace`; they no longer invent temporary directories, XML configuration, dirty flags, or other persistence identity. CSV merge planning also accepts semantic source and target states. Creating a new XML workspace from an imported state passes that state to `XmlWorkspaceWriter`, which selects the standard XML configuration and layout. Saving an already-open XML workspace remains a different operation and preserves that workspace's existing configuration and shard assignments through the same writer. `MetaXmlCodec` remains the direct semantic model-and-instance XML document conversion; `XmlWorkspaceWriter` owns filesystem configuration, shard planning, locking, staging, and rollback.

The mutable `Workspace` object and `WorkspaceService` compatibility shell are gone from both `meta` and `meta-bi`. XML callers open an exact `OpenedXmlWorkspace`, operate on semantic state, and publish through `XmlWorkspaceWriter`; new XML workspaces are written from `InMemoryWorkspace` directly.

The XML lifecycle now has an explicit opened representation value. `XmlWorkspaceReader` resolves and reads XML configuration, model, shards, layout, and a canonical baseline fingerprint into `OpenedXmlWorkspace`. Model mutation commands execute against its semantic state without changing the opened baseline, validate the candidate, and ask `XmlWorkspaceWriter` to compare the current directory with the baseline under the write lock before publishing. A rejected operation, strict-warning failure, staging failure, or concurrent edit therefore requires no command-level snapshot restoration. Relationship-column recovery options remain attached to the opened value so the conflict re-read uses the same interpretation as the original load.

All public model and instance mutation commands use that lifecycle. The instance family includes single-row insert/update/delete, relationship mutation, record rename, bulk insert, existing-workspace CSV import, and equal/aligned diff merge. Their planners consume `InMemoryWorkspace`; XML configuration and shard layout remain private to the opened target. Model suggestion, ordinary reads, graph diagnostics, generation/export, validation, status, diff inputs, initialization, and workspace merge now use the reader/writer boundary without the compatibility shell.

Model suggestion analysis now consumes only `InMemoryWorkspace`. Its report contains analysis results, not an XML workspace path. The CLI retains the loaded XML path and supplies it when the user asks to print executable refactor commands. This keeps command rendering in the CLI without changing suggestion classification or output.

Graph diagnostics operate only on a valid workspace. `XmlWorkspaceReader` validates the semantic state before returning it, and graph analysis validates direct in-memory callers as well. Graph statistics describe valid topology; malformed declarations are rejected at the representation boundary instead of being presented as graph properties.

The `meta` CLI no longer consumes an XML compatibility shell. Semantic workspace merge remains `WorkspaceComposition`; XML compatibility, entity-storage rules, shard-layout reconciliation, and publication belong to the XML writer boundary. Command-level snapshot rollback has also been removed.

Post-removal verification passed the foundation build, focused XML and operation suites, MetaWeave, MetaDocs, MetaCli, MetaMesh, the external `meta` CLI suite, all ten standard downstream CLI builds, and the affected MetaSchema, MetaConvert, MetaSql, MetaDataVault, and MetaTransform build/test slices. The complete MetaSql project passes 107/107 in approximately four minutes and fifteen seconds; earlier harness runs timed out before this integration-heavy suite completed. MetaConvert forced rebuilds now pass with zero warnings after its Business Data Vault projection was aligned with modeled optional implementation columns.

`GenerationService` owns deliberate output artifacts generated from semantic state. It does not own workspace persistence and is not a legacy workspace engine.

The removed mutation engine and implicit normalizer must not be reintroduced as compatibility paths. Representation repair belongs to the owning adapter; semantic producers must return valid state.
