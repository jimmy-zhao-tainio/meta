# Meta CLI Command Spec v1

This spec defines the canonical `meta` command surface.

## Purpose

`meta` manages metadata model + instance data in git and emits SQL/C# outputs plus SQL-project consumables.

- Developer flow: pull -> edit metadata -> commit.
- CI/CD flow: build the emitted SQL project artifact -> drop metadata DB -> recreate metadata DB.
- Out of scope: reconcile/merge/migration engine.

## Workspace contract

Workspace discovery:
- Default: search upward from current directory.
- Override: `--workspace <path>`.

Canonical files:
- `workspace.xml`
- `metadata/model.xml`
- `metadata/instance/<Entity>.xml`

## Instance XML contract

Every instance element uses one strict shape:

- Entity element name is the model entity name.
- Mandatory identity attribute: `Id="<non-empty-string>"`.
- Relationship usage is single-target and stored only as attributes:
  - attribute name: `<TargetEntity>Id`
  - value: non-empty identity string
  - omitted attribute means relationship usage is missing (and fails `meta check` for required relationships)
- Non-relationship properties are stored only as child elements:
  - `<PropertyName>text</PropertyName>`
  - missing element means property is unset
  - present empty element means explicit empty string

Writer/reader rules:

- No other instance attributes are allowed in strict mode.
- No relationship child elements are allowed in strict mode.
- No null-to-empty coercion at persistence boundaries.

## Determinism contract

Writers are byte-stable for identical logical state.

- UTF-8 (no BOM), LF line endings.
- Stable ordering for entities, properties, relationships, shards, instances, and attributes.
- Mutating commands normalize implicitly before save.

## Diff and merge identity rules

- Equal-model instance diff:
  - `meta instance diff <leftWorkspace> <rightWorkspace>`
  - hard-fails unless left/right `model.xml` files are byte-identical.
  - writes a normal workspace using fixed model template `Meta.Cli/Templates/InstanceDiffModel.Equal.xml`.
  - merge command: `meta instance merge <targetWorkspace> <diffWorkspace>`.
- Aligned instance diff:
  - `meta instance diff-aligned <leftWorkspace> <rightWorkspace> <alignmentWorkspace>`
  - supports model differences using explicit entity/property mappings from fixed alignment contract.
  - writes a normal workspace using fixed model template `Meta.Cli/Templates/InstanceDiffModel.Aligned.xml`.
  - merge command: `meta instance merge-aligned <targetWorkspace> <diffWorkspace>`.
- Diff semantics:
  - no persisted booleans, hashes, fingerprints, or timestamps.
  - no packed field formats.
  - set differences are represented by instance presence (`*NotIn*` entities).
  - FK-like references use `<Entity>Id` naming.
- Merge semantics:
  - rows are matched by `Entity + Id`.
  - merge preserves incoming ids from the diff artifact.
  - merge never remaps ids in the target workspace.
  - if the target no longer matches the diff left snapshot, merge fails instead of guessing.

## Id policy

- Row identity is `Id` and is treated as stable.
- `Id` values are opaque strings; they do not need to be numeric.
- `meta import csv` requires a column named `Id` (case-insensitive header match).
- Existing-entity CSV import is upsert-by-Id only: matching Id updates, new Id inserts, missing CSV rows are preserved.
- Entity containers are always named `<EntityName>List` in XML and emitted C# collections use the same naming.
- Landing relationship candidates should stay as scalar `...Id` fields until promoted.
- `meta model refactor property-to-relationship` preserves row identities and only rewrites fields.
- `meta model refactor relationship-to-property` preserves row identities and only rewrites fields.
- `--auto-id` is only for creating new rows when the source does not already carry an external identity.
- `meta instance merge` and `meta instance merge-aligned` preserve ids from their diff artifacts.

## Instance and relationship addressing

Instance addressing:
- `<Entity> <Id>` only.

Relationship usage:
- `meta instance relationship set <FromEntity> <FromId> --to <RelationshipSelector> <ToId>`
- `meta instance relationship list <FromEntity> <FromId>`

Semantics:
- `set` updates exactly one declared relationship usage selected by target entity, role, or implied field name.

## Query filters

`meta query` supports:
- `--equals <Field> <Value>` (repeatable)
- `--contains <Field> <Value>` (repeatable)
- `--top <n>`

Filters are ANDed in provided order.

## Errors and exit codes

Human output:
- one clear failure line
- optional blocker/details
- optional single `Next:` line (last line)

