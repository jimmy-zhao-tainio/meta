# isomorphic-metadata

`isomorphic-metadata` is a deterministic metadata backend. The canonical representation is an XML workspace on disk (git-friendly), but you can round-trip: materialize a workspace from SQL, emit SQL/C# representations and SQL-project consumables, and load/save model instances via C# consumables for tooling.

This repo ships three CLI tools:

`meta` (Meta CLI): workspace/model/instance operations, diff/merge, import, generate.  
`meta-schema` (MetaSchema CLI): schema extraction into sanctioned `MetaSchema` workspaces.  
`meta-type` (MetaType CLI): creation of sanctioned `MetaType` workspaces.

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
```

On Windows, building `Metadata.Framework.sln` refreshes the published single-file CLI at `Meta.Cli\bin\publish\win-x64\meta.exe`.

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
| `meta model rename-entity <Old> <New>` | Atomically rename an entity, update relationship targets, and rename implied non-role FK names. | `meta model rename-entity SourceSystem Source` |
| `meta model drop-entity <Entity>` | Drop entity definition (blocked if instances or inbound refs exist). | `meta model drop-entity SourceSystem` |
| `meta model add-property <Entity> <Property> ...` | Add scalar property; uses `--default-value` to backfill required additions on existing rows. | `meta model add-property Cube Purpose --required true --default-value Unknown` |
| `meta model rename-property <Entity> <Old> <New>` | Rename one scalar property. | `meta model rename-property Cube Purpose BusinessPurpose` |
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
| `meta generate sql --out <dir>` | Emit deterministic SQL schema + data consumables. | `meta generate sql --out .\\out\\sql` |
| `meta generate csharp --out <dir>` | Emit dependency-free consumer C# API consumables. | `meta generate csharp --out .\\out\\csharp` |
| `meta generate csharp --out <dir> --tooling` | Emit optional tooling helpers for load/save/import flows. | `meta generate csharp --out .\\out\\csharp --tooling` |
| `meta generate ssdt --out <dir>` | Emit `Schema.sql`, `Data.sql`, `PostDeploy.sql`, and `Metadata.sqlproj`. | `meta generate ssdt --out .\\out\\ssdt` |

`meta import csv` is Id-first: the file must contain a column named `Id` (case-insensitive header match). On re-import into an existing entity, matching Id updates the row, new Id inserts the row, and rows missing from the CSV are preserved. Entity containers are always `<EntityName>List`. There is no alternate id-column mapping and no best-effort reconciliation.

### Suggest workflow

#### Full example: CSV import -> suggest -> refactor

This is the intended landing workflow: import flat CSVs with stable row Ids, run suggest, then apply an atomic model+instance refactor.

`meta model suggest` is structural and deterministic. It scans model+instance data and only prints eligible relationship promotions by default.

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

Relationship suggestions
  1) Order.ProductId -> Product (lookup: Product.Id)
  2) Order.SupplierId -> Supplier (lookup: Supplier.Id)
  3) Order.WarehouseId -> Warehouse (lookup: Warehouse.Id)
```

`meta model suggest` output after all three refactors:

```text
OK: model suggest
Workspace: .\Workspace
Model: ProductModel
Suggestions: 0

Relationship suggestions
  (none)
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

## MetaSchema

MetaSchema is the schema-extraction toolchain.

It builds sanctioned `MetaSchema` workspaces from external source schema. `meta` can then treat those workspaces like any other metadata workspace.

Current status: `meta-schema extract sqlserver` connects to SQL Server and creates a `MetaSchema` workspace with `System`, `Schema`, `Table`, and `Field` rows for one declared system/schema/table. `Field.TypeId` is a scalar type identity such as `sqlserver:type:nvarchar`; `MetaSchema` does not mint extraction-local type entities anymore.

#### Commands

```powershell
meta-schema help
meta-schema extract sqlserver --help
```

## MetaType

MetaType is the sanctioned type-vocabulary toolchain.

It creates normal metadata workspaces using the sanctioned `MetaType` model. That model is the future shared ownership boundary for type systems, types, and type specs; `MetaSchema` should point at it rather than mint extraction-local type identities.

Current status: `meta-type init` creates a new populated `MetaType` workspace with sanctioned type systems, types, and type specs.

```cmd
meta-type help
meta-type init --help
meta-type init --new-workspace .\MetaType.Workspace
```

## References

Full command surface and contracts: `COMMANDS.md`

## Tests

```powershell
dotnet test Metadata.Framework.sln
```


