---
name: use-meta-transform-flow
description: Operate the Meta-BI schema, TransformScript, Binding, Data Quality, Pipeline, and Orchestration path with each product retaining its semantics.
---

# Use the Meta transform flow

Use this skill for modeled SQL transforms from schema discovery through
analysis and execution. The products form a semantic path, but they do not
collapse into one workspace or one owner.

## Keep the boundaries explicit

```text
MetaSchema source and target contracts
                 +
       MetaTransformScript
                 |
                 v
       MetaTransformBinding

MetaTransformScript (+ Binding gate) -> MetaDataQuality
MetaTransformScript + Binding        -> MetaPipeline
MetaPipeline + Binding effects       -> MetaOrchestration
```

- TransformScript owns modeled T-SQL syntax, navigation, import, and emission.
- Binding resolves reads, writes, identities, shapes, and types against current
  schema contracts. It is derived output; regenerate it whenever script or
  schema inputs change.
- Data Quality owns transform analysis, corpus inference, findings, promotion,
  and quality candidates.
- Pipeline owns task execution, streaming, batching, and task evidence.
- Orchestration owns cross-pipeline effects, dependencies, locks, run plans,
  and coordinated execution.

Do not recreate Binding in a consumer, use SQL text when typed structure exists,
or add a freshness protocol around a deliberately stale Binding workspace.

## Discover and import

Inspect exact syntax first:

```powershell
meta-schema help extract sqlserver
meta-transform-script help from sql-file
meta-transform-script help from sql-files
meta-transform-script help from sql-code
```

Bare `SELECT` imports require an explicit target identifier; CREATE VIEW carries
its own target. Mutations and stored-procedure contracts follow their documented
command rules. Preserve authored SQL spelling where the supported contract does
so, and fail explicitly on unsupported syntax rather than storing opaque text.

## Bind

Use `meta-transform-binding bind` with the complete transform workspace, one or
more source MetaSchema workspaces, the target MetaSchema workspace, execution
system, and type-conversion policy when required. Strict binding is the normal
proof. Use partial binding only when the task explicitly wants a reviewed skip
report.

Inspect the resulting Binding workspace as semantic evidence. Never pair it
with independently modified script or schema workspaces.

## Analyze and execute

- Use `meta-data-quality from-transform-workspace`, `inspect`, and `promote` for
  quality discovery and selection. Convert promoted candidates through the
  sanctioned DataQuality-to-MetaSql path when deployable SQL is required.
- Use `meta-pipeline` to create pipelines, add transform-backed tasks, inspect,
  and execute them. Operational databases carry runtime evidence, not model
  truth.
- Use `meta-orchestration create`, inspection, issue explanation, run-plan
  refresh, and execution for multiple pipelines and shared object effects.

Before constructing a workflow manually, inspect these maintained witnesses:

- `Demos/MetaTransformBindingCliIntegration` for import and binding;
- `Demos/MetaDataQualityPhase2ABCliIntegration` for offline DQ inference;
- `Demos/MetaPipelineSqlServerCliIntegration` for runtime execution;
- `Demos/MetaOrchestrationCliIntegration` for the complete dependency path;
- `Demos/AdventureWorksBiStackDemo` for 120 transforms across three pipelines.

## Verify the affected boundary

Use focused semantic tests plus the closest integration mesh. Parser changes
need SQL-model round trips; Binding changes need resolved identity/type/write
evidence; Pipeline changes need row-stream and operational witnesses;
Orchestration changes need graph and run-plan witnesses. Do not substitute a
success message for inspection of the produced workspace.