Exit codes:
- `0` success
- `1` usage/argument error, diff has differences, or merge precondition conflict
- `2` integrity check failure
- `3` reserved
- `4` workspace/data/model/operation error
- `5` generation failure
- `6` internal error

## Global flags

- `--workspace <path>`
- `--strict`

## Command surface

<!-- GENERATED-COMMAND-SURFACE:START -->
Workspace:
- `meta init [<path>]`
- `meta status [--workspace <path>]`
- `meta workspace merge <leftWorkspace> <rightWorkspace> --new-workspace <path> --model <name>`

Inspect and validate:
- `meta check [--workspace <path>]`
- `meta list entities [--workspace <path>]`
- `meta list properties <Entity> [--workspace <path>]`
- `meta list relationships <Entity> [--workspace <path>]`
- `meta view entity <Entity> [--workspace <path>]`
- `meta view instance <Entity> <Id> [--workspace <path>]`
- `meta query <Entity> [--equals <Field> <Value>]... [--contains <Field> <Value>]... [--top <n>] [--workspace <path>]`
- `meta graph stats [--workspace <path>] [--top <n>] [--cycles <n>]`
- `meta graph inbound <Entity> [--workspace <path>] [--top <n>]`

Model mutation and refactor:
- `meta model suggest [--show-keys] [--explain] [--print-commands] [--workspace <path>]`
- `meta model refactor property-to-relationship --source <Entity.Property> --target <Entity> --lookup <Property> [--role <Role>] [--preserve-property] [--workspace <path>]`
- `meta model refactor relationship-to-property --source <Entity> --target <Entity> [--role <Role>] [--property <PropertyName>] [--workspace <path>]`
- `meta model add-entity <Name> [--workspace <path>]`
- `meta model rename-entity <Old> <New> [--workspace <path>]`
- `meta model drop-entity <Entity> [--workspace <path>]`
- `meta model add-property <Entity> <Property> [--required true|false] [--default-value <Value>] [--workspace <path>]`
- `meta model rename-property <Entity> <Old> <New> [--workspace <path>]`
- `meta model rename-relationship <FromEntity> <ToEntity> [--role <Role>] [--workspace <path>]`
- `meta model drop-property <Entity> <Property> [--workspace <path>]`
- `meta model add-relationship <FromEntity> <ToEntity> [--role <RoleName>] [--default-id <ToId>] [--workspace <path>]`
- `meta model drop-relationship <FromEntity> <ToEntity> [--workspace <path>]`

Instance mutation:
- `meta insert <Entity> [<Id>|--auto-id] --set Field=Value [--set Field=Value ...] [--workspace <path>]`
- `meta bulk-insert <Entity> [--from tsv|csv] [--file <path>|--stdin] [--key Field[,Field2...]] [--auto-id] [--workspace <path>]`
- `meta instance update <Entity> <Id> --set Field=Value [--set Field=Value ...] [--workspace <path>]`
- `meta instance rename-id <Entity> <OldId> <NewId> [--workspace <path>]`
- `meta instance relationship set <FromEntity> <FromId> --to <RelationshipSelector> <ToId> [--workspace <path>]`
- `meta instance relationship list <FromEntity> <FromId> [--workspace <path>]`
- `meta delete <Entity> <Id> [--workspace <path>]`

Diff and merge:
- `meta instance diff <leftWorkspace> <rightWorkspace>`
- `meta instance merge <targetWorkspace> <diffWorkspace>`
- `meta instance diff-aligned <leftWorkspace> <rightWorkspace> <alignmentWorkspace>`
- `meta instance merge-aligned <targetWorkspace> <diffWorkspace>`

Import and generate:
- `meta import sql <connectionString> <schema> --new-workspace <path>`
- `meta import csv <csvFile> --entity <EntityName> [--workspace <path> | --new-workspace <path>]`
- `meta generate sql --out <dir> [--workspace <path>]`
- `meta generate csharp --out <dir> [--workspace <path>] [--tooling]`
- `meta generate ssdt --out <dir> [--workspace <path>]`

<!-- GENERATED-COMMAND-SURFACE:END -->

## Command quick reference (summary + example)

<!-- GENERATED-COMMAND-QUICKREF:START -->
Workspace:

