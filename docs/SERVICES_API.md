# C# Tooling Services API

This page documents the supported C# service surface for building tooling on top of `meta` without going through the CLI command parser.

Scope:
- `Meta.Core.Services` contracts and core implementations
- `Meta.Adapters` composition and import/export adapters
- deterministic usage patterns for load -> validate -> mutate -> save

## Assembly map

- `Meta.Core`:
  - domain types (`Workspace`, `GenericModel`, `GenericInstance`, `GenericEntity`, `GenericRecord`)
  - concrete operation plans and generic/XML interpreters in `Meta.Core.Operations`
  - core service contracts and implementations in `Meta.Core.Services`
- `Meta.Adapters`:
  - `ServiceCollection` composition root
  - `ImportService` / `ExportService` adapter implementations
  - SQL Server and Roslyn-backed C# operation sessions

## Quick start

```csharp
using Meta.Adapters;
using Meta.Core.Services;

var services = new ServiceCollection();
var workspace = await services.WorkspaceService.LoadAsync(@".\Workspace");
var diagnostics = services.ValidationService.Validate(workspace);
if (diagnostics.HasErrors)
{
    throw new InvalidOperationException("Workspace has validation errors.");
}
```

## Composition root (`Meta.Adapters`)

`Meta.Adapters.ServiceCollection` wires the default concrete services:

- `IWorkspaceService WorkspaceService`
- `IValidationService ValidationService`
- `IImportService ImportService`
- `IExportService ExportService`
- `IModelRefactorService ModelRefactorService`
- `IInstanceRefactorService InstanceRefactorService`
- `IInstanceDiffService InstanceDiffService`
- `IWorkspaceMergeService WorkspaceMergeService`

Use this when you want a single default object graph for tooling code.

## Core service contracts (`Meta.Core.Services`)

### `IWorkspaceService`

```csharp
Task<Workspace> LoadAsync(string workspaceRootPath, bool searchUpward = false, CancellationToken cancellationToken = default);
Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default);
Task SaveAsync(Workspace workspace, string? expectedFingerprint, CancellationToken cancellationToken = default);
string CalculateHash(Workspace workspace);
```

Behavior:
- `LoadAsync` loads the exact workspace path by default. Upward search is available only when the caller requests it explicitly.
- `SaveAsync` is validation-gated and atomic at workspace level.
- `SaveAsync(... expectedFingerprint ...)` enforces optimistic concurrency.
- `CalculateHash` returns deterministic workspace content hash.

### `IValidationService`

```csharp
WorkspaceDiagnostics Validate(Workspace workspace);
```

Behavior:
- validates model + instance invariants
- returns errors/warnings as diagnostics (no mutation)

### `IWorkspaceMergeService`

```csharp
WorkspaceMergeResult MergeInto(
    Workspace targetWorkspace,
    IReadOnlyList<Workspace> sourceWorkspaces,
    WorkspaceMergeOptions options);
```

`WorkspaceMergeOptions`:
- `MergedModelName`

Behavior:
- merges full model + instance from multiple workspaces into a target workspace object
- fail-only on collisions/incompatible config

## Operation contract (`Meta.Core.Operations`)

```csharp
var plan = MetaOperationPlan.Create(
    new InsertRecordOperation(
        "Cube",
        "sales",
        new Dictionary<string, string>
        {
            ["Name"] = "Sales",
        }));

var session = await XmlMetaOperationSession.OpenExistingAsync(@".\Workspace");
session.Apply(plan);
await session.CommitAsync();
```

`MetaOperationPlan` is an ordered, atomic program of concrete operations.
The operation families are:

- model operations: `AddEntityOperation`, `RemoveEntityOperation`
- model-and-instance refactors: add, remove, rename, or change the requiredness of a property; add or remove a relationship
- instance operations: insert/delete a record, set/clear a property, and set/clear a relationship

The generic reference interpreter validates the source and resulting state.
`MetaOperationException` identifies the failing operation and carries structured
workspace diagnostics when conformance rejects the result.

Execution surfaces:

- `InMemoryMetaOperationSession`: copy-and-publish reference state
- `XmlMetaOperationSession`: exact-path XML load, explicit commit/discard, and stale-write rejection
- `SqlServerMetaOperationSession`: serializable SQL transaction with a savepoint per plan
- `CSharpMetaOperationSession`: owned C# source directory, Roslyn
  decode/compile, staged canonical publication, and stale-write rejection

The XML, SQL Server, and C# source sessions support the complete current
fourteen-operation vocabulary. The same conformance plans are compared with the
normative in-memory interpreter.

