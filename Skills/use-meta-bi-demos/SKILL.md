---
name: use-meta-bi-demos
description: Select, inspect, validate, and run Meta-BI integration demos as modeled MetaMesh witnesses without bypassing their prerequisites or cleanup boundaries.
---

# Use Meta-BI demos

Use this skill when reproducing product behavior, testing a CLI workflow, or
choosing an integration witness in `meta-bi/Demos`.

## Select the witness

Read [references/demos.md](references/demos.md) and choose the smallest demo
that crosses the affected semantic boundary. Use AdventureWorks when the task
requires a complete system rather than a focused CLI witness.

Each maintained demo has a README and usually one `.MetaMesh` workspace. The
mesh is the executable construction record; checked-in workspaces are its
reviewable results.

## Inspect first

From the `meta-bi` repository root:

```powershell
meta-mesh show --workspace <demo-mesh-path>
meta-mesh workspaces --workspace <demo-mesh-path>
meta-mesh operations --workspace <demo-mesh-path>
meta-mesh steps --operation <name> --workspace <demo-mesh-path>
meta-mesh validate --operation <name> --workspace <demo-mesh-path>
```

Missing workspaces may be declared outputs rather than damage. Inspect the
operation before deciding they should already exist.

## Check the external boundary

Read the README and selected steps for:

- required CLI builds and `PATH` entries;
- SQL Server or Analysis Services instances;
- connection-string environment variable names;
- permissions to create, deploy, process, restore, or drop objects;
- tracked expected outputs and transient run evidence;
- cleanup operations and exactly what they remove.

Do not substitute credentials, install servers, run cleanup, or deploy merely
because the demo declares those steps. Match the authorization of the current
task.

## Run and inspect

Run the named operation through MetaMesh rather than copying its child commands:

```powershell
meta-mesh run --operation <name> --workspace <demo-mesh-path>
```

Use the owning product CLI and generic `meta` inspection commands to examine
the resulting workspaces. For database demos, inspect both modeled output and
the verification operation; a successful process exit is not the complete
witness.

After execution, review `git status --short`. Distinguish expected regenerated
artifacts from transient database or run evidence, and never commit incidental
environment output.
