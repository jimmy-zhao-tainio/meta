# C# Tooling Services API

This page documents the supported C# service surface for building tooling on top of `meta` without going through the CLI command parser.

Scope:
- `Meta.Core.Services` contracts and core implementations
- `Meta.Adapters` composition and import/export adapters
- deterministic usage patterns for load -> validate -> mutate -> save

## Assembly map

- `Meta.Core`:
  - domain types (`Workspace`, `GenericModel`, `GenericInstance`, `GenericEntity`, `GenericRecord`)
  - core service contracts and implementations in `Meta.Core.Services`
- `Meta.Adapters`:
  - `ServiceCollection` composition root
  - `ImportService` / `ExportService` adapter implementations

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
- `IOperationService OperationService`
- `IModelRefactorService ModelRefactorService`
- `IInstanceRefactorService InstanceRefactorService`
- `IWorkspaceMergeService WorkspaceMergeService`

Use this when you want a single default object graph for tooling code.

## Core service contracts (`Meta.Core.Services`)

### `IWorkspaceService`

```csharp
Task<Workspace> LoadAsync(string workspaceRootPath, bool searchUpward = true, CancellationToken cancellationToken = default);
Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default);
Task SaveAsync(Workspace workspace, string? expectedFingerprint, CancellationToken cancellationToken = default);
string CalculateHash(Workspace workspace);
```

Behavior:
- `LoadAsync` resolves a workspace from the provided path (upward search by default).
- `SaveAsync` is validation-gated and atomic at workspace level.
- `SaveAsync(... expectedFingerprint ...)` enforces optimistic concurrency.
- `CalculateHash` returns deterministic workspace content hash.

### `IValidationService`

```csharp
WorkspaceDiagnostics Validate(Workspace workspace);
WorkspaceDiagnostics ValidateIncremental(Workspace workspace, IReadOnlyCollection<string> touchedEntities);
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

### `IOperationService`

```csharp
void Execute(Workspace workspace, WorkspaceOp operation);
bool CanUndo(Workspace workspace);
bool CanRedo(Workspace workspace);
void Undo(Workspace workspace);
void Redo(Workspace workspace);
void ApplyWithoutHistory(Workspace workspace, WorkspaceOp operation);
IReadOnlyCollection<WorkspaceOp> GetUndoOperations(Workspace workspace);
```

Behavior:
- operation execution with in-memory undo/redo history per workspace instance
- no persistence by itself (call `IWorkspaceService.SaveAsync` explicitly)

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
Task ExportSqlAsync(Workspace workspace, string schemaOutputPath, string dataOutputPath, CancellationToken cancellationToken = default);
Task ExportCSharpAsync(Workspace workspace, string outputPath, CancellationToken cancellationToken = default);
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
GenerationManifest GenerateSsdt(Workspace workspace, string outputDirectory);
GenerationManifest BuildManifest(string rootDirectory);
bool AreEquivalent(GenerationManifest left, GenerationManifest right, out string message);
```

`GenerateCSharp(... includeTooling: true)` emits optional `<ModelName>.Tooling.cs` helper surface.

### `GraphStatsService` (static)

```csharp
GraphStatsReport Compute(GenericModel model, int topN = 10, int cycleSampleLimit = 10);
```

Model-level graph diagnostics (in/out degree, SCC/cycle, roots/sinks, component counts).

### `NormalizationService` (static)

```csharp
IReadOnlyList<WorkspaceOp> BuildNormalizeOperations(Workspace workspace, NormalizeOptions? options = null);
```

Generates deterministic cleanup operations; execute with `IOperationService`.

## Error model

Typical failures throw `InvalidOperationException` with explicit precondition messages.

Optimistic save mismatch throws `WorkspaceConflictException`:

- `ExpectedFingerprint`
- `ActualFingerprint`

## Recommended tooling workflow

```csharp
using Meta.Adapters;
using Meta.Core.Services;

var services = new ServiceCollection();

var workspace = await services.WorkspaceService.LoadAsync(@".\Workspace");
var beforeHash = services.WorkspaceService.CalculateHash(workspace);

var diagnostics = services.ValidationService.Validate(workspace);
if (diagnostics.HasErrors)
{
    throw new InvalidOperationException("Fix validation errors before mutation.");
}

// Example: model refactor
var refactorResult = services.ModelRefactorService.RefactorPropertyToRelationship(
    workspace,
    new PropertyToRelationshipRefactorOptions(
        SourceEntityName: "Order",
        SourcePropertyName: "WarehouseId",
        TargetEntityName: "Warehouse",
        LookupPropertyName: "Id",
        Role: "",
        DropSourceProperty: true,
        RequireSourceReuse: true));

// Validate post-change
var postDiagnostics = services.ValidationService.Validate(workspace);
if (postDiagnostics.HasErrors)
{
    throw new InvalidOperationException("Refactor introduced validation errors.");
}

// Persist with optimistic concurrency
await services.WorkspaceService.SaveAsync(workspace, expectedFingerprint: beforeHash);
```

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
| `meta model add-entity/add-property/add-relationship/drop-*`, `meta instance update`, `meta instance relationship set`, `meta delete`, `meta insert`, `meta bulk-insert` | `OperationService.Execute(...)` over `WorkspaceOp` + `NormalizationService.BuildNormalizeOperations(...)` + `ValidationService.Validate(...)` + `WorkspaceService.SaveAsync(...)` |
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
| `meta instance diff/merge` and aligned variants | currently implemented via CLI diff support and workspace services; no dedicated public `IInstanceDiffService` contract yet |