The SQL Server session opens only an encoded Meta SQL workspace. Entity
identities and relationship values are `nvarchar(128)` under the explicit
Meta identity collation and are constrained to non-empty printable ASCII
without leading or trailing spaces. This bounded repertoire makes SQL
case-insensitive equality agree with Meta's `OrdinalIgnoreCase` identity
semantics. Identity checks and foreign keys must be enabled and trusted; the
session verifies those schema guarantees without materializing the tables.
Generic SQL import remains the permissive route for ordinary databases.

### C# source workspace

```csharp
using Meta.Adapters;
using Meta.Core.Operations;

var state = new CSharpMetaWorkspaceReader()
    .Read(@".\Metadata.CSharp");

var session = CSharpMetaOperationSession.OpenExisting(
    @".\Metadata.CSharp");
session.Apply(MetaOperationPlan.Create(
    new SetPropertyOperation(
        "Cube",
        "sales",
        "Name",
        "Sales and margin")));
session.Commit();
```

Create a new owned source workspace with:

```csharp
var session = CSharpMetaOperationSession.Create(
    @".\Metadata.CSharp",
    initialState);
```

The accepted C# form consists of the generated model root, sealed entity
classes, automatic string and object-reference properties, entity collection
initializers, and statically resolvable relationship assignments. Roslyn
compiles the source and supplies symbols, nullable annotations, constant
semantics, and operation trees. Workspace code is never executed.

Commit regenerates a canonical marked source directory, compiles and decodes
the staged output, compares it with the pending abstract state, checks the
baseline directory fingerprint, and then publishes it. The session owns the
directory: arbitrary project files, custom methods or accessors, and unrelated
source are not preserved.

### `IModelRefactorService`

```csharp
RenameModelRefactorResult RenameModel(Workspace workspace, RenameModelRefactorOptions options);
RenameEntityRefactorResult RenameEntity(Workspace workspace, RenameEntityRefactorOptions options);
RenameRelationshipRefactorResult RenameRelationship(Workspace workspace, RenameRelationshipRefactorOptions options);
PropertyToRelationshipRefactorResult RefactorPropertyToRelationship(Workspace workspace, PropertyToRelationshipRefactorOptions options);
RelationshipToPropertyRefactorResult RefactorRelationshipToProperty(Workspace workspace, RelationshipToPropertyRefactorOptions options);
```

Option records:
- `RenameModelRefactorOptions(OldModelName, NewModelName)`
- `RenameEntityRefactorOptions(OldEntityName, NewEntityName)`
- `RenameRelationshipRefactorOptions(SourceEntityName, TargetEntityName, CurrentRole, NewRole)`
- `PropertyToRelationshipRefactorOptions(SourceEntityName, SourcePropertyName, TargetEntityName, LookupPropertyName, Role, DropSourceProperty, RequireSourceReuse = true)`
- `RelationshipToPropertyRefactorOptions(SourceEntityName, TargetEntityName, Role, PropertyName)`

Behavior:
- atomic in-memory refactor operations
- fail-only on precondition collisions/invalid state
- marks workspace dirty; caller persists explicitly

### `IInstanceRefactorService`

```csharp
RenameInstanceIdRefactorResult RenameInstanceId(
    Workspace workspace,
    RenameInstanceIdRefactorOptions options);
```

`RenameInstanceIdRefactorOptions(EntityName, OldId, NewId)`

Behavior:
- renames row Id and rewrites inbound relationship usages referencing that Id
- fail-only on collisions/missing entity or row

### `IInstanceDiffService`

```csharp
InstanceDiffBuildResult BuildEqualDiffWorkspace(
    Workspace leftWorkspace,
    Workspace rightWorkspace,
    string rightWorkspacePath);

InstanceDiffBuildResult BuildAlignedDiffWorkspace(
    Workspace leftWorkspace,
    Workspace rightWorkspace,
    Workspace alignmentWorkspace,
    string rightWorkspacePath);

void ApplyEqualDiffWorkspace(
    Workspace targetWorkspace,
    Workspace diffWorkspace);

void ApplyAlignedDiffWorkspace(
    Workspace targetWorkspace,
    Workspace diffWorkspace);
```

`InstanceDiffBuildResult`:

```csharp
InstanceDiffBuildResult(
    Workspace DiffWorkspace,
    string DiffWorkspacePath,
    bool HasDifferences,
    int LeftRowCount,
    int RightRowCount,
    int LeftPropertyCount,
    int RightPropertyCount,
    int LeftNotInRightCount,
    int RightNotInLeftCount);
```

Behavior:
- builds the sanctioned diff workspace used by `meta instance diff`
- supports both equal-model diff and aligned-model diff
- applies a sanctioned diff workspace back onto a target workspace for merge flows
- fails on diff/merge precondition mismatches with explicit `InvalidOperationException` messages

