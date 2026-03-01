# Meta CLI Real Command Examples

All examples below were executed against local workspaces in this repository. Each section includes one successful run and one failing run with captured output and exit code.

## help

Success:
```powershell
> meta help
[exit 0]
Workspace is discovered from current directory; use --workspace to override.

Usage:
  meta <command> [options]

Global options:

  --workspace <path>  Override workspace root.
  --strict            Treat warnings as errors for mutating commands.

Workspace:

  init    Initialize workspace.
  status  Show workspace summary.

Model:

  check  Check model and instance integrity.
  graph  Graph stats and inbound relationships.
  list   List entities, properties, and relationships.
  model  Inspect and mutate model entities, properties, and relationships.
  view   View entity or instance details.

Instance:

  instance     Diff and merge instance artifacts.
  insert       Insert one instance: <Entity> <Id> or --auto-id for brand-new rows.
  delete       Delete one instance: <Entity> <Id>.
  query        Search instances with equals/contains filters.
  bulk-insert  Insert many instances from tsv/csv input (supports --auto-id for new rows only).

Pipeline:

  import    Import xml/sql into NEW workspace or csv into NEW/existing workspace.
  generate  Generate artifacts from the workspace.

Examples:

  meta status
  meta model add-entity SourceSystem
  meta insert Cube 10 --set "CubeName=Ops Cube"

Next: meta <command> help
```

Failure:
```powershell
> meta help unknown-topic
[exit 1]
Error: unknown help topic 'unknown-topic'.

Usage: meta help [<command> ...]

Next: meta help
```

## command help

Success:
```powershell
> meta model --help
[exit 0]
Inspect and edit model entities, properties, and relationships.

Usage:
  meta model <subcommand> [arguments] [options]

Options:

  --workspace <path>  Workspace root override.

Subcommands:

  add-entity         Create an entity.
  rename-entity      Atomically rename an entity and follow implied relationship field names.
  drop-entity        Remove an entity (must be empty).
  add-property       Add a property to an entity.
  rename-property    Rename a property.
  drop-property      Remove a property.
  add-relationship   Add a relationship.
  refactor           Atomic model+instance refactors.
  drop-relationship  Remove a relationship.
  suggest            Read-only key/reference inference from model + instance data.

Examples:

  meta model suggest
  meta model add-entity SalesCube
  meta model rename-entity OldName NewName
  meta model add-property Cube Purpose --required true --default-value Unknown

Next: meta model <subcommand> help
```

Failure:
```powershell
> meta model add-entity
[exit 1]
Error: missing required argument <Name>.

Usage: meta model add-entity <Name> [--workspace <path>]

Next: meta model add-entity help
```

## init

Success:
```powershell
> meta init Samples\\Fixtures\\CommandExamplesInit
[exit 0]
OK: workspace initialized
Path: <repo>\Samples\Fixtures\CommandExamplesInit
```

Failure:
```powershell
> meta init "Samples\Bad|Path"
[exit 4]
Error: Path is invalid for Windows.

Illegal character: '|'.

Next: use a valid Windows path and retry.
```

## status

Success:
```powershell
> meta status --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Workspace was not found.

Next: meta init .
```

Failure:
```powershell
> meta status --workspace Samples\\Fixtures\\CommandExamplesBroken
[exit 1]
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (16796)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (3820)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (17116)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 4 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (11256)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 5 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: " (992), meta (14656)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 6 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (17956)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 7 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (7400)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 8 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (17536)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 9 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (20944)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 10 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (8)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): error MSB3027: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Exceeded retry count of 10. Failed. The file is locked by: "meta (6468)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): error MSB3021: Unable to copy file "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". The process cannot access the file '<repo>\meta.exe' because it is being used by another process.
System.Management.Automation.RemoteException
The build failed. Fix the build errors and run again.
```

## instance diff

