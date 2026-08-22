---
name: build-meta-bi-stack
description: Design and construct a bespoke end-to-end Meta-BI stack from business requirements, source evidence, reusable models, transformations, quality, runtime, and analytics.
---

# Build a Meta-BI stack

Use this skill when an agent is asked to turn business knowledge, requirements,
schema, and available data evidence into a working BI system. This is a system
composition task, not a requirement to use every Meta-BI product.

Read these supporting skills as their boundaries become active:

- [Work in Meta-BI](../work-in-meta-bi/SKILL.md)
- [Author Meta models](../author-meta-models/SKILL.md)
- [Use the transform flow](../use-meta-transform-flow/SKILL.md)
- [Use MetaConvert](../use-meta-conversions/SKILL.md)
- [Use MetaWeave](../use-meta-weave/SKILL.md)
- [Use MetaMesh](../use-meta-mesh/SKILL.md)
- [Use Meta-BI demos](../use-meta-bi-demos/SKILL.md)

## Start from evidence and reusable knowledge

Inputs may include:

- business language, goals, rules, measures, decisions, and security needs;
- source schemas, data profiles, sample data, contracts, and operational limits;
- prepared model workspaces, implementation policies, transform patterns, and
  sanctioned weaves already present in the repositories;
- external runtime and deployment constraints.

Do not begin from a blank architectural template when reusable modeled knowledge
already fits. Select and compose prepared assets, then add the business-specific
concepts and mappings required by the evidence.

The reusable catalogue is not yet one finished service. Search sanctioned
workspaces, implementation policies, transform patterns, weaves, and demos in
the repositories instead of assuming no prior model exists.

Keep unresolved meaning visible. Ask the user about genuinely ambiguous grain,
business identity, history, derivation, ownership, or quality rules instead of
turning guesses into sanctioned models.

When business knowledge or requirements are carried in bespoke customer
models, keep those models customer-specific and map their explicit concepts
into Meta-BI product workspaces through suitable correspondence. Do not flatten
them into prose bags or pretend a generic product model already contains the
customer's meaning.

## Select the stack from the requirements

Choose only the layers justified by the system:

- MetaSchema for source and target contracts;
- Raw and Business Data Vault when source-history preservation and curated
  business integration require them;
- MetaDataWarehouse for dimensional delivery and conformed analytical grain;
- MetaTransformPattern and MetaTransformScript for repeated and bespoke SQL
  transformations;
- MetaTransformBinding for compile-time resolution and validation;
- MetaDataQuality for analysis, findings, promotion, and executable checks;
- MetaPipeline for task execution and streaming evidence;
- MetaOrchestration for cross-pipeline dependencies, locks, and run plans;
- MetaAnalytics plus Tabular or Multidimensional realization when an analytical
  semantic model is required.

Do not insert a layer because a reference demo contains it. Equally, do not
collapse layers whose separate contracts carry required meaning.

## Define the workspace map

Before authoring hundreds of commands, write down the participating workspaces
and their handoffs. Distinguish:

- external or extracted source contracts;
- authored business and analytical models;
- reusable definitions, implementation policies, and option workspaces;
- generated conversion targets;
- transforms and their derived Binding workspaces;
- execution, orchestration, and quality workspaces;
- deployed artifacts and operational evidence.

Create a MetaMesh workspace that gives each participant a stable name and
surface location. Organize operations around outcomes that can be reviewed and
repeated. The mesh coordinates the stack; it does not absorb the models.

## Construct each semantic boundary

1. Discover or adapt each source into a reviewable MetaSchema workspace.
2. Author business models through their owning CLIs, preserving grain,
   identity, relationships, history, and ordering explicitly.
3. Use direct sanctioned MetaConvert paths for structural transformations.
   Use or author MetaWeave correspondence from bespoke models where the mapping
   is a model transformation. Use authored transform mappings where movement
   between data layers requires business judgment.
4. Reuse transform patterns for repeated SQL shape, materialize their instances
   to MetaSqlScript, and import through the existing MetaTransformScript parser.
   Author exceptional transforms directly in the supported T-SQL contract.
5. Bind every executable transform boundary against its current source and
   target MetaSchema contracts before placing it in a pipeline.
6. Analyze modeled transforms for Data Quality evidence and promote checks that
   match the required assurance policy.
7. Build pipelines from validated transforms and infer or author orchestration
   across pipelines from their modeled effects.
8. Author portable analytics from the delivered warehouse contract, then use a
   sanctioned realization path for the chosen analytical engine.

A direct workspace path remains valid even when another route could be composed
through intermediate models. Do not turn the stack into an academic conversion
exercise.

## Prove the stack as it grows

Keep every handoff inspectable. At each operation, inspect the produced
workspace, run the owning validation or binding proof, and preserve explicit
diagnostics. Do not postpone all reconciliation until the end.

Use small semantic witnesses while authoring, then system witnesses for the
assembled stack:

- source-to-target counts and business totals;
- transform binding and target-shape evidence;
- promoted Data Quality checks and findings;
- task and pipeline execution evidence;
- orchestration graph and run-plan inspection;
- analytical measures reconciled to relational results.

Meta workspaces carry structural scale such as thousands of transforms; they do
not carry terabytes of fact data. MetaPipeline must stream large data volumes
with memory bounded by its working batch behavior.

## Use AdventureWorks appropriately

`Demos/AdventureWorksBiStackDemo` is the complete executable reference for how
an agent used public CLIs and a MetaMesh workspace to construct and verify a BI
stack. Inspect its 27 workspace declarations and 29 operations for mechanics.
Do not copy its Data Vault, warehouse grain, measures, or target architecture
into a different business problem without matching evidence. It is a system
witness, not the scale proof or universal template.

## Completion evidence

A constructed stack should leave:

- authored requirements and unresolved questions visible to the user;
- one authoritative workspace for every selected model;
- reproducible MetaMesh operations for construction and verification;
- strictly bound transforms before execution;
- reviewable quality, pipeline, orchestration, and analytical evidence where
  those products are used;
- deployment steps separated from semantic model construction and executed only
  with the required authority.
