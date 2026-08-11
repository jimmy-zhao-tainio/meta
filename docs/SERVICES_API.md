# Meta Workspace Surfaces and Services

This document describes the foundation API for working with metadata
workspaces from C# code. A workspace is the product abstraction; XML, SQL, and
C# are supported surfaces for the same model and instance structure.

## Assembly Boundaries

- `Meta.Operations` owns representation-neutral state, reads, constraints,
  validation, and operations.
- `Meta.Core` owns shared domain algorithms and foundation services that are
  independent of one workspace surface.
- `Meta.TypedModels` owns representation-neutral mapping between typed CLR
  models and semantic workspace state. It depends only on Operations.
- `Meta.Surfaces` owns the lightweight workspace descriptor and shared
  publication infrastructure. It has no Roslyn or SqlClient dependency.
- `Meta.Surfaces.Xml` owns XML readers, writers, codecs, and layout.
- `Meta.Surfaces.CSharp` owns C# readers, writers, and transactional source
  publication. It is the only surface package that directly references Roslyn.
- `Meta.Surfaces.Sql` owns SQL workspace reads, writes, operations, and SQL
  Server DDL. It is the only surface package below Integration that directly
  references SqlClient.
- `Meta.Integration` owns descriptor-selected routing, cross-surface
  import/export, CSV support, SQL deployment, and service composition.

The dependency direction is:

```text
Meta.Operations <- Meta.Core
Meta.Operations <- Meta.TypedModels
Meta.Surfaces
Meta.Operations + Meta.Surfaces + Meta.TypedModels <- Meta.Surfaces.Xml
Meta.Operations + Meta.Surfaces <- Meta.Surfaces.CSharp (+ Roslyn)
Meta.Operations <- Meta.Surfaces.Sql (+ SqlClient)
Core + Operations + TypedModels + all surfaces <- Meta.Integration (+ SqlClient)
```

The surface implementations are parallel. Common Surfaces is independent of
Core and Operations; XML and C# share its descriptor and publication
infrastructure, while SQL depends directly on Operations and owns its complete
SQL DDL model.

Public namespaces follow those assembly names. This is a coordinated breaking
change in internal package version `0.1.0-internal.14`; no compatibility facade
preserves the former cross-assembly `Meta.Core.*` namespace ownership.

## Workspace Metadata

`workspace.meta` identifies the workspace surface and carries only the
surface-specific location or non-default storage settings that are needed by
the reader. It is a small descriptor, not a second model format.

## Representation-Neutral Operations

`InMemoryWorkspace` contains a generic model and its instance graph.
`InMemoryOperations` applies the common operation language to that state and
returns a separate candidate with structured results. A rejected operation
batch does not modify its input.

```csharp
var execution = InMemoryOperations.Execute(
    workspace,
    new Operation.AddEntity("Order"),
    new Operation.AddProperty("Order", "OrderNumber", IsRequired: true));
```

Operation construction and execution enforce the common Meta identity,
referential-integrity, and model rules before a surface publishes the result.

`TypedModelMapper` maps typed CLR contracts to and from the same
`InMemoryWorkspace` state. The mapper owns no filesystem, XML, Roslyn, or SQL
behavior; each surface remains responsible for its own representation.

## XML Workspace

`XmlWorkspaceReader.OpenAsync` opens one exact workspace directory and returns
an `OpenedXmlWorkspace`. `XmlWorkspaceWriter` owns XML layout, shards, locking,
stale-write detection, and atomic publication.

```csharp
var opened = await XmlWorkspaceReader.OpenAsync(@".\Workspace");
var execution = InMemoryOperations.Execute(
    opened.State,
    new Operation.AddEntity("Order"));

await XmlWorkspaceWriter.WriteAsync(
    opened,
    execution.Workspace,
    execution.Results);
```

Use `XmlWorkspaceWriter.WriteNewAsync` for a new XML workspace. XML loading
uses the exact path supplied by the caller; it does not search parent folders.

## C# Workspace

`CSharpWorkspace` reads and writes a workspace represented by C# sources.
`MetaCSharpReader` and `MetaCSharpWriter` translate between those sources and
the shared semantic workspace state. Creation and publication read the written
sources back and reject a semantic difference before publishing them.

Object references are the natural in-memory C# integrity surface. Serialized
identities are transport details used to persist and reconstruct those
references.

## SQL Workspace

`SqlWorkspace` opens and creates a workspace in a SQL database. Its operation
target applies the common operation language as SQL DDL and DML, so ordinary
reads and mutations do not need to materialize every instance row in memory.
`SqlWorkspaceSource` exposes the common read contract for bounded queries and
record streams.

Full conversion to another surface necessarily reads the source structure and
materializes the semantic state needed by the destination writer.

## Integration Services

`Meta.Integration.ServiceCollection` composes the integration services:

- `ImportService` imports SQL and CSV data into semantic workspace state.
- `ExportService` writes selected external representations and streams CSV.
- `SqlServerDeploymentService` deploys SQL scripts to an external SQL Server.
- Diff and merge services compose semantic reads and operation plans.

These services cross a surface boundary. They do not define the workspace
surface contract itself.

## Failure Model

- Invalid operations produce structured operation or validation failures.
- A surface rejects data it cannot carry without semantic loss.
- XML and C# publication detects a stale source and refuses to overwrite it.
- SQL creation and execution are verified by the SQL surface before completion.

Use the surface API that matches the workspace being operated on. Do not parse
workspace files in a product service to discover how another surface works.