### `IImportService` (implemented in `Meta.Adapters`)

```csharp
Task<Workspace> ImportSqlAsync(string connectionString, string schema, CancellationToken cancellationToken = default);
Task<Workspace> ImportCsvAsync(string csvPath, string entityName, CancellationToken cancellationToken = default);
```

Behavior:
- returns in-memory workspace; caller chooses where/when to save
- CSV import is Id-first (`Id` column required, case-insensitive header match)

### `IExportService` (implemented in `Meta.Adapters`)

```csharp
Task ExportXmlAsync(Workspace workspace, string outputDirectory, CancellationToken cancellationToken = default);
Task ExportCsvAsync(Workspace workspace, string entityName, string outputPath, CancellationToken cancellationToken = default);
```

Behavior:
- filesystem export wrappers over `WorkspaceService` / `GenerationService`

## Static analysis/generation services

### `ModelSuggestService` (static)

```csharp
ModelSuggestReport Analyze(Workspace workspace);
LookupRelationshipSuggestion AnalyzeLookupRelationship(
    Workspace workspace,
    string sourceEntityName,
    string sourcePropertyName,
    string targetEntityName,
    string targetPropertyName,
    string? role = null,
    bool allowSourcePropertyReplacement = true,
    bool requireSourceReuse = true);
```

Use this for read-only structural suggestion analysis in tooling flows. Strong suggestions require one exact eligible target; weak suggestions capture role-style suffix matches and cases where one source property still matches more than one eligible target.

### `GenerationService` (static)

```csharp
GenerationManifest GenerateSql(Workspace workspace, string outputDirectory);
GenerationManifest GenerateCSharp(Workspace workspace, string outputDirectory, bool includeTooling = false);
GenerationManifest GenerateCSharpWorkspace(GenericModel model, GenericInstance instance, string outputDirectory);
GenerationManifest GenerateSsdt(Workspace workspace, string outputDirectory);
```

`GenerateCSharp(... includeTooling: true)` emits optional `<ModelName>.Tooling.cs` helper surface.
`GenerateCSharpWorkspace(...)` emits the bounded, marked C# form used by
`CSharpMetaOperationSession`.

### `GraphStatsService` (static)

```csharp
GraphStatsReport Compute(GenericModel model, int topN = 10, int cycleSampleLimit = 10);
```

Model-level graph diagnostics (in/out degree, SCC/cycle, roots/sinks, component counts).

## Error model

Typical failures throw `InvalidOperationException` with explicit precondition messages.

Operation failures throw `MetaOperationException` with:

- `OperationIndex`
- `Operation`
- `Diagnostics` when structural conformance rejected the source or result

Optimistic save mismatch throws `WorkspaceConflictException`:

- `ExpectedFingerprint`
- `ActualFingerprint`

## Recommended tooling workflow

```csharp
using Meta.Core.Operations;

var session = await XmlMetaOperationSession.OpenExistingAsync(@".\Workspace");
session.Apply(MetaOperationPlan.Create(
    new SetPropertyOperation(
        "Cube",
        "sales",
        "Name",
        "Sales and margin")));
await session.CommitAsync();
```

Use `IModelRefactorService` and `IInstanceRefactorService` for the larger
rename and property/relationship conversion refactors that have not yet moved
onto the shared operation contract.

## Notes for generated tooling users

- Generated consumer POCOs are dependency-free.
- Generated optional tooling file (`--tooling`) uses `Meta.Adapters.ServiceCollection` and these services under the hood.
- For custom tools, prefer calling services directly for explicit control over validation, refactor sequencing, and save boundaries.

## CLI to Services API mapping

This maps CLI surfaces to the primary C# service entrypoints used today.