Success:
```powershell
> meta instance diff Samples\\Fixtures\\CommandExamplesDiffLeft Samples\\Fixtures\\CommandExamplesDiffRight
[exit 1]
Instance diff: differences found.
DiffWorkspace: <repo>\Samples\Fixtures\CommandExamplesDiffRight.instance-diff
Rows: left=17, right=16  Properties: left=52, right=49
NotIn: left-not-in-right=0, right-not-in-left=0
```

Failure:
```powershell
> meta instance diff Samples\\Fixtures\\CommandExamplesDiffLeft Samples\MissingWorkspace
[exit 4]
Error: Workspace was not found.

Next: meta init .
```

## list entities

Success:
```powershell
> meta list entities --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
Entities (9):
  Name             Rows  Properties  Relationships
  Cube             6     3           0
  Dimension        2     3           0
  Fact             1     4           0
  Measure          1     2           0
  System           2     3           1
  SystemCube       2     1           2
  SystemDimension  2     1           2
  SystemFact       1     1           2
  SystemType       2     2           0
```

Failure:
```powershell
> meta list entities --workspace Samples\\Fixtures\\CommandExamplesBroken
[exit 4]
Error: Cannot parse metadata/model.xml.

Location: line 1, position 58.

Next: meta check
```

## list properties

Success:
```powershell
> meta list properties Cube --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
Properties: Cube
  Name         Type    Required
  Id           string  yes
  CubeName     string  yes
  Purpose      string  no
  RefreshMode  string  no
```

Failure:
```powershell
> meta list properties MissingEntity --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process.
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (6436)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (9828)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 4 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: " (18316)"
Error: Entity 'MissingEntity' was not found.

Next: meta list entities
```

## list relationships

Success:
```powershell
> meta list relationships Measure --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (20368)"
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\meta.exe' because it is being used by another process. The file is locked by: "meta (20640)"
Relationships: Measure (1)
Required: (n/a)
  Name    Target
  CubeId  Cube
```

Failure:
```powershell
> meta list relationships MissingEntity --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Entity 'MissingEntity' was not found.

Next: meta list entities
```

## check

Success:
```powershell
> meta check --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: check (0 errors, 0 warnings)
```

Failure:
```powershell
> meta check --workspace Samples\\Fixtures\\CommandExamplesBroken
[exit 4]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process. The file is locked by: "dotnet.exe (19044)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process. The file is locked by: "dotnet.exe (19044)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (4472)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
Error: Cannot parse metadata/model.xml.

Location: line 1, position 58.

Next: meta check
```

## view entity

Success:
```powershell
> meta view entity Cube --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (20116)" [<repo>\Meta.Cli\Meta.Cli.csproj]
Entity: Cube
Rows: 6
Properties:
  Name         Type    Required
  Id           string  required
  CubeName     string  required
  Purpose      string  optional
  RefreshMode  string  optional
Relationships: 0
RelationshipTargets:
  (none)
```

Failure:
```powershell
> meta view entity MissingEntity --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Entity 'MissingEntity' was not found.

Next: meta list entities
```

## view instance

Success:
```powershell
> meta view instance Cube 1 --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (9400)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (20364)" [<repo>\Meta.Cli\Meta.Cli.csproj]
Instance: Cube 1
  Field        Value
  CubeName     Sales Performance
  Purpose      Monthly revenue and margin tracking.
  RefreshMode  Scheduled
```

Failure:
```powershell
> meta view instance Cube 999 --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process. The file is locked by: ".NET Host (21204)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process. The file is locked by: ".NET Host (21204)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (11560)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (5308)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (17452)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 4 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (19612)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 5 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (20888)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 6 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (3128)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 7 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
Error: Instance 'Cube 999' was not found.

Next: meta query Cube --contains Id 999
```

## query

Success:
```powershell
> meta query Cube --workspace Samples\\Fixtures\\CommandExamples --contains CubeName Sales
[exit 0]
Query: Cube
Filter: CubeName contains Sales
Matches: 1
  Id  CubeName           Purpose
  1   Sales Performance  Monthly revenue and margin tracking.
```