Practical rule for non-CLI tooling:
- use `ServiceCollection` and call services directly when the service contract exists
- for diff/merge internals, either invoke CLI or mirror CLI support code until a dedicated diff/merge service contract is introduced

## MetaWeave Services

### `MetaWeaveSuggestService`

```csharp
Task<WeaveSuggestResult> SuggestAsync(Workspace weaveWorkspace, CancellationToken cancellationToken = default);
```

Example CLI output from the sanctioned weak role weave workspace:

```text
OK: weave suggest
Workspace: C:\Users\jimmy\Desktop\meta\MetaWeave.Workspaces\Weave-Suggest-WeakRoleReferenceType
Suggestions: 0
WeakSuggestions: 1

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

## MetaFabric Services

### `MetaFabricAuthoringService`

```csharp
Task AddWeaveReferenceAsync(Workspace fabricWorkspace, string alias, string workspacePath, CancellationToken cancellationToken = default);
Task AddBindingReferenceAsync(
    Workspace fabricWorkspace,
    string name,
    string weaveAlias,
    string sourceEntity,
    string sourceProperty,
    string targetEntity,
    string targetProperty,
    CancellationToken cancellationToken = default);
Task AddScopeRequirementAsync(
    Workspace fabricWorkspace,
    string bindingReferenceName,
    string parentBindingReferenceName,
    string sourceParentReferenceName,
    string targetParentReferenceName,
    CancellationToken cancellationToken = default);
```

### `MetaFabricSuggestService`

```csharp
Task<FabricSuggestResult> SuggestAsync(Workspace fabricWorkspace, CancellationToken cancellationToken = default);
```

Example CLI output from the sanctioned unscoped fabric workspace:

```text
OK: fabric suggest
Workspace: C:\Users\jimmy\Desktop\meta\MetaFabric.Workspaces\Fabric-Suggest-Scoped-Group-CategoryItem
Suggestions: 1
WeakSuggestions: 0

Scope suggestions
  1) ChildItem -> ParentGroup (source parent: GroupId, target parent: CategoryId)

Commands
  meta-fabric add-scope --workspace "C:\Users\jimmy\Desktop\meta\MetaFabric.Workspaces\Fabric-Suggest-Scoped-Group-CategoryItem" --binding ChildItem --parent-binding ParentGroup --source-parent-reference GroupId --target-parent-reference CategoryId

Weak scope suggestions
  (none)
```

### `MetaFabricService`

```csharp
Task<FabricCheckResult> CheckAsync(Workspace fabricWorkspace, CancellationToken cancellationToken = default);
```

Example CLI output from the sanctioned scoped fabric workspace:

```text
OK: fabric check
Workspace: C:\Users\jimmy\Desktop\meta\MetaFabric.Workspaces\Fabric-Scoped-Group-CategoryItem
Weaves: 2
Bindings: 2
ResolvedRows: 5
Errors: 0
```

### Additional CLI to Services API mappings

| CLI command family | Primary C# API path |
|---|---|
| `meta-fabric init` | `MetaFabricWorkspaces.CreateEmptyMetaFabricWorkspace(...)` + `WorkspaceService.SaveAsync(...)` |
| `meta-fabric add-weave` | `MetaFabricAuthoringService.AddWeaveReferenceAsync(...)` |
| `meta-fabric add-binding` | `MetaFabricAuthoringService.AddBindingReferenceAsync(...)` |
| `meta-fabric add-scope` | `MetaFabricAuthoringService.AddScopeRequirementAsync(...)` |
| `meta-fabric suggest` | `MetaFabricSuggestService.SuggestAsync(...)` |
| `meta-fabric check` | `MetaFabricService.CheckAsync(...)` |

## ModelSuggestService Example

Example CLI output from the sanctioned Suggest demo workspace:

```text
OK: model suggest
Workspace: C:\Users\jimmy\Desktop\meta\Samples\Demos\SuggestDemo\Workspace
Model: ProductModel
Suggestions: 3
WeakSuggestions: 0

Relationship suggestions
  1) Order.ProductId -> Product (lookup: Product.Id)
  2) Order.SupplierId -> Supplier (lookup: Supplier.Id)
  3) Order.WarehouseId -> Warehouse (lookup: Warehouse.Id)

Weak relationship suggestions
  (none)
```

Role-style weak example:

```text
OK: model suggest
Workspace: C:\Users\jimmy\AppData\Local\Temp\meta-weak-role-doc
Model: WeakSuggestModel
Suggestions: 0
WeakSuggestions: 1

Relationship suggestions
  (none)

Weak relationship suggestions
  1) Order.SourceProductId -> Product (lookup: Product.Id, role: SourceProduct)
```

Ambiguous weak example:

```text
OK: model suggest
Workspace: C:\Users\jimmy\AppData\Local\Temp\meta-weak-ambiguous-doc
Model: AmbiguousSuggestModel
Suggestions: 0
WeakSuggestions: 2

Relationship suggestions
  (none)

Weak relationship suggestions
  1) Mapping.ReferenceTypeId -> ReferenceType (lookup: ReferenceType.Id)
  2) Mapping.ReferenceTypeId -> Type (lookup: Type.Id, role: ReferenceType)
```

