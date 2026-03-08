# isomorphic-metadata

`isomorphic-metadata` is a deterministic metadata backend. The canonical representation is an XML workspace on disk (git-friendly), but you can round-trip: materialize a workspace from SQL, emit SQL/C# representations and SQL-project consumables, and load/save model instances via C# consumables for tooling.

This repo ships three CLI tools:

`meta` (Meta CLI): workspace/model/instance operations, diff/merge, import, generate.  
`meta-weave` (MetaWeave CLI): authoring, suggestion, validation, and materialization of sanctioned cross-model property bindings.  
`meta-fabric` (MetaFabric CLI): scoped validation over sanctioned weave workspaces.

BI-specific sanctioned models and CLIs live in the separate `meta-bi` repository.

## Metadata foundations (project terminology)

In this project, metadata is not "extra comments about data". Metadata is the product model itself:

- `metadata/model.xml`: the schema contract (entities, scalar properties, required relationships).
- `metadata/instance/*.xml`: the instance graph for that model (rows + relationship usages).
- `workspace.xml`: workspace-level configuration for layout/encoding/order and storage.

Core terms:

- `Model`: named set of entity definitions.
- `Entity`: a record type (similar to a table).
- `Property`: scalar attribute on an entity instance.
- `Relationship`: required reference from one entity to another.
- `Instance`: one row/record of an entity, identified by `Id`.
- `Workspace`: on-disk unit that contains model + instance + workspace config.

### Why isomorphic metadata

Isomorphic means the **same semantics** can live in multiple forms without translation drift.

Think of it as one model with three "native surfaces", each optimized for a different kind of work:

- **XML workspace**: canonical, deterministic, and git-friendly (clean diffs, reviewable history, mergeable refactors).
- **SQL**: **data-first** and database-native (easy to inspect/query/validate at scale; good interchange format when your source of truth starts in a database).
- **C#**: **application-first** (strongly typed objects for tools and apps; no XML parsing in consumer code; easy to integrate into pipelines/services).

Because the semantics are the same, multiple producers and consumers can collaborate without rewriting the model for each layer:

- analysts/modelers edit model + instance in workspace form,
- platform engineers consume SQL outputs and SQL-project consumables in their pipelines,
- application/tooling engineers consume emitted C# APIs.

### Why git matters for metadata

The workspace is designed for version control:

- deterministic writer output (stable ordering, utf-8 no bom, LF) keeps diffs clean.
- model and instance changes are code-reviewed like source code.
- branch/merge workflows apply to metadata evolution.
- refactors are auditable through before/after XML deltas.

## Workspace contract

A workspace is a directory containing:

`workspace.xml`  
`metadata/model.xml`  
`metadata/instance/...`