| Command | Summary | Example |
|---|---|---|
| `meta init [<path>]` | Initialize a metadata workspace. | `meta init .` |
| `meta status [--workspace <path>]` | Show workspace summary and model/instance sizes. | `meta status` |
| `meta workspace merge <leftWorkspace> <rightWorkspace> --new-workspace <path> --model <name>` | Merge two full workspaces into a new workspace with a required merged model name. | `meta workspace merge .\LeftWorkspace .\RightWorkspace --new-workspace .\MergedWorkspace --model MergedModel` |

Inspect and validate:

| Command | Summary | Example |
|---|---|---|
| `meta check [--workspace <path>]` | Run model + instance integrity checks. | `meta check` |
| `meta list entities [--workspace <path>]` | List entities with instance/property/relationship counts. | `meta list entities` |
| `meta list properties <Entity> [--workspace <path>]` | List properties for one entity. | `meta list properties Cube` |
| `meta list relationships <Entity> [--workspace <path>]` | List declared outgoing relationships for one entity. | `meta list relationships Measure` |
| `meta view entity <Entity> [--workspace <path>]` | Show one entity schema card. | `meta view entity Cube` |
| `meta view instance <Entity> <Id> [--workspace <path>]` | Show one instance as field/value table. | `meta view instance Cube 1` |
| `meta query <Entity> [--equals <Field> <Value>]... [--contains <Field> <Value>]... [--top <n>] [--workspace <path>]` | Search instances using equals/contains filters. | `meta query Cube --contains CubeName Sales` |
| `meta graph stats [--workspace <path>] [--top <n>] [--cycles <n>]` | Show graph metrics, top degrees, and cycle samples. | `meta graph stats --top 10 --cycles 5` |
| `meta graph inbound <Entity> [--workspace <path>] [--top <n>]` | Show entities that point to the target entity. | `meta graph inbound Cube --top 20` |

Model mutation and refactor:

| Command | Summary | Example |
|---|---|---|
| `meta model suggest [--show-keys] [--explain] [--print-commands] [--workspace <path>]` | Read-only relationship inference from model + instance data. Only fully resolvable many-to-one promotions are printed, using the sanctioned Id-based `<TargetEntity>Id -> <TargetEntity>.Id` inference path. | `meta model suggest --workspace .\Workspace` |
| `meta model refactor property-to-relationship --source <Entity.Property> --target <Entity> --lookup <Property> [--role <Role>] [--preserve-property] [--workspace <path>]` | Atomically convert a scalar source property to a required relationship using a target lookup key. Source property is dropped by default; `--preserve-property` keeps it when no naming collision would result. | `meta model refactor property-to-relationship --source Order.WarehouseId --target Warehouse --lookup Id` |
| `meta model refactor relationship-to-property --source <Entity> --target <Entity> [--role <Role>] [--property <PropertyName>] [--workspace <path>]` | Atomically convert a required relationship back to a required scalar Id property. | `meta model refactor relationship-to-property --source Order --target Warehouse` |
| `meta model add-entity <Name> [--workspace <path>]` | Add a new entity definition. | `meta model add-entity SourceSystem` |
| `meta model rename-entity <Old> <New> [--workspace <path>]` | Atomically rename an entity, update relationship targets, and rename implied non-role relationship field names. | `meta model rename-entity Warehouse StorageLocation` |
| `meta model drop-entity <Entity> [--workspace <path>]` | Drop an entity (blocked if instances or inbound relationships exist). | `meta model drop-entity SourceSystem` |
| `meta model add-property <Entity> <Property> [--required true\|false] [--default-value <Value>] [--workspace <path>]` | Add an entity property. | `meta model add-property Cube Purpose --required true --default-value Unknown` |
| `meta model rename-property <Entity> <Old> <New> [--workspace <path>]` | Rename a property in one entity. | `meta model rename-property Cube Purpose Description` |
| `meta model rename-relationship <FromEntity> <ToEntity> [--role <Role>] [--workspace <path>]` | Rename a relationship usage by setting or clearing its role; instance relationship field names are rewritten atomically. Omit `--role` to clear the role. | `meta model rename-relationship System SystemType --role PrimarySystemType` |
| `meta model drop-property <Entity> <Property> [--workspace <path>]` | Drop a property from an entity. | `meta model drop-property Cube Description` |
| `meta model add-relationship <FromEntity> <ToEntity> [--role <RoleName>] [--default-id <ToId>] [--workspace <path>]` | Add a required relationship; use --default-id to backfill existing source instances. | `meta model add-relationship Measure Cube --default-id 1` |
| `meta model drop-relationship <FromEntity> <ToEntity> [--workspace <path>]` | Drop a declared relationship (blocked if in use). | `meta model drop-relationship Measure Cube` |