| CLI command family | Primary C# API path |
|---|---|
| `meta init` | `WorkspaceService.SaveAsync(...)` with newly created `Workspace` object |
| `meta status` | `WorkspaceService.LoadAsync(...)` |
| `meta check` | `WorkspaceService.LoadAsync(...)` + `ValidationService.Validate(...)` |
| `meta list ...`, `meta view ...`, `meta query ...` | `WorkspaceService.LoadAsync(...)` then in-memory domain traversal |
| `meta model add-entity/add-property/add-relationship/drop-*`, `meta instance update`, `meta instance relationship set`, `meta delete`, `meta insert`, `meta bulk-insert` | `XmlMetaOperationSession` over one ordered `MetaOperationPlan` |
| `meta model rename-model/rename-entity/rename-relationship` | `ModelRefactorService` + `ValidationService.Validate(...)` + `WorkspaceService.SaveAsync(...)` |
| `meta model refactor property-to-relationship` | `ModelRefactorService.RefactorPropertyToRelationship(...)` + validate + save |
| `meta model refactor relationship-to-property` | `ModelRefactorService.RefactorRelationshipToProperty(...)` + validate + save |
| `meta instance rename-id` | `InstanceRefactorService.RenameInstanceId(...)` + validate + save |
| `meta model suggest` | `ModelSuggestService.Analyze(...)` |
| `meta graph stats` | `GraphStatsService.Compute(...)` |
| `meta workspace merge` | `WorkspaceMergeService.MergeInto(...)` + `ValidationService.Validate(...)` + `WorkspaceService.SaveAsync(...)` |
| `meta import sql` | `ImportService.ImportSqlAsync(...)` + validate + `ExportService.ExportXmlAsync(...)` |
| `meta import csv` | `ImportService.ImportCsvAsync(...)`; for existing workspace import path, CLI upserts into loaded workspace then validates and saves |
| `meta export csv` | `ExportService.ExportCsvAsync(...)` |
| `meta generate sql/csharp/ssdt` | `GenerationService.GenerateSql/GenerateCSharp/GenerateSsdt` |
| `meta instance diff/merge` and aligned variants | `InstanceDiffService.BuildEqualDiffWorkspace/BuildAlignedDiffWorkspace` and `ApplyEqualDiffWorkspace/ApplyAlignedDiffWorkspace` |

Practical rule for non-CLI tooling:
- use `ServiceCollection` and call services directly when the service contract exists
- diff/merge now has a dedicated `IInstanceDiffService`; tooling should use that instead of mirroring CLI support code

## MetaWeave Services

### `MetaWeaveSuggestService`

```csharp
Task<WeaveSuggestResult> SuggestAsync(Workspace weaveWorkspace, CancellationToken cancellationToken = default);
```

Example CLI output from the sanctioned weak role weave workspace:

```text
Ok

Binding suggestions
  (none)

Weak binding suggestions
  1) Source.Mapping.SourceReferenceTypeId -> Reference.ReferenceType.Id (role: SourceReferenceType)
```

### `MetaWeaveAuthoringService`

```csharp
Task AddModelReferenceAsync(Workspace weaveWorkspace, string alias, string modelName, string workspacePath, CancellationToken cancellationToken = default);
Task AddPropertyBindingAsync(
    Workspace weaveWorkspace,
    string name,
    string sourceModelAlias,
    string sourceEntity,
    string sourceProperty,
    string targetModelAlias,
    string targetEntity,
    string targetProperty,
    CancellationToken cancellationToken = default);
```

### `MetaWeaveService`

```csharp
Task<WeaveCheckResult> CheckAsync(Workspace weaveWorkspace, CancellationToken cancellationToken = default);
Task<Workspace> MaterializeAsync(Workspace weaveWorkspace, string materializedWorkspaceRootPath, string mergedModelName, CancellationToken cancellationToken = default);
```

### Additional CLI to Services API mappings

| CLI command family | Primary C# API path |
|---|---|
| `meta-weave add-model` | `MetaWeaveAuthoringService.AddModelReferenceAsync(...)` |
| `meta-weave add-binding` | `MetaWeaveAuthoringService.AddPropertyBindingAsync(...)` |
| `meta-weave suggest` | `MetaWeaveSuggestService.SuggestAsync(...)` |
| `meta-weave check` | `MetaWeaveService.CheckAsync(...)` |
| `meta-weave materialize` | `MetaWeaveService.MaterializeAsync(...)` |

## ModelSuggestService Example

Example CLI output from the sanctioned Suggest demo workspace:

```text
Ok

Relationship suggestions
  1) Order.ProductId -> Product (lookup: Product.Id)
  2) Order.SupplierId -> Supplier (lookup: Supplier.Id)
  3) Order.WarehouseId -> Warehouse (lookup: Warehouse.Id)

Weak relationship suggestions
  (none)
```

Role-style weak example:

```text
Ok

Relationship suggestions
  (none)

Weak relationship suggestions
  1) Order.SourceProductId -> Product (lookup: Product.Id, role: SourceProduct)
```

Ambiguous weak example:

```text
Ok

Relationship suggestions
  (none)

Weak relationship suggestions
  1) Mapping.ReferenceTypeId -> ReferenceType (lookup: ReferenceType.Id)
  2) Mapping.ReferenceTypeId -> Type (lookup: Type.Id, role: ReferenceType)
```





## SqlServerDeploymentService

Implemented in `Meta.Adapters`. Deploys generated `.sql` files to SQL Server in deterministic file-name order, supports `GO` batch separators, and can optionally create/use a target database before applying scripts.
