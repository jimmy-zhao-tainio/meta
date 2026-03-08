# Weave and Fabric Note

## Purpose

This note records what is real, what is inferred, and what should not be overclaimed when discussing `MetaWeave` and any future move toward a broader "fabric" concept.

## What is established by research

### 1. Weaving is a real model-engineering concept

In model-driven engineering, weaving models are used to connect elements across contributing models.

The EMF Views documentation describes a weaving model as a root model that contains contributing models and virtual links. Contributing models hold the concrete elements being connected, and virtual links describe modifications or cross-model links that appear in the resulting view.

This confirms that:

- a weave can be modeled explicitly
- a weave can connect multiple contributing models
- weaving is a legitimate foundational concept in model engineering

It does **not** by itself define parent-scoped consistency semantics.

### 2. Data fabric is a broader architectural concept

The current mainstream use of "data fabric" is architectural, not just relational binding.

IBM describes data fabric as an architectural approach for integrated, end-to-end data management using metadata, integration, governance, and unified access across heterogeneous systems.

Gartner describes data fabric as a metadata-driven design and implementation approach for data integration and data management.

Microsoft Fabric is not a model-weaving product, but Microsoft documents it as an end-to-end analytics platform with multiple workloads operating over shared platform services and shared storage.

This confirms that "fabric" in mature usage implies something broader than pairwise links:

- multiple connected components
- shared platform semantics
- common services or shared substrate
- end-to-end integration rather than isolated mappings

## What these sources do **not** prove

The sources above do **not** prove that model weaving must become "fabric".

They also do **not** prove a ready-made metamodel for scoped or hierarchical bindings.

So we should not claim:

- that Microsoft Fabric validates a specific `MetaWeave` design
- that data-fabric literature gives us a direct metamodel for parent-scoped bindings
- that current model-weaving literature already solves our exact consistency problem

## What is an internal inference from our own models

The following is not copied from a source. It is an internal architectural inference from the current sanctioned models.

Our current `MetaWeave` model supports flat property bindings:

- source model/entity/property
- target model/entity/property

That is enough for direct value equivalence checks such as:

- `BusinessHub.Name` -> `BusinessObject.Name`
- `BusinessLink.Name` -> `BusinessRelationship.Name`

It is **not** enough for child rows whose validity depends on a bound parent context.

Examples:

- a `BusinessHubKeyPart` should match a `BusinessKeyPart` within the correct parent hub/key pair
- a `BusinessLinkEnd` should match a `BusinessRelationshipParticipant` within the correct parent link/relationship pair

This is the parent-scoped binding problem.

So the current design pressure is real:

- the existing weave concept is valid
- but flat property bindings are too weak for some cross-model consistency problems

That conclusion comes from our model structure, not from the external sources themselves.

## Practical distinction for this project

### Weave

Use "weave" for:

- explicit cross-model bindings
- direct, inspectable, sanctioned mappings
- checks and materialization over those mappings

This is what `MetaWeave` does today.

### Fabric

Use "fabric" only if the foundational layer grows to support a broader connected system of bindings and shared semantics, for example:

- parent-scoped bindings
- grouped bindings with context
- transitive consistency across several sanctioned models
- shared services for lineage, governance, discovery, or materialization across many model families

That would be a real semantic step up from flat weaving.

## Current sanctioned next step

The next foundational artifact is `MetaFabric`, a sanctioned model for scoped binding over weave workspaces only.

It does not replace `MetaWeave`. It composes `MetaWeave` workspaces and adds parent-scoped binding requirements where flat property bindings are not enough.

The corresponding CLI is `meta-fabric`, starting with `init` and `check` over sanctioned fabric workspaces.

See `docs/META-FABRIC-BOUNDARY.md`, `MetaFabric.Workspaces/MetaFabric`, and `MetaFabric.Workspaces/Fabric-Scoped-Group-CategoryItem`.

## Current recommendation

Do not rename `MetaWeave` yet.

Instead:

1. keep `MetaWeave` as the current foundational flat-binding tool
2. explicitly acknowledge that parent-scoped binding is a real missing capability
3. only introduce a broader "fabric" concept if the foundational layer grows beyond flat bindings into a genuinely connected, metadata-driven integration substrate

That keeps the language honest.

## Sources

- EMF Views User Guide: <https://www.atlanmod.org/emfviews/manual/user.html>
- EMF Views overview: <https://www.atlanmod.org/emfviews/>
- IBM, What is a Data Fabric?: <https://www.ibm.com/topics/data-fabric>
- IBM Data Fabric architecture guide: <https://www.ibm.com/architectures/hybrid/data-fabric>
- Gartner, What is Data Fabric?: <https://www.gartner.com/en/data-analytics/topics/data-fabric>
- Microsoft Learn, What is Microsoft Fabric?: <https://learn.microsoft.com/en-us/fabric/fundamentals/microsoft-fabric-overview>
- Microsoft Learn, Microsoft Fabric overview: <https://learn.microsoft.com/en-us/fabric/get-started/microsoft-fabric-overview>

