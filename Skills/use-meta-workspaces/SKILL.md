---
name: use-meta-workspaces
description: Inspect, query, create, copy, and convert Meta workspace surfaces through the generic meta CLI without treating files as the API.
---

# Use Meta workspaces

Use this skill when the task concerns a workspace as a representation-neutral
model and instance graph. Use an owning product CLI instead when authoring
domain meaning such as a Data Vault hub, warehouse fact, or analytics measure.

## Inspect before changing

Resolve the workspace path and read `workspace.meta` only to identify its
surface. Then inspect it through the CLI:

```powershell
meta status --workspace <path>
meta list entities --workspace <path>
meta graph stats --workspace <path>
meta query <Entity> --top 20 --workspace <path>
meta view entity <Entity> --workspace <path>
```

Run `meta help <command>` and, for nested commands, the full command path before
forming the final invocation. Use `list`, `view`, `query`, and `graph` instead
of dumping large XML files.

## Preserve the workspace contract

- XML, SQL, and C# are surfaces over the same modeled truth.
- A workspace has one authoritative representation at one location.
- `--workspace` normally selects an existing workspace. Output surface options
  create a new workspace; they do not mean "also write another canonical copy."
- The current directory is the ordinary default where the command documents
  it, but explicit paths are safer in scripts and meshes.
- Use the owning CLI for domain mutations. Reserve generic `insert`,
  `bulk-insert`, `delete`, and model mutation for tasks that explicitly require
  low-level workspace operations.

## Create or project a surface

The generic surface projection contract is:

```powershell
meta create --source-workspace <source> --with-instances --xml <destination>
meta create --source-workspace <source> --with-instances --csharp <destination>
meta create --source-workspace <source> --with-instances --sql <destination> --connection-env <ENV_NAME>
```

Confirm the current syntax with `meta help create`. Use `--with-instances` only
when instance data belongs in the destination. Do not add a project file merely
because the selected representation is C#; a C# workspace is a source surface,
not necessarily a compiled application.

## Validate the result

Inspect source and destination with `meta status`, compare the relevant model
and instance counts, and exercise the consumer that motivated the projection.
For representation-integrity work, prove the supported round trip rather than
adding a standalone file-shape check. Keep temporary projections outside the
tracked workspace unless the task explicitly requires them.
