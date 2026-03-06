# Architecture Curation

## Purpose

This note explains how the sanctioned `Architecture` model is being curated.

The goal is not to adopt TOGAF, ArchiMate, or any other external framework wholesale. The goal is to pull a small, useful set of architecture concepts into `meta` so the project can model architecture in its own terms.

## Source material used

Primary input used for this pass:

- ArchiMate 101, Open Group ArchiMate community:
  - active structure, behavior, passive structure, relationships, viewpoints
  - https://archimate-community.pages.opengroup.org/workgroups/archimate-101/
- TOGAF architecture content/view language:
  - architectures represented as models, views, and artifacts
  - https://www.opengroup.org/architecture/tools-arch-dev.htm
- TOGAF template deliverables note:
  - deliverables and artifacts as architecture work products
  - https://help.opengroup.org/hc/en-us/articles/21726647171730-Are-There-Any-Template-Deliverables-for-the-TOGAF-Standard

## What was taken from ArchiMate

Useful ideas pulled from ArchiMate:

- architecture is made of elements and relationships
- relationships are typed and meaningful, not just loose links
- viewpoints and views are first-class architecture concepts
- constraints/motivation exist, but they should be kept small at first

Especially useful from the ArchiMate generic metamodel:

- active structure, behavior, and passive structure as generic categories
- relationship families such as composition, aggregation, assignment, realization, serving, access, triggering, flow, and association

For this pass, those ideas are used as a vocabulary source, not copied directly into the model as fixed enum values.

## What was taken from TOGAF

Useful ideas pulled from TOGAF:

- architecture work is expressed through models, views, and artifacts
- architecture artifacts are not just runtime outputs; they are also work products that communicate architecture
- architecture descriptions need viewpoints so different stakeholders can look at the same architecture from different angles

TOGAF is being used here mainly as a reminder that architecture is not only a graph of components. It also needs representations.

## What was deliberately not taken

Not adopted in this pass:

- TOGAF ADM phases
- TOGAF deliverable process machinery
- ArchiMate layer taxonomy as a hardcoded model rule
- ArchiMate full element catalog
- implementation/migration roadmap concepts
- framework-specific entities such as `MetaModel`, `MetaTool`, `Weave`, or `ArtifactKind`

Those may appear later as instance data or as separate sanctioned models, but they should not be hardcoded into the generic `Architecture` model itself.

## Current curated model direction

The current `Architecture` model keeps only generic concepts:

- `ElementType`
- `Element`
- `RelationshipType`
- `ElementRelationship`
- `Viewpoint`
- `View`
- `ViewElement`
- `ViewElementRelationship`
- `Constraint`
- `ConstraintElement`

This means:

- a model, tool, command, weave, or artifact kind is not a first-class entity in the metamodel
- instead, those will later be represented as `Element` rows classified by `ElementType`
- architecture structure emerges from typed relationships between elements

## Why this is a better fit for now

The earlier draft was really a model of the current `meta` family inventory.

That was too specific. A sanctioned `Architecture` model should be generic enough to describe:

- this framework
- another framework
- or a future architecture we have not named yet

Using generic elements and typed relationships keeps the model open while still giving it structure.

## Open questions

Still unresolved:

- whether `Constraint` is enough, or if motivation concepts such as requirement/principle/capability are needed
- whether `View` and `Viewpoint` are enough, or if broader architecture work-product concepts are needed
- whether element typing should stay fully instance-driven, or whether a small number of sanctioned built-in architecture element classes should exist
- how runtime/deployable artifacts should relate to architecture representations

These should be solved after the generic structure is reviewed, not before.