Instance data may be sharded: multiple instance files can contain rows for the same entity; load merges those shards and save preserves existing shard file layout (new rows for an entity are written to that entity's primary shard).

## Meta CLI at a glance

`meta` operates on workspaces and provides these capability groups:

Workspace operations: create and inspect workspaces (`init`, `status`).  
Validation and inspection: check integrity and explore model/instance (`check`, `list`, `view`, `query`, `graph`).  
Edits: mutate models and instance data (`model ...`, `insert`, `delete`, `bulk-insert`, `instance update`, `instance rename-id`, `instance relationship set|list`, `instance diff`, `instance merge`).  
Model analysis and guided refactor: read-only relationship inference (`model suggest`) and atomic model+instance refactors (`model refactor property-to-relationship`, `model refactor relationship-to-property`).  
Pipelines: import and emit representations (`import ...`, `generate ...`).

Workflow: model + instance workspace -> `meta` emits C#/SQL consumables -> your patterns/generators produce organization-specific artifacts.

## One sample across XML, SQL, and C#

### XML model (`metadata/model.xml`)

```xml
<?xml version="1.0" encoding="utf-8"?>
<Model name="EnterpriseBIPlatform">
  <EntityList>
    <Entity name="Cube">
      <PropertyList>
        <Property name="CubeName" />
        <Property name="Purpose" isRequired="false" />
        <Property name="RefreshMode" isRequired="false" />
      </PropertyList>
    </Entity>

    <Entity name="Measure">
      <PropertyList>
        <Property name="MeasureName" />
        <Property name="MDX" isRequired="false" />
      </PropertyList>
      <RelationshipList>
        <Relationship entity="Cube" />
      </RelationshipList>
    </Entity>

    <Entity name="SystemType">
      <PropertyList>
        <Property name="TypeName" />
      </PropertyList>
    </Entity>

    <Entity name="System">
      <PropertyList>
        <Property name="SystemName" />
        <Property name="Version" isRequired="false" />
      </PropertyList>
      <RelationshipList>
        <Relationship entity="SystemType" />
      </RelationshipList>
    </Entity>

    <Entity name="SystemCube">
      <PropertyList>
        <Property name="ProcessingMode" isRequired="false" />
      </PropertyList>
      <RelationshipList>
        <Relationship entity="Cube" />
        <Relationship entity="System" />
      </RelationshipList>
    </Entity>
  </EntityList>
</Model>
```

### XML instance (`metadata/instance/*.xml`)

```xml
<?xml version="1.0" encoding="utf-8"?>
<EnterpriseBIPlatform>
  <CubeList>
    <Cube Id="1">
      <CubeName>Sales Performance</CubeName>
      <Purpose>Monthly revenue and margin tracking.</Purpose>
      <RefreshMode>Scheduled</RefreshMode>
    </Cube>
  </CubeList>

  <MeasureList>
    <Measure Id="1" CubeId="1">
      <MeasureName>Sales Amount</MeasureName>
      <MDX>[Measures].[Sales Amount]</MDX>
    </Measure>
  </MeasureList>

  <SystemTypeList>
    <SystemType Id="1">
      <TypeName>Internal</TypeName>
    </SystemType>
  </SystemTypeList>

  <SystemList>
    <System Id="1" SystemTypeId="1">
      <SystemName>Enterprise Analytics Platform</SystemName>
      <Version>2.1</Version>
    </System>
  </SystemList>

  <SystemCubeList>
    <SystemCube Id="1" CubeId="1" SystemId="1">
      <ProcessingMode>InMemory</ProcessingMode>
    </SystemCube>
  </SystemCubeList>
</EnterpriseBIPlatform>
```

### SQL representation (`meta generate sql`)

`meta generate sql` emits `schema.sql` and `data.sql` as deterministic SQL consumables (DDL and INSERT scripts).

```sql
CREATE TABLE [dbo].[Cube] (
  [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [CubeName] NVARCHAR(256) NOT NULL,
  [Purpose] NVARCHAR(256) NULL,
  [RefreshMode] NVARCHAR(256) NULL
);

CREATE TABLE [dbo].[Measure] (
  [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [MeasureName] NVARCHAR(256) NOT NULL,
  [MDX] NVARCHAR(256) NULL,
  [CubeId] INT NOT NULL
);

CREATE TABLE [dbo].[SystemType] (
  [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [TypeName] NVARCHAR(256) NOT NULL
);

CREATE TABLE [dbo].[System] (
  [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [SystemName] NVARCHAR(256) NOT NULL,
  [Version] NVARCHAR(256) NULL,
  [SystemTypeId] INT NOT NULL
);

CREATE TABLE [dbo].[SystemCube] (
  [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [ProcessingMode] NVARCHAR(256) NULL,
  [CubeId] INT NOT NULL,
  [SystemId] INT NOT NULL
);

ALTER TABLE [dbo].[Measure]
ADD CONSTRAINT [FK_Measure_Cube_CubeId]
FOREIGN KEY ([CubeId]) REFERENCES [dbo].[Cube]([Id]);

ALTER TABLE [dbo].[System]
ADD CONSTRAINT [FK_System_SystemType_SystemTypeId]
FOREIGN KEY ([SystemTypeId]) REFERENCES [dbo].[SystemType]([Id]);

ALTER TABLE [dbo].[SystemCube]
ADD CONSTRAINT [FK_SystemCube_Cube_CubeId]
FOREIGN KEY ([CubeId]) REFERENCES [dbo].[Cube]([Id]);

ALTER TABLE [dbo].[SystemCube]
ADD CONSTRAINT [FK_SystemCube_System_SystemId]
FOREIGN KEY ([SystemId]) REFERENCES [dbo].[System]([Id]);
```

### C# representation (`meta generate csharp`)

#### What gets emitted

`meta generate csharp` emits dependency-free consumer types in a namespace that matches the model name (for example `namespace EnterpriseBIPlatform`):
- `<ModelName>.cs` (static model facade with a built-in instantiated snapshot, e.g. `EnterpriseBIPlatform`)
- `<Entity>.cs` (one file per entity, POCOs)

#### Consumer usage (dependency-free)

```csharp
using EnterpriseBIPlatform;
using System;
using System.Linq;

// Consumer view: dependency-free POCOs + a strongly-typed model facade.
foreach (var system in EnterpriseBIPlatform.SystemList)
{
    Console.WriteLine($"{system.SystemName} [{system.SystemType.TypeName}]");

    foreach (var link in EnterpriseBIPlatform.SystemCubeList.Where(x => x.SystemId == system.Id))
    {
        var mode = string.IsNullOrEmpty(link.ProcessingMode) ? "n/a" : link.ProcessingMode;
        Console.WriteLine($"  Cube: {link.Cube.CubeName} (mode: {mode})");
    }
}

foreach (var measure in EnterpriseBIPlatform.MeasureList)
{
    Console.WriteLine($"{measure.Cube.CubeName}.{measure.MeasureName}");
}
```

#### Optional tooling surface (`--tooling`)

If you also run `meta generate csharp --tooling`, `meta` emits `<ModelName>.Tooling.cs` in the same model-name namespace, with workspace load/save helpers backed by Meta runtime services. Keep this separate from dependency-free consumer usage.

## Install and run

Build:

```powershell
dotnet build Metadata.Framework.sln
dotnet build MetaWeave.sln
dotnet build MetaFabric.sln
```

On Windows, these builds refresh the published single-file CLIs at:

- `Meta.Cli\bin\publish\win-x64\meta.exe`
- `MetaWeave.Cli\bin\publish\win-x64\meta-weave.exe`
- `MetaFabric.Cli\bin\publish\win-x64\meta-fabric.exe`

Run directly:

```powershell
dotnet run --project Meta.Cli/Meta.Cli.csproj -- help
```

If `Meta.Cli\bin\publish\win-x64` is on your `PATH`:

```powershell
meta help
```

Or run the executable directly:

```powershell
Meta.Cli\bin\publish\win-x64\meta.exe help
```

Build the installer:

```cmd
dotnet build Meta.Installer\Meta.Installer.csproj
```

Then install the foundation CLIs (`meta`, `meta-weave`, `meta-fabric`) into `%LOCALAPPDATA%\meta\bin` and add that directory to your user `PATH`:

```cmd
Meta.Installer\bin\publish\win-x64\install-meta.exe
```

## XML contracts summary

### Model XML

`metadata/model.xml` defines the schema for your metadata workspace: which entities exist, what properties they have, and how entities relate to each other.

A model has one root `<Model name="...">` and then:

- `<Entity>` defines a record type (like a table).
- `name="Cube"` is the singular name.
- Instances are grouped under a deterministic `<EntityName>List` container in XML and emitted C# root collections use the same `<EntityName>List` naming.
- `<PropertyList>` lists scalar fields for the entity. `dataType="string"` is the default and omitted; properties are required by default (`isRequired="true"`), and optional fields use `isRequired="false"`.
- `<RelationshipList>` lists required foreign-key style references to other entities. A relationship points to a target entity and is required by default. In instance XML it becomes `${TargetEntity}Id` by default. If you need multiple relationships to the same target, specify `role="..."` and it becomes `${Role}Id`.
- `Id` is implicit on every entity, so `Property name="Id"` is not written.

Example:

```xml
<Model name="EnterpriseBIPlatform">
  <EntityList>
    <Entity name="Measure">
      <PropertyList>
        <Property name="MeasureName" />
      </PropertyList>
      <RelationshipList>
        <Relationship entity="Cube" />
        <Relationship entity="Cube" role="SourceCube" />
      </RelationshipList>
    </Entity>
  </EntityList>
</Model>
```

### Instance XML

`metadata/instance/*.xml` stores the data for a model.

The root element is the model name (for example `<EnterpriseBIPlatform>`).

Each entity's instances are grouped under a deterministic list container element (for example `<CubeList>`, `<MeasureList>`). Inside the container, each record is written as the singular entity element (for example `<Cube ...>`, `<Measure ...>`).

Every record must have an `Id="..."` attribute.

Relationships are stored as `...Id` attributes on the record. By default a relationship to `Cube` is stored as `CubeId="..."`. If the relationship has a role like `role="SourceCube"`, it is stored as `SourceCubeId="..."`.

Scalar properties are written as child elements. Missing element means unset. An empty element means explicit empty string.

Example:

```xml
<MeasureList>
  <Measure Id="1" CubeId="10" SourceCubeId="11">
    <MeasureName>Sales Amount</MeasureName>
    <Notes />
  </Measure>
</MeasureList>
```

## Meta command guide

Workspace discovery defaults to the current directory (searching upward). Use `--workspace <path>` to override.

Global behavior:

- success responses use `OK: ...`.
- validation/usage/runtime failures use `Error: ...` and non-zero exit.
- `--strict` upgrades warnings to errors for mutating commands.

### Command discovery

| Command | What it is for | Example |
|---|---|---|
| `meta help` | Show top-level command groups and examples. | `meta help` |
| `meta <command> help` | Show usage/options/examples for one command. | `meta model help` |
| `meta <command> <subcommand> --help` | Show detailed subcommand help. | `meta model suggest --help` |

### Workspace commands

| Command | What it is for | Example |
|---|---|---|
| `meta init [<path>]` | Create a new workspace at target path (or current directory). | `meta init .\\Workspace` |
| `meta status` | Print workspace summary (entities, rows, basic counts). | `meta status` |

### Inspect and validate

| Command | What it is for | Example |
|---|---|---|
| `meta check` | Run model+instance integrity checks. | `meta check` |
| `meta list entities` | List entities and high-level counts. | `meta list entities` |
| `meta list properties <Entity>` | List scalar properties for one entity. | `meta list properties Cube` |
| `meta list relationships <Entity>` | List declared outgoing relationships for one entity. | `meta list relationships Measure` |
| `meta view entity <Entity>` | Show one entity schema card. | `meta view entity Cube` |
| `meta view instance <Entity> <Id>` | Show one instance field/value view. | `meta view instance Cube 1` |
| `meta query <Entity> ...` | Search instances with `--equals` / `--contains` filters. | `meta query Cube --contains CubeName Sales --top 20` |
| `meta graph stats` | Print graph metrics, top degrees, cycle samples. | `meta graph stats --top 10 --cycles 5` |
| `meta graph inbound <Entity>` | Show who references the target entity. | `meta graph inbound Cube --top 20` |

### Model commands

| Command | What it is for | Example |
|---|---|---|
| `meta model suggest` | Read-only relationship inference; only fully resolvable many-to-one promotions are printed by default. | `meta model suggest` |
| `meta model suggest --print-commands` | Print copy/paste refactor commands for eligible suggestions. | `meta model suggest --print-commands` |
| `meta model suggest --show-keys --explain` | Include candidate key diagnostics and explain blocks. | `meta model suggest --show-keys --explain` |
| `meta model refactor property-to-relationship ...` | Atomic model+instance rewrite from scalar property to required relationship. Source property is dropped by default; `--preserve-property` keeps it only when the implied relationship usage name would not collide. | `meta model refactor property-to-relationship --source Order.WarehouseId --target Warehouse --lookup Id` |
| `meta model refactor relationship-to-property ...` | Atomic model+instance rewrite from required relationship back to scalar Id property. | `meta model refactor relationship-to-property --source Order --target Warehouse` |
| `meta model add-entity <Name>` | Add a new entity definition. | `meta model add-entity SourceSystem` |
| `meta model rename-model <Old> <New>` | Rename the model contract and instance root element. | `meta model rename-model EnterpriseBIPlatform AnalyticsModel` |
| `meta model rename-entity <Old> <New>` | Atomically rename an entity, update relationship targets, and rename implied non-role FK names. | `meta model rename-entity SourceSystem Source` |
| `meta model drop-entity <Entity>` | Drop entity definition (blocked if instances or inbound refs exist). | `meta model drop-entity SourceSystem` |
| `meta model add-property <Entity> <Property> ...` | Add scalar property; uses `--default-value` to backfill required additions on existing rows. | `meta model add-property Cube Purpose --required true --default-value Unknown` |
| `meta model set-property-required <Entity> <Property> ...` | Change whether a property is required; use `--default-value` to backfill existing missing rows when switching to required. `--default-value` is only valid with `--required true`. | `meta model set-property-required Cube Purpose --required true --default-value Unknown` |
| `meta model rename-property <Entity> <Old> <New>` | Rename one scalar property. | `meta model rename-property Cube Purpose BusinessPurpose` |
| `meta model rename-relationship <FromEntity> <ToEntity> [--role <Role>]` | Rename a relationship usage by setting or clearing its role; instance relationship field names are rewritten atomically. Omit `--role` to clear the role. | `meta model rename-relationship System SystemType --role PrimarySystemType` |
| `meta model drop-property <Entity> <Property>` | Remove one scalar property. | `meta model drop-property Cube Description` |
| `meta model add-relationship <From> <To> ...` | Add required relationship; uses `--default-id` for backfill when source has rows. | `meta model add-relationship Measure Cube --default-id 1` |
| `meta model drop-relationship <From> <To>` | Remove declared relationship from model. | `meta model drop-relationship Measure Cube` |

### Instance commands

| Command | What it is for | Example |
|---|---|---|
| `meta insert <Entity> <Id> --set ...` | Insert one row with explicit Id. | `meta insert Cube 10 --set "CubeName=Ops Cube"` |
| `meta insert <Entity> --auto-id --set ...` | Insert one brand-new row with generated numeric Id when no external identity exists. | `meta insert Cube --auto-id --set "CubeName=Auto Cube"` |
| `meta bulk-insert <Entity> ...` | Insert many rows from tsv/csv file or stdin. | `meta bulk-insert Cube --from tsv --file .\\cube.tsv --key Id` |
| `meta instance update <Entity> <Id> --set ...` | Update fields on one row by Id. Use `instance rename-id` to change the row Id itself. | `meta instance update Cube 10 --set "Purpose=Operations reporting"` |
| `meta instance rename-id <Entity> <OldId> <NewId>` | Atomically rename one row Id and update inbound relationships that reference it. | `meta instance rename-id Cube 10 Cube-010` |
| `meta instance relationship set <FromEntity> <FromId> --to <RelationshipSelector> <ToId>` | Set one relationship usage. The selector may be the target entity, relationship role, or implied relationship field name. | `meta instance relationship set Measure 1 --to Cube 10` |
| `meta instance relationship list <FromEntity> <FromId>` | List relationship usages for one row. | `meta instance relationship list Measure 1` |
| `meta delete <Entity> <Id>` | Delete one row by Id. | `meta delete Cube 10` |

Id policy:
- Row `Id` values are stable strings; they do not need to be numeric.
- Use explicit stable `Id` values whenever the source system already has identity.
- `meta import csv` requires a column named `Id` (case-insensitive header match).
- Scalar landing fields that point at other entities should stay as `...Id` until promoted.
- `--auto-id` is only for creating brand-new rows when no external identity exists.
- `meta model refactor property-to-relationship` preserves row identities; it only rewrites fields.
- `meta model refactor relationship-to-property` preserves row identities; it only rewrites fields.
- `meta instance merge` and `meta instance merge-aligned` preserve ids from the diff artifact and never remap existing rows.

### Instance diff and merge (quick index)

| Command | What it is for | Example |
|---|---|---|
| `meta instance diff <left> <right>` | Diff two workspaces with byte-identical `model.xml`. | `meta instance diff .\\LeftWorkspace .\\RightWorkspace` |
| `meta instance merge <target> <diffWorkspace>` | Apply equal-model diff artifact to target workspace. | `meta instance merge .\\TargetWorkspace .\\RightWorkspace.instance-diff` |
| `meta instance diff-aligned <left> <right> <alignment>` | Diff two workspaces using explicit alignment mappings. | `meta instance diff-aligned .\\LeftWorkspace .\\RightWorkspace .\\AlignmentWorkspace` |
| `meta instance merge-aligned <target> <diffWorkspace>` | Apply aligned diff artifact to target workspace. | `meta instance merge-aligned .\\TargetWorkspace .\\RightWorkspace.instance-diff-aligned` |

### Import and emit representations

| Command | What it is for | Example |
|---|---|---|
| `meta import sql <connectionString> <schema> --new-workspace <path>` | Create a new workspace by importing a SQL schema into workspace form (model + instance). | `meta import sql "Server=.;Database=EnterpriseBIPlatform;Trusted_Connection=True;TrustServerCertificate=True;" dbo --new-workspace .\\ImportedWorkspace` |
| `meta import csv <csvFile> --entity <EntityName> (--new-workspace <path> or --workspace <path>)` | Landing import: one CSV to one entity + rows in new or existing workspace, requiring a CSV column named `Id` and using those values as instance identities. | `meta import csv .\\landing.csv --entity Landing --new-workspace .\\ImportedWorkspace` |
| `meta export csv <Entity> --out <file> [--workspace <path>]` | Export one entity's instance rows to CSV with `Id` first, then relationship usage columns, then scalar properties. | `meta export csv Cube --out .\\cube.csv` |
| `meta generate sql --out <dir>` | Emit deterministic SQL schema + data consumables. | `meta generate sql --out .\\out\\sql` |
| `meta generate csharp --out <dir>` | Emit dependency-free consumer C# API consumables. | `meta generate csharp --out .\\out\\csharp` |
| `meta generate csharp --out <dir> --tooling` | Emit optional tooling helpers for load/save/import flows. | `meta generate csharp --out .\\out\\csharp --tooling` |
| `meta generate ssdt --out <dir>` | Emit `Schema.sql`, `Data.sql`, `PostDeploy.sql`, and `Metadata.sqlproj`. | `meta generate ssdt --out .\\out\\ssdt` |

`meta import csv` is Id-first: the file must contain a column named `Id` (case-insensitive header match). On re-import into an existing entity, matching Id updates the row, new Id inserts the row, and rows missing from the CSV are preserved. Entity containers are always `<EntityName>List`. There is no alternate id-column mapping and no best-effort reconciliation.

## Internal packages

The foundation libraries are packable as internal NuGet packages:

- `Meta.Core`
- `Meta.Adapters`
- `MetaWeave.Core`
- `MetaFabric.Core`

Pack them into the local feed at `.nupkg` with:

```cmd
pack-internal.cmd
```

Downstream repositories should consume these packages instead of reaching back into this repo with project references. That keeps the foundation boundary explicit and prevents BI-side drift from silently editing core.

Today `meta-bi` consumes `Meta.Core` from this feed. `MetaWeave.Core` and `MetaFabric.Core` are published alongside it so downstream repos can adopt weave/fabric core package references without reopening the source boundary.

### Suggest workflow

#### Full example: CSV import -> suggest -> refactor

This is the intended landing workflow: import flat CSVs with stable row Ids, run suggest, then apply an atomic model+instance refactor.

`meta model suggest` is structural and deterministic. It prints strong exact-name relationship promotions by default and prints weak suggestions separately when RI is still complete but the target is only a role-style suffix match or the same source property matches more than one eligible target.

Eligible means the promotion satisfies 100% referential integrity (RI) before any mutation:
- source lands first as a scalar `...Id` field (for example `WarehouseId`)
- target lookup property is complete and unambiguous (no null/blank values, no duplicates)
- source property is complete (no null/blank values)
- every source value resolves to an existing target lookup value (full coverage, no unmatched values)
- source and target scalar types are compatible under strict rules (no implicit casting)
- source values are reused, so the source behaves like a reference and the direction is structurally evidenced as many-to-one

For Id-first landing, the sanctioned structural rule is `ProductId -> Product.Id`, `SupplierId -> Supplier.Id`, and so on. `meta model suggest` does not rely on `Code` fields for this workflow. If any rule fails, the item is not printed.

```cmd
meta import csv .\demo-csv\products.csv --entity Product --new-workspace .\Workspace
cd .\Workspace
meta import csv ..\demo-csv\suppliers.csv --entity Supplier
meta import csv ..\demo-csv\categories.csv --entity Category
meta import csv ..\demo-csv\warehouses.csv --entity Warehouse
meta import csv ..\demo-csv\orders.csv --entity Order

meta model suggest
meta model suggest --print-commands

meta model refactor property-to-relationship --source Order.ProductId --target Product --lookup Id
meta model refactor property-to-relationship --source Order.SupplierId --target Supplier --lookup Id
meta model refactor property-to-relationship --source Order.WarehouseId --target Warehouse --lookup Id

meta model suggest
meta check
```

`meta model suggest` output before refactor:

```text
OK: model suggest
Workspace: .\Workspace
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

`meta model suggest` output after all three refactors:

```text
OK: model suggest
Workspace: .\Workspace
Model: ProductModel
Suggestions: 0
WeakSuggestions: 0

Relationship suggestions
  (none)

Weak relationship suggestions
  (none)
```

Weak role-style example:

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

Weak ambiguous example:

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

Model change example (`metadata/model.xml`) for `Order`:

Before (after CSV import, before refactor):

```xml
<Entity name="Order">
  <PropertyList>
    <Property name="OrderNumber" />
    <Property name="ProductId" />
    <Property name="SupplierId" />
    <Property name="WarehouseId" />
    <Property name="StatusText" />
  </PropertyList>
</Entity>
```

After running all three `property-to-relationship` refactors (`ProductId`, `SupplierId`, `WarehouseId`):

```xml
<Entity name="Order">
  <PropertyList>
    <Property name="OrderNumber" />
    <Property name="StatusText" />
  </PropertyList>
  <RelationshipList>
    <Relationship entity="Product" />
    <Relationship entity="Supplier" />
    <Relationship entity="Warehouse" />
  </RelationshipList>
</Entity>
```

These promotions rewrite each `Order` row from scalar `ProductId`/`SupplierId`/`WarehouseId` elements to required relationship usages with the same names as XML attributes, and by default remove the source scalar properties. Use `--preserve-property` only when the scalar field name will not collide with the implied relationship usage name.

Instance diff/merge can be used to propagate these changes across workspaces (see **Instance diff and merge**).

### Instance diff and merge

#### 1. Purpose

Workspaces evolve independently on different branches. `meta instance diff` and `meta instance merge` are deterministic synchronization primitives for instance data:

- `diff` produces a structured change artifact from Left -> Right.
- `merge` applies that artifact to a target workspace with strict validation.

This is not a text diff. It is a semantic diff over entity rows, scalar values, and relationship usages.

#### 2. What `meta instance diff` does (mechanics)

Inputs:

- Left workspace
- Right workspace

Output:

- a deterministic diff workspace artifact (default sibling path: `<RightWorkspace>.instance-diff`)

Mechanics:

- requires byte-identical `metadata/model.xml` for equal-model diff.
- compares row identity by entity + `Id`.
- compares scalar property values.
- compares relationship usage values (`...Id` attributes).
- compares row presence/absence.

Conceptual change classes captured by the artifact:

- added rows (present in Right, absent in Left)
- removed rows (present in Left, absent in Right)
- updated scalar values on shared rows
- updated relationship references on shared rows

Determinism guarantees:

- canonical ordering/normalization is applied before writing the diff artifact.
- entity/property traversal and row comparisons are stable.
- same inputs produce the same logical diff artifact.

The artifact is a structured description of how to transform Left into Right.

#### 3. What `meta instance merge` does (mechanics)

Inputs:

- target workspace
- diff artifact workspace (from `meta instance diff`)

Operation:

- verifies precondition: target must match the diff Left snapshot.
- applies the diff Right snapshot to the target in memory.
- normalizes and validates the resulting workspace.
- persists once only if validation/postconditions pass.

Atomicity and safety:

- all changes are applied or none are persisted.
- any failure restores pre-merge in-memory state and aborts.
- save is a single workspace write operation after successful validation.
- rows created by merge keep the `Id` values recorded in the diff artifact.
- merge never allocates replacement ids or remaps existing ids.

Determinism:

- same target + same diff artifact -> same resulting workspace state.

#### 4. Referential integrity behavior

Merge is strict. It fails rather than writing invalid state.

- required relationship usage missing -> validation failure.
- relationship target row missing -> validation failure.
- deleting rows that remain referenced -> validation failure.
- relationship updates are applied as part of the snapshot and validated with the whole workspace.

Operation ordering is snapshot-based: merge builds the target end state in memory, validates, then saves. It does not persist partial intermediate steps.

#### 5. Row identity model

Rows are matched by identity (`Entity`, `Id`), not by value similarity.

- identity stability is assumed.
- no fuzzy matching.
- no business-key reconciliation.
- if a target workspace has drifted so that an incoming `Id` would collide with different state, merge fails its precondition instead of remapping or guessing.

#### 6. Typical workflow

Example flow with two independently evolved workspaces:

1. baseline/integration workspace is your Left reference.
2. feature workspace with intended changes is your Right reference.
3. generate diff from Left -> Right.
4. apply diff into integration target (which must still match Left snapshot).

```powershell
meta instance diff .\WorkspaceA .\WorkspaceB
meta instance merge .\WorkspaceA .\WorkspaceB.instance-diff
```

If merge precondition fails, regenerate diff against the current target baseline.

#### 7. Interaction with model refactoring

`model refactor property-to-relationship` changes instance structure (scalar values moved to required relationship usages). Diff/merge captures and replays those instance-level structural value changes when both sides share the same model contract.

For model-contract drift between workspaces, use the aligned diff/merge path (`instance diff-aligned` / `instance merge-aligned`) with explicit alignment mappings.

#### 8. What this is NOT

- not a text merge tool.
- not automatic conflict resolution.
- not model/schema merge.
- not record matching by business key.
- not best-effort partial reconciliation.

#### 9. Command reference

```powershell
meta instance diff <LeftWs> <RightWs>
meta instance merge <TargetWs> <DiffWorkspace>
```

## MetaWeave

MetaWeave is the sanctioned cross-model binding toolchain.

Cross-model links remain ordinary scalar properties in the source workspace. A weave workspace carries the meaning of those properties separately, so the link stays isomorphic across XML, SQL, and C# while still being validated rigorously.

For scoped binding over weave workspaces, the foundation repo carries the sanctioned `MetaFabric` model at `MetaFabric.Workspaces\MetaFabric` and the `meta-fabric` CLI for validation of parent-scoped binding requirements. See `docs/META-FABRIC-BOUNDARY.md` and `docs/WEAVE-FABRIC-NOTE.md`.

A weave workspace contains:

- `ModelReference` rows: which workspaces and models participate in the weave
- `PropertyBinding` rows: which source property resolves to which target identity property

`meta-weave suggest` loads the weave workspace, loads the referenced workspaces, and prints strong missing property bindings only when the source values are complete, reused, and 100% resolvable against a unique target key with exact name alignment. Weak suggestions cover role-style suffix matches and any case where one source property still resolves to more than one eligible target instead of choosing for you. `meta-weave check` then proves that every bound value resolves with 100% RI into the target model.

Current authoring flow:

```cmd
meta-weave help
meta-weave init --new-workspace .\MetaWeave.Workspace
meta-weave add-model --workspace .\MetaWeave.Workspace --alias Source --model SampleSourceCatalog --workspace-path .\MetaWeave.Workspaces\SampleSourceCatalog
meta-weave add-model --workspace .\MetaWeave.Workspace --alias Reference --model SampleReferenceCatalog --workspace-path .\MetaWeave.Workspaces\SampleReferenceCatalog
meta-weave suggest --workspace .\MetaWeave.Workspace
meta-weave add-binding --workspace .\MetaWeave.Workspace --name "SampleSourceCatalog.Attribute.TypeId -> SampleReferenceCatalog.ReferenceType.Id" --source-model Source --source-entity Attribute --source-property TypeId --target-model Reference --target-entity ReferenceType --target-property Id
meta-weave check --workspace .\MetaWeave.Workspace
meta-weave materialize --workspace .\MetaWeave.Workspace --new-workspace .\MergedWorkspace --model SampleCatalogMaterialized
```

For plain full-workspace composition without weave bindings:

```cmd
meta workspace merge .\LeftWorkspace .\RightWorkspace --new-workspace .\MergedWorkspace --model MergedModel
```

Sanctioned examples live under:

- `MetaWeave.Workspaces\Weave-Attribute-ReferenceType`
- `MetaWeave.Workspaces\Weave-Mapping-ReferenceType`

## References

Full command surface and contracts: `COMMANDS.md`
C# tooling services API: `docs/SERVICES_API.md`

## Tests

```powershell
dotnet test Metadata.Framework.sln
dotnet test MetaWeave.sln
dotnet test MetaFabric.sln
```




## Weave Suggest Example

Weak role-style example:

```cmd
meta-weave suggest --workspace .\MetaWeave.Workspaces\Weave-Suggest-WeakRoleReferenceType
```

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

Ambiguous exact-name example:

```cmd
meta-weave suggest --workspace .\MetaWeave.Workspaces\Weave-Suggest-AmbiguousReferenceType
```

```text
OK: weave suggest
Workspace: C:\Users\jimmy\Desktop\meta\MetaWeave.Workspaces\Weave-Suggest-AmbiguousReferenceType
Suggestions: 0
WeakSuggestions: 2

Binding suggestions
  (none)

Weak binding suggestions
  1) Source.Mapping.ReferenceTypeId -> ReferenceA.ReferenceType.Id
  2) Source.Mapping.ReferenceTypeId -> ReferenceB.ReferenceType.Id