Instance mutation:

| Command | Summary | Example |
|---|---|---|
| `meta insert <Entity> [<Id>\|--auto-id] --set Field=Value [--set Field=Value ...] [--workspace <path>]` | Insert one instance by explicit Id or auto-generated numeric Id. Use --auto-id only when creating a brand-new row with no external identity. | `meta insert Cube 10 --set "CubeName=Ops Cube"` |
| `meta bulk-insert <Entity> [--from tsv\|csv] [--file <path>\|--stdin] [--key Field[,Field2...]] [--auto-id] [--workspace <path>]` | Bulk insert instances from tsv/csv input. Use --auto-id only for new rows whose source data does not carry an external Id. | `meta bulk-insert Cube --from tsv --file .\cube.tsv --key Id` |
| `meta instance update <Entity> <Id> --set Field=Value [--set Field=Value ...] [--workspace <path>]` | Update fields on one instance by Id. Use `instance rename-id` to change the row Id itself. | `meta instance update Cube 1 --set RefreshMode=Manual` |
| `meta instance rename-id <Entity> <OldId> <NewId> [--workspace <path>]` | Atomically rename one row Id and update inbound relationships that reference it. | `meta instance rename-id Cube 1 Cube-001` |
| `meta instance relationship set <FromEntity> <FromId> --to <RelationshipSelector> <ToId> [--workspace <path>]` | Set one relationship usage. Selector may be target entity, relationship role, or implied relationship field name. | `meta instance relationship set Measure 1 --to Cube 2` |
| `meta instance relationship list <FromEntity> <FromId> [--workspace <path>]` | List relationship usage for one instance. | `meta instance relationship list Measure 1` |
| `meta delete <Entity> <Id> [--workspace <path>]` | Delete one instance by Id. | `meta delete Cube 10` |

Diff and merge:

| Command | Summary | Example |
|---|---|---|
| `meta instance diff <leftWorkspace> <rightWorkspace>` | Diff instance data for two workspaces with byte-identical model.xml. | `meta instance diff .\LeftWorkspace .\RightWorkspace` |
| `meta instance merge <targetWorkspace> <diffWorkspace>` | Apply an equal-model instance diff artifact to a target workspace. | `meta instance merge .\TargetWorkspace .\RightWorkspace.instance-diff` |
| `meta instance diff-aligned <leftWorkspace> <rightWorkspace> <alignmentWorkspace>` | Diff mapped instance data using an explicit alignment workspace. | `meta instance diff-aligned .\LeftWorkspace .\RightWorkspace .\AlignmentWorkspace` |
| `meta instance merge-aligned <targetWorkspace> <diffWorkspace>` | Apply an aligned instance diff artifact to a target workspace. | `meta instance merge-aligned .\TargetWorkspace .\RightWorkspace.instance-diff-aligned` |

Import and generate:

| Command | Summary | Example |
|---|---|---|
| `meta import sql <connectionString> <schema> --new-workspace <path>` | Import metadata from SQL into a new workspace. | `meta import sql "Server=...;Database=...;..." dbo --new-workspace .\ImportedWorkspace` |
| `meta import csv <csvFile> --entity <EntityName> [--workspace <path> \| --new-workspace <path>]` | Import one CSV file as one entity + rows. The CSV must include a column named Id (case-insensitive match); existing-entity import is deterministic upsert by Id. | `meta import csv .\landing.csv --entity Landing --new-workspace .\ImportedWorkspace` |
| `meta generate sql --out <dir> [--workspace <path>]` | Generate SQL schema + data scripts. | `meta generate sql --out .\out\sql` |
| `meta generate csharp --out <dir> [--workspace <path>] [--tooling]` | Generate C# model and entity classes. | `meta generate csharp --out .\out\csharp` |
| `meta generate ssdt --out <dir> [--workspace <path>]` | Generate Schema.sql, Data.sql, PostDeploy.sql, and Metadata.sqlproj. | `meta generate ssdt --out .\out\ssdt` |

<!-- GENERATED-COMMAND-QUICKREF:END -->

## Diff/merge example

```powershell
meta instance diff .\LeftWorkspace .\RightWorkspace
# Output includes: DiffWorkspace: <path>

meta instance merge .\LeftWorkspace "<path from diff output>"
```