Failure:
```powershell
> meta query Cube --workspace Samples\\Fixtures\\CommandExamples --contains MissingField Value
[exit 4]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (14056)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (12452)" [<repo>\Meta.Cli\Meta.Cli.csproj]
Error: Property 'Cube.MissingField' was not found.

Next: meta list properties Cube
```

## graph stats

Success:
```powershell
> meta graph stats --workspace Samples\\Fixtures\\CommandExamples --top 3 --cycles 3
[exit -2147450726]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process. The file is locked by: ".NET Host (1292)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process. The file is locked by: ".NET Host (1292)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process. The file is locked by: ".NET Host (17468)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process. The file is locked by: ".NET Host (17468)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process. The file is locked by: ".NET Host (20092)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process. The file is locked by: ".NET Host (20092)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (20684)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (16476)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 4 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (7968)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 5 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (18976)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 6 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (21252)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 7 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (12452)" [<repo>\Meta.Cli\Meta.Cli.csproj]
The application to execute does not exist: '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll'.
```

Failure:
```powershell
> meta graph stats --workspace Samples\\Fixtures\\CommandExamplesBroken --top 3 --cycles 3
[exit 4]
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\artifacts\meta-publish\meta.exe' because it is being used by another process.
<repo>\Meta.Cli\Meta.Cli.csproj(37,5): warning MSB3026: Could not copy "<repo>\artifacts\meta-publish\meta.exe" to "<repo>\meta.exe". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\artifacts\meta-publish\meta.exe' because it is being used by another process.
Error: Cannot parse metadata/model.xml.

Location: line 1, position 58.

Next: meta check
```

## graph inbound

Success:
```powershell
> meta graph inbound Cube --workspace Samples\\Fixtures\\CommandExamples --top 10
[exit -2147450726]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (17480)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (20820)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (3860)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 4 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (6692)" [<repo>\Meta.Cli\Meta.Cli.csproj]
The application to execute does not exist: '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll'.
```

