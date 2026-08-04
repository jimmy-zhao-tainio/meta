# C# Services API

This page describes the supported C# surfaces for operating on Meta workspaces
without invoking a CLI.

## Assemblies

- `Meta.Operations` owns representation-neutral model and instance state,
  reads, constraints, validation, and operations.
- `Meta.Core` owns XML workspace serialization and higher-level algorithms
  composed from the operation language.
- `Meta.Adapters` owns SQL and C# representation adapters plus import and
  export services.

## XML Workspaces

`XmlWorkspaceReader` opens one exact XML workspace path. It returns an
`OpenedXmlWorkspace`, which carries the semantic state, the physical XML
layout, and the baseline fingerprint needed for safe publication.

```csharp
using Meta.Core.Operations;
using Meta.Core.Serialization;

var opened = await XmlWorkspaceReader.OpenAsync(@".\Workspace");

var execution = InMemoryOperations.Execute(
    opened.State,
    new Operation.AddEntity("Order"),
    new Operation.AddProperty(
        "Order",
        "OrderNumber",
        IsRequired: true));

await XmlWorkspaceWriter.WriteAsync(
    opened,
    execution.Workspace,
    execution.Results);
```

`XmlWorkspaceWriter.WriteAsync` validates the candidate, checks that the
workspace has not changed since it was opened, preserves XML configuration and
shard layout, applies XML-specific rename effects, and publishes atomically.

Use `XmlWorkspaceWriter.WriteNewAsync` when creating a new XML workspace from
semantic state. Use `WriteMergedAsync` when publishing a semantic merge while
preserving compatible source layouts.

XML loading never searches parent directories.

## Semantic Operations

`InMemoryWorkspace` contains only a `GenericModel` and `GenericInstance`.
`InMemoryOperations.Execute` applies a batch atomically and returns a separate
candidate plus structured results. Rejected batches do not alter the input.

```csharp
var execution = InMemoryOperations.Execute(
    workspace,
    new Operation.InsertRecord(
        "Order",
        "order-1",
        new Dictionary<string, string>
        {
            ["OrderNumber"] = "SO-001",
        }));
```

Operation construction enforces the common Meta name and identity language.
Execution enforces model and referential integrity independently of the storage
surface.

## Reads

`IMetaWorkspaceSource` is the common read contract. An
`InMemoryWorkspaceSource` adapts in-memory state. `SqlWorkspaceSource`
implements the same reads directly in SQL so bounded queries and record streams
do not require loading all instance rows.

The source contract includes model name, entity names, properties,
relationships, record streams, record lookup, counts, and bounded typed
queries.

## SQL Workspaces

`SqlWorkspaceSource` reads a SQL-backed Meta workspace under one coherent
transaction. `SqlOperations` applies the common operation language directly
through SQL DDL and DML; ordinary SQL reads and mutations do not materialize
instance tables in memory.

Full conversion to XML or C# intentionally materializes semantic state by
composing the common reads.

## Import And Export

`Meta.Adapters.ServiceCollection` provides:

- `IImportService ImportService`
- `IExportService ExportService`
- `IInstanceDiffService InstanceDiffService`
- `IWorkspaceMergeService WorkspaceMergeService`
- `SqlServerDeploymentService SqlServerDeploymentService`

`IImportService` returns `InMemoryWorkspace` for SQL and CSV imports.
Existing-workspace CSV import returns an operation plan rather than mutating its
target.

`IExportService.ExportXmlAsync` writes an `InMemoryWorkspace` as a new XML
workspace. `ExportCsvAsync` streams one entity from an
`IMetaWorkspaceSource`.

## Higher-Level Algorithms

`IWorkspaceMergeService.MergeAsync` reads semantic sources and returns a
`WorkspaceMergePlan` containing merged state and counts.

`IInstanceDiffService` builds equal or aligned diff workspaces from
`InMemoryWorkspace` values. Its merge methods return ordinary operation plans
for the caller to execute and publish.

`ModelSuggestService.Analyze` consumes `InMemoryWorkspace`.
`GenerationService.GenerateSql`, `GenerateCSharp`, and `GenerateSsdt`
also consume semantic state; output-directory ownership belongs to those
artifact generators.

## Failure Model

- Invalid semantic operations throw `MetaOperationException`.
- Validation returns structured `WorkspaceDiagnostics`.
- Publishing a stale opened XML workspace throws `WorkspaceConflictException`
  with expected and actual fingerprints.
- Representation adapters reject structures they cannot carry without
  semantic loss.

The old mutable `Workspace` object and `WorkspaceService` compatibility
shell no longer exist. Callers choose a representation adapter explicitly and
keep semantic work separate from persistence.