```

Additional sanctioned examples:

- `MetaWeave.Workspaces\Weave-Suggest-WeakRoleReferenceType`
- `MetaWeave.Workspaces\Weave-Suggest-AmbiguousReferenceType`

## MetaFabric

MetaFabric is the sanctioned scoped-binding layer over weave workspaces.

A fabric workspace contains:

- `WeaveReference` rows: which sanctioned weave workspaces participate
- `BindingReference` rows: which weave bindings are in scope
- `BindingScopeRequirement` rows: which child bindings must resolve inside an already resolved parent binding

`meta-fabric check` loads the referenced weave workspaces, then validates child bindings under their declared parent scope. This closes the gap where a child weave is globally ambiguous on its own but becomes deterministic once the parent binding is known.

Current command surface:

```cmd
meta-fabric help
meta-fabric init --new-workspace .\MetaFabric.Workspace
meta-fabric check --workspace .\MetaFabric.Workspaces\Fabric-Scoped-Group-CategoryItem
```

Scoped example:

```cmd
meta-weave check --workspace .\MetaWeave.Workspaces\Weave-Scoped-Item-CategoryItem
meta-fabric check --workspace .\MetaFabric.Workspaces\Fabric-Scoped-Group-CategoryItem
```

```text
OK: fabric check
Workspace: C:\Users\jimmy\Desktop\meta\MetaFabric.Workspaces\Fabric-Scoped-Group-CategoryItem
Weaves: 2
Bindings: 2
ResolvedRows: 5
Errors: 0
```

If you run the child weave on its own, it fails because the child binding is globally ambiguous without parent scope:

```text
Error: weave check failed.
  - Item.Name -> CategoryItem.Name: Source row 'Item:item:alpha:common' value 'Common' resolved ambiguously to 'CategoryItem.Name'.
  - Item.Name -> CategoryItem.Name: Source row 'Item:item:beta:common' value 'Common' resolved ambiguously to 'CategoryItem.Name'.
Next: fix the reported bindings and retry meta-weave check.
```

Additional sanctioned examples:

- `MetaWeave.Workspaces\Weave-Scoped-Group-Category`
- `MetaWeave.Workspaces\Weave-Scoped-Item-CategoryItem`
- `MetaFabric.Workspaces\Fabric-Scoped-Group-CategoryItem`