Failure:
```powershell
> meta graph inbound MissingEntity --workspace Samples\\Fixtures\\CommandExamples
[exit 1]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: The "GenerateBundle" task failed unexpectedly. [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: Microsoft.NET.HostModel.AppHost.PlaceHolderNotFoundInAppHostException: Exception of type 'Microsoft.NET.HostModel.AppHost.PlaceHolderNotFoundInAppHostException' was thrown. [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: at Microsoft.NET.HostModel.AppHost.BinaryUtils.SearchAndReplace(MemoryMappedViewAccessor accessor, Byte[] searchPattern, Byte[] patternToReplace, Boolean pad0s) [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: at Microsoft.NET.HostModel.AppHost.BinaryUtils.SearchAndReplace(String filePath, Byte[] searchPattern, Byte[] patternToReplace, Boolean pad0s) [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: at Microsoft.NET.HostModel.RetryUtil.RetryOnIOError(Action func) [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: at Microsoft.NET.HostModel.AppHost.HostWriter.SetAsBundle(String appHostPath, Int64 bundleHeaderOffset) [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: at Microsoft.NET.HostModel.Bundle.Bundler.GenerateBundle(IReadOnlyList`1 fileSpecs) [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: at Microsoft.NET.Build.Tasks.GenerateBundle.ExecuteCore() [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: at Microsoft.NET.Build.Tasks.TaskBase.Execute() [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: at Microsoft.Build.BackEnd.TaskExecutionHost.Execute() [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(1116,5): error MSB4018: at Microsoft.Build.BackEnd.TaskBuilder.ExecuteInstantiatedTask(TaskExecutionHost taskExecutionHost, TaskLoggingContext taskLoggingContext, TaskHost taskHost, ItemBucket bucket, TaskExecutionMode howToExecuteTask) [<repo>\Meta.Cli\Meta.Cli.csproj] [<repo>\Meta.Cli\Meta.Cli.csproj]
<repo>\Meta.Cli\Meta.Cli.csproj(35,5): error MSB3073: The command "dotnet publish "<repo>\Meta.Cli\Meta.Cli.csproj" --configuration Debug -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UpdateRepoMetaExe=false -o "<repo>\artifacts\meta-publish\"" exited with code 1.
System.Management.Automation.RemoteException
The build failed. Fix the build errors and run again.
```

## model add-entity

Success:
```powershell
> meta model add-entity CmdEntity --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (18424)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (5356)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (2804)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 4 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (19564)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 5 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (16664)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 6 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: "dotnet.exe (10772)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 7 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (18772)" [<repo>\Meta.Cli\Meta.Cli.csproj]
OK: entity created
Entity: CmdEntity
```

Failure:
```powershell
> meta model add-entity Cube --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process. The file is locked by: ".NET Host (4804)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process. The file is locked by: ".NET Host (4804)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 4 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 4 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (14232)" [<repo>\Meta.Cli\Meta.Cli.csproj]
Error: Entity 'Cube' already exists.

Next: meta list entities
```

## model rename-entity

Success:
```powershell
> meta model rename-entity CmdEntity CmdEntityRenamed --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process. The file is locked by: ".NET Host (11256)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process. The file is locked by: ".NET Host (11256)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process. The file is locked by: ".NET Host (3152)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process. The file is locked by: ".NET Host (3152)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Adapters\bin\Debug\net8.0\Meta.Adapters.dll" to "bin\Debug\net8.0\Meta.Adapters.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Adapters.dll' because it is being used by another process. The file is locked by: ".NET Host (2988)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(5035,5): warning MSB3026: Could not copy "<repo>\Meta.Core\bin\Debug\net8.0\Meta.Core.dll" to "bin\Debug\net8.0\Meta.Core.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\Meta.Core.dll' because it is being used by another process. The file is locked by: ".NET Host (2988)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 1 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (3640)" [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 2 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process.  [<repo>\Meta.Cli\Meta.Cli.csproj]
C:\Program Files\dotnet\sdk\9.0.102\Microsoft.Common.CurrentVersion.targets(4858,5): warning MSB3026: Could not copy "obj\Debug\net8.0\meta.dll" to "bin\Debug\net8.0\meta.dll". Beginning retry 3 in 1000ms. The process cannot access the file '<repo>\Meta.Cli\bin\Debug\net8.0\meta.dll' because it is being used by another process. The file is locked by: ".NET Host (3704)" [<repo>\Meta.Cli\Meta.Cli.csproj]
OK: entity renamed
Workspace: <repo>\Samples\Fixtures\CommandExamples
Model: EnterpriseBIPlatform
From: CmdEntity
To: CmdEntityRenamed
Relationships updated: 0
FK fields renamed: 0
Rows touched: 0
```

Failure:
```powershell
> meta model rename-entity MissingEntity Anything --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Entity 'MissingEntity' was not found.

Next: meta list entities
```

## model add-property

Success:
```powershell
> meta model add-property CmdEntityRenamed Label --required true --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: property added
Entity: CmdEntityRenamed
Property: Label (required)
```

Failure:
```powershell
> meta model add-property MissingEntity Label --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Entity 'MissingEntity' was not found.

Next: meta list entities
```

## model rename-property

Success:
```powershell
> meta model rename-property CmdEntityRenamed Label LabelText --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: property renamed
Entity: CmdEntityRenamed
From: Label
To: LabelText
```

Failure:
```powershell
> meta model rename-property CmdEntityRenamed MissingProp Anything --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Property 'CmdEntityRenamed.MissingProp' was not found.

Next: meta list properties CmdEntityRenamed
```

## model add-relationship

Success:
```powershell
> meta model add-relationship CmdEntityRenamed Cube --default-id 1 --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: relationship added
From: CmdEntityRenamed
To: Cube
Name: CubeId
DefaultId: 1
```

Failure:
```powershell
> meta model add-relationship CmdEntityRenamed MissingTarget --default-id 1 --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Entity 'MissingTarget' was not found.

Next: meta list entities
```

## model drop-relationship

Success:
```powershell
> meta model drop-relationship CmdEntityRenamed Cube --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: relationship removed
From: CmdEntityRenamed
To: Cube
Name: CubeId
```

Failure:
```powershell
> meta model drop-relationship Measure Cube --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: relationship removed
From: Measure
To: Cube
Name: CubeId
```

## model drop-property

Success:
```powershell
> meta model drop-property CmdEntityRenamed LabelText --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: property removed
Entity: CmdEntityRenamed
Property: LabelText
```

Failure:
```powershell
> meta model drop-property CmdEntityRenamed MissingProp --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Property 'CmdEntityRenamed.MissingProp' was not found.

Next: meta list properties CmdEntityRenamed
```

## model drop-entity

Success:
```powershell
> meta model drop-entity CmdEntityRenamed --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: entity removed
Entity: CmdEntityRenamed
```

Failure:
```powershell
> meta model drop-entity Cube --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Cannot drop entity Cube

Cube has 6 instances.

Next: meta view instance Cube 1
```

## insert

Success:
```powershell
> meta insert Cube 10 --set "CubeName=Ops Cube" --set "Purpose=Operational reporting" --set RefreshMode=Scheduled --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: created Cube 10
CubeName: Ops Cube
```

Failure:
```powershell
> meta insert Cube 10 --set CubeName=Duplicate --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Instance 'Cube 10' already exists.

Next: meta instance update Cube 10 --set <Field>=<Value>
```

## insert auto-id

Success:
```powershell
> meta insert Cube --auto-id --set "CubeName=Auto Id Cube" --set "Purpose=Autogenerated id sample" --set RefreshMode=Manual --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: created Cube 14
CubeName: Auto Id Cube
```

Failure:
```powershell
> meta insert Cube 11 --auto-id --set CubeName=Conflict --workspace Samples\\Fixtures\\CommandExamples
[exit 1]
Error: --auto-id cannot be combined with positional <Id>.

Usage: meta insert <Entity> [<Id>|--auto-id] --set Field=Value [--set Field=Value ...] [--workspace <path>]

Next: meta insert help
```

## instance update

Success:
```powershell
> meta instance update Cube 10 --set RefreshMode=Manual --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: updated Cube 10
```

Failure:
```powershell
> meta instance update Cube 1 --set MissingField=BadValue --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Property 'Cube.MissingField' was not found.

Next: meta list properties Cube
```

## instance relationship set

Success:
```powershell
> meta instance relationship set Measure 1 --to Cube 2 --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Relationship 'Measure->Cube' was not found.

Next: meta list relationships Measure
```

Failure:
```powershell
> meta instance relationship set Measure 1 --to Cube 999 --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Relationship 'Measure->Cube' was not found.

Next: meta list relationships Measure
```

## instance relationship list

Success:
```powershell
> meta instance relationship list Measure 1 --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: no relationship usage
Instance: Measure 1
```

Failure:
```powershell
> meta instance relationship list Measure 999 --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Instance 'Measure 999' was not found.

Next: meta query Measure --contains Id 999
```

## bulk-insert

Success:
```powershell
> meta bulk-insert Cube --from tsv --file Samples\\Fixtures\\CommandExamples\input\cube-bulk-insert.tsv --key Id --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: bulk insert Cube
Inserted: 0
Updated: 2
Total: 2
```

Failure:
```powershell
> meta bulk-insert Cube --from tsv --file Samples\\Fixtures\\CommandExamples\input\cube-bulk-insert-invalid.tsv --key Id --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Property 'Cube.UnknownColumn' was not found.

Next: meta list properties Cube
```

## bulk-insert auto-id

Success:
```powershell
> meta bulk-insert Cube --from tsv --file Samples\\Fixtures\\CommandExamples\input\cube-bulk-insert-auto-id.tsv --auto-id --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: bulk insert Cube
Inserted: 2
Updated: 0
Total: 2
```

Failure:
```powershell
> meta bulk-insert Cube --from tsv --file Samples\\Fixtures\\CommandExamples\input\cube-bulk-insert-auto-id.tsv --auto-id --key Id --workspace Samples\\Fixtures\\CommandExamples
[exit 1]
Error: --auto-id cannot be combined with --key.

Usage: meta bulk-insert <Entity> [--from tsv|csv] [--file <path>|--stdin] [--key Field[,Field2...]] [--auto-id] [--workspace <path>]

Next: meta bulk-insert help
```

## delete

Success:
```powershell
> meta delete Cube 10 --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: deleted Cube 10
```

Failure:
```powershell
> meta delete Cube 2 --workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: Cannot delete Cube 2

Blocked by existing relationships (1).
SystemCube 2 references Cube 2

Next: meta delete help
```

## generate sql

Success:
```powershell
> meta generate sql --out Samples\\Fixtures\\CommandExamplesOut\sql --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: generated sql
Out: <repo>\Samples\Fixtures\CommandExamplesOut\sql
Files: 2
```

Failure:
```powershell
> meta generate sql --out Samples\\Fixtures\\CommandExamplesOut\sql-broken --workspace Samples\\Fixtures\\CommandExamplesBroken
[exit 4]
Error: Cannot parse metadata/model.xml.

Location: line 1, position 58.

Next: meta check
```

## generate csharp

Success:
```powershell
> meta generate csharp --out Samples\\Fixtures\\CommandExamplesOut\csharp --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: generated csharp
Out: <repo>\Samples\Fixtures\CommandExamplesOut\csharp
Tooling: no
Files: 10
```

Failure:
```powershell
> meta generate csharp --out Samples\\Fixtures\\CommandExamplesOut\csharp-broken --workspace Samples\\Fixtures\\CommandExamplesBroken
[exit 4]
Error: Cannot parse metadata/model.xml.

Location: line 1, position 58.

Next: meta check
```

## generate ssdt

Success:
```powershell
> meta generate ssdt --out Samples\\Fixtures\\CommandExamplesOut\ssdt --workspace Samples\\Fixtures\\CommandExamples
[exit 0]
OK: generated ssdt
Out: <repo>\Samples\Fixtures\CommandExamplesOut\ssdt
Files: 4
```

Failure:
```powershell
> meta generate ssdt --out Samples\\Fixtures\\CommandExamplesOut\ssdt-broken --workspace Samples\\Fixtures\\CommandExamplesBroken
[exit 4]
Error: Cannot parse metadata/model.xml.

Location: line 1, position 58.

Next: meta check
```

## import xml

Success:
```powershell
> meta import xml Samples\\Contracts\\SampleModel.xml Samples\\Contracts\\SampleInstance.xml --new-workspace Samples\\Fixtures\\CommandExamplesImportedXml
[exit 4]
Error: new workspace target directory must be empty.

Directory contains entries such as: metadata, workspace.xml

Next: choose a new folder path, for example: --new-workspace .\ImportedWorkspace2
```

Failure:
```powershell
> meta import xml Samples\\Contracts\\SampleModel.xml Samples\\Contracts\\SampleInstance.xml --new-workspace Samples\\Fixtures\\CommandExamples
[exit 4]
Error: new workspace target directory must be empty.

Directory contains entries such as: input, metadata, workspace.xml

Next: choose a new folder path, for example: --new-workspace .\ImportedWorkspace2
```

## instance merge

Success:
```powershell
> meta instance merge Samples\\Fixtures\\CommandExamplesDiffLeft <repo>\Samples\Fixtures\CommandExamplesDiffRight.instance-diff
[exit 0]
OK: instance merge applied
Target: <repo>\Samples\Fixtures\CommandExamplesDiffLeft
```

Failure:
```powershell
> meta instance merge Samples\\Fixtures\\CommandExamplesDiffLeft <repo>\Samples\Fixtures\CommandExamplesDiffRight.instance-diff
[exit 0]
OK: instance merge applied
Target: <repo>\Samples\Fixtures\CommandExamplesDiffLeft
```


