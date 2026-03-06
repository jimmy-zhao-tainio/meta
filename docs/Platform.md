# BI Platform

## Purpose

This document defines the capability surface of the future user-facing BI platform model.

It is not a final metadata model. It is a platform-level description of what a fully automated BI system must be able to do, what concerns it must cover, and where the eventual modeling work needs to focus.

The goal is to make the platform visible as a coherent system before deciding exact model shapes.

Companion workspace model: [`Architecture.Workspace/metadata/model.xml`](../Architecture.Workspace/metadata/model.xml)

## Scope

In scope:

- full BI platform automation
- source onboarding
- schema and type understanding
- ingestion and landing
- transformation and integration
- persistent analytical storage
- semantic serving
- orchestration and operations
- deployment and environment management
- security
- data quality
- observability, diagnostics, resilience, and recovery

Out of scope:

- reports
- dashboards
- report authoring
- presentation-layer layout concerns

The semantic layer is in scope. Reporting that consumes the semantic layer is not.

## Why capabilities first

At this stage, thinking in terms of capabilities is more useful than thinking in terms of hundreds of individual platform items.

Capabilities make it easier to answer:

- what the platform must actually do
- what concerns are cross-cutting
- what belongs in reusable sanctioned models
- what belongs in the final user system model

Item-level metadata can come later. The platform shape needs to be clear first.

## Platform capabilities

### 1. Platform definition and governance

The platform must define what system is being built, who owns it, what subject areas it serves, and what policies govern it.

This capability includes:

- system identity and purpose
- business domains and subject areas
- ownership and accountability
- environment boundaries
- release boundaries
- governance policies

This is the control surface for the platform as a whole.

### 2. Source onboarding

The platform must be able to declare and manage the source landscape it depends on.

This capability includes:

- source systems
- source endpoints
- selected source objects
- source structure
- source relationships when the source truly carries them
- source grain and identity expectations
- extraction contracts and watermarks

This is where a BI system says what it consumes and under what acquisition rules.

### 3. Structural and type understanding

The platform must understand source structure and bind source types to sanctioned type meaning.

This capability includes:

- extracted structural metadata
- sanctioned type ownership
- source type descriptions
- conversion intent
- conversion implementation identity

This is the point where raw source structure becomes governable metadata rather than just technical trivia.

### 4. Ingestion and landing

The platform must support repeatable data acquisition into controlled landing structures.

This capability includes:

- landing contracts
- batch and load-window handling
- arrival expectations
- retained landing identity
- retention behavior for landed data and traces

This is the platform boundary where source data first becomes managed platform data.

### 5. Transformation and integration

The platform must describe how incoming data is reshaped, standardized, joined, derived, and prepared for persistent analytical structures.

This capability includes:

- transformation intent
- row-shape changes
- derivations
- joins and filters
- normalization rules
- business rules
- technical keying where needed downstream

This is where data starts to take on platform meaning rather than source-system meaning.

### 6. Historical integration storage

The platform must support integrated historical storage, not just one-step staging or one-step warehouse loading.

This capability includes:

- raw vault structures
- business vault structures
- temporal behavior
- audit behavior
- helper structures such as PIT and bridge
- reference data structures where they belong

This is the persistent integration core of the BI platform.

### 7. Analytical storage and serving

The platform must support consumption-oriented analytical structures and the semantic layer above them.

This capability includes:

- warehouse-serving structures
- dimensions and facts
- conformed structures
- aggregates and partitions
- semantic models
- semantic relationships
- measures and hierarchies
- semantic processing behavior

This is how integrated platform data becomes usable for analysis.

### 8. Orchestration and execution control

The platform must be able to describe, schedule, coordinate, and control its own work.

This capability includes:

- pipelines and tasks
- dependencies
- schedules
- retries and timeouts
- concurrency rules
- environment bindings
- execution groupings

This is the operational control layer for getting the platform to run in a repeatable way.

### 9. Deployment and environment management

The platform must not stop at design-time metadata. It must describe how technical artifacts move across environments and how change is applied safely.

This capability includes:

- deployable artifact boundaries
- environment-specific bindings
- versioning
- release movement across dev, QA, pre-prod, and prod
- rollback strategy
- post-deploy actions

This is where the platform stops being design-only and becomes operationally real.

### 10. Operations, observability, and diagnostics

The platform must be able to observe itself and support diagnosis when behavior is wrong or degraded.

This capability includes:

- run history
- execution state
- metrics
- alerts
- incidents
- lineage evidence
- diagnostic tooling hooks
- SLA-oriented monitoring

This is also where platform health concerns must be represented explicitly, including:

- monitoring
- performance visibility
- database health
- indexing strategy
- query plan diagnostics

A full BI system is not complete if it can build artifacts but cannot explain what happened when they run.

### 11. Resilience and recovery

The platform must be able to survive failures and support controlled recovery.

This capability includes:

- error resistance
- restart behavior
- partial-failure handling
- backup strategy
- restore strategy
- rollback strategy
- degraded execution modes where they are intentionally supported

This should include cases such as partial processing behavior in downstream engines where partial completion is a real operational concern.

### 12. Security and data quality

The platform must govern who can access what and must define what "acceptable data" means.

This capability includes:

- principals, roles, and permissions
- environment and data access controls
- secret references
- quality dimensions
- quality rules and expectations
- quality results and issues
- exception handling policies

This is where trust is governed, both in access and in data itself.

## How the platform operates

A fully automated BI platform operates as one connected system:

1. define the platform, its subject areas, and its environments
2. onboard source systems and declare what to extract
3. understand source structure and bind source types to sanctioned type meaning
4. ingest and land data under repeatable contracts
5. transform and integrate data into persistent historical structures
6. shape data for warehouse and semantic serving
7. orchestrate execution across all platform work
8. deploy artifacts safely across environments
9. observe, diagnose, protect, and recover the platform while it runs

The important point is that this is one platform model problem, not a pile of disconnected tooling problems.

## Ownership lens

The hard boundary question is not solved yet, but the shape of the problem is clear.

Sanctioned models should own reusable, stable domains such as:

- source structure
- type ownership
- type conversion intent
- transformation intent
- weaving
- implementation-specific technical surfaces such as vault, warehouse, orchestration, and product artifacts

The future user `BISystemModel` should own:

- one concrete BI platform
- its subject areas and business-facing structure
- composition of sanctioned models
- system-specific bindings, intent, and operating rules

So the likely direction is neither:

- one monolithic predetermined product model

nor:

- a loose pile of disconnected sanctioned models with no top-level system identity

The likely direction is a composed system: reusable sanctioned models underneath, one user-authored platform model above them.

## What this means for future modeling

Before deciding exact model shapes, future work needs to answer:

- which capability is being modeled
- whether that capability is reusable across systems or system-specific
- whether it belongs in a sanctioned model or in the user platform model
- what runtime artifacts it affects
- what operational evidence it must emit
- what resilience, security, and environment concerns attach to it

That is the point of this document. It is the capability map for the platform we intend users to build.
