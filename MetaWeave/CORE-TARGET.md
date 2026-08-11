# MetaWeave Core Target Architecture

## Status And Scope

This document defines the formal target for MetaWeave Core. It is a semantic
correspondence system between modeled workspace states. It is not a description
of the current implementation and it does not define command-line behavior,
workspace persistence, or physical artifact integration.

The target is intended to be strong enough that future artifact adapters can
remain mechanical. An adapter may read, write, or execute an artifact contract.
It may not decide how a domain workspace corresponds to that contract.

## Current Repository Facts

The following facts describe the repository at the time this target was
written. They are evidence, not compatibility constraints.

- A neutral workspace is represented as a model and an instance graph.
- `Meta.Operations` provides representation-neutral workspace state, reads,
  validation, constraints, and mutation operations.
- `Meta.TypedModels` maps typed CLR objects to and from neutral workspace state.
- Workspace surfaces own persistence and publication. `Meta.Integration` owns
  surface selection and cross-surface composition.
- The current MetaWeave model contains model references with workspace paths and
  property bindings between named source and target properties.
- The current MetaWeave Core opens referenced workspaces, performs naming-based
  suggestions, merges workspace state, and converts selected `...Id` properties
  into relationships.
- The current CLI exposes authoring, suggestion, validation, and materialization
  around that narrow behavior.
- The current tests establish the behavior of those path-bound bindings and
  heuristics. They do not establish general bidirectional correspondence laws.

The current path ownership, naming heuristics, workspace merging, and
property-to-relationship special cases are accidental constraints. The target
architecture does not preserve them.

## Formal Vocabulary

### Workspace

A workspace is:

```text
W = (M, I)
```

where:

- `M` is a model contract containing entity, property, relationship, identity,
  requiredness, cardinality, ordering, and value presence, content, and
  provenance semantics that the model can express.
- `I` is an instance graph conforming to `M`.

`Valid(W)` means that the model is internally valid and the instance graph
conforms to it, including identity uniqueness, required values, referential
integrity, cardinality, and modeled ordering constraints.

The source workspace is `S = (M_S, I_S)`. The target workspace is
`T = (M_T, I_T)`.

### Correspondence

`K` is a declarative correspondence definition between `M_S` and `M_T`. It
contains the rules, domains, loss declarations, canonicalization references,
and extension contracts needed to relate the two modeled worlds.

`K` is data. It contains no workspace locations, persistence choices, physical
artifact details, or hidden executable scripts.

### Directional Application

The forward application is a function:

```text
F_K : D_F -> W_T
```

where `D_F` is an explicitly defined subset of valid source workspaces. The
reverse application is:

```text
G_K : D_G -> W_S
```

where `D_G` is an explicitly defined subset of valid target workspaces.

A direction is total when its domain contains every valid workspace conforming
to its bound input model contract. A direction is partial when its domain has
additional modeled preconditions. A direction that is not defined has no
executable plan.

Applying a partial direction outside its domain returns a structured failure
and no output workspace. It does not guess, repair, or return a best-effort
result.

### Recovery-Claim Domains

Directional executability and recovery are separate claims. A source recovery
claim has an explicit domain `R_S`. A target recovery claim has an explicit
domain `R_T`.

Source recovery requires:

```text
R_S subset-of D_F
F_K(R_S) subset-of D_G
```

Target recovery requires:

```text
R_T subset-of D_G
G_K(R_T) subset-of D_F
```

The subset obligations are part of the claim. For an input in `R_S`, a forward
result outside `D_G` refutes source recovery. For an input in `R_T`, a reverse
result outside `D_F` refutes target recovery. The opposite direction's domain
cannot make a recovery law vacuously inapplicable.

Total source recovery means `R_S` contains every valid workspace conforming to
the exact source model contract. Total target recovery means `R_T` contains
every valid workspace conforming to the exact target model contract.

### Semantic Equivalence And Canonical-State Equality

`≈M` is model-bound semantic equivalence over workspaces conforming to `M`.
`≈S` and `≈T` are its source and target instances. It is reflexive, symmetric,
and transitive. It compares modeled meaning while allowing only representation
differences that the model contract explicitly declares semantically
irrelevant.

The model contract supplies those equivalence rules as executable semantic
contracts. If it declares none, semantic equivalence has no permission to ignore
a represented state difference and therefore coincides with state equality.

`≡M` is model-bound extensional workspace-state equality. `≡S` and `≡T` are its
source and target instances. It requires:

- the same exact model signature;
- the same record identities and entity membership;
- the same value presence, present content, and represented provenance;
- the same relationship targets;
- the same significant ordering;
- the same canonical representation choices carried by the model.

State equality may ignore object references and incidental enumeration order
for model-declared unordered collections. It does not ignore identifier
spelling, a declared representation choice, or any other state that a
canonicalizer is expected to settle.

State equality implies semantic equivalence:

```text
W1 ≡M W2  =>  W1 ≈M W2
```

The converse need not hold. Two semantically equivalent workspaces may use
different noncanonical spellings or other explicitly equivalent
representations. Ordinary object equality is also insufficient because it can
distinguish object identity or harmless unordered container layout.

### Canonicalization

`C_S : W_S -> W_S` and `C_T : W_T -> W_T` are total, deterministic,
semantics-preserving canonicalization functions over valid workspaces. They
must preserve meaning and reach an extensionally stable state:

```text
C_S(S) ≈S S
C_T(T) ≈T T

C_S(C_S(S)) ≡S C_S(S)
C_T(C_T(T)) ≡T C_T(T)
```

A canonicalizer that claims one normal form per semantic equivalence class must
also establish:

```text
S1 ≈S S2  =>  C_S(S1) ≡S C_S(S2)
T1 ≈T T2  =>  C_T(T1) ≡T C_T(T2)
```

A deterministic normalization step that lacks this uniqueness law is a
normalizer, not a canonicalizer suitable for canonical round-trip claims.

Canonicalization may select one representation among explicitly semantically
equivalent forms. It may not conceal information loss. If a rule drops or
conflates modeled information, that behavior is loss, not canonicalization.

Model-owned canonicalization describes equivalences intrinsic to one model.
Correspondence-owned canonicalization describes representation choices created
by `K`. These contracts remain distinct even when a compiled plan composes
them.

## Core Mission

MetaWeave Core owns the meaning and execution of correspondences between two
workspace contracts.

It owns:

- validating model contracts needed by a correspondence;
- validating correspondence definitions;
- deriving directional, loss, and round-trip capabilities;
- compiling immutable directional plans;
- applying plans to neutral workspace state;
- validating candidate results;
- verifying round-trip claims;
- producing stable diagnostics, logical provenance, trace receipts, and loss
  evidence;
- comparing correspondence definitions across versions.

It does not own:

- locating, opening, creating, saving, or publishing workspaces;
- files, directories, databases, source code, formats, or connection strings;
- workspace surface selection or conversion;
- physical artifact readers, writers, deployment, or execution;
- command-line parsing or presentation;
- domain behavior hidden in hardcoded branches.

Core application is pure with respect to observable workspace state. Inputs are
not mutated. The output depends only on the input workspace, the validated plan,
and the explicitly supplied extension implementation bindings. Validation,
analysis, and compilation depend only on semantic extension contracts. Time,
randomness, environment variables, process state, network state, and ambient
registries cannot influence the result.

Trace delivery is an explicit observation channel around that pure semantic
evaluation. The state of a trace sink may affect only the trace outcome; it may
not affect the candidate workspace, loss detection, validation, or semantic
success.

## Target Correspondence Metamodel

The following concepts are the minimum semantic vocabulary. The eventual Meta
model should express alternatives as distinct entities and relationships, not
as a generic `Kind` property or an opaque payload.

### Correspondence Identity And Version

A `Correspondence` has a stable identity. A `CorrespondenceRevision` identifies
one immutable semantic definition. Compatibility between revisions is an
explicit relationship; it is not inferred from version text or record order.

A revision binds:

- one source model contract;
- one target model contract;
- directional domain declarations;
- record-set, record-construction, relationship, identity, and value rules;
- source and target recovery claims with explicit recovery domains;
- factored presence, content, and represented-provenance transitions;
- canonicalization contracts;
- extension contracts;
- explicit loss declarations.

Revision identity changes whenever executable semantics change. Descriptions
may change without changing semantic identity only when they are outside the
compiled contract digest.

### Model Contracts And Signatures

`SourceModelContract` and `TargetModelContract` bind `K` to model signatures.
A signature is a deterministic digest of the complete semantic model contract,
paired with the signature algorithm identity. A model name or version label is
diagnostic information, not sufficient compatibility evidence.

The first implementation should require exact signatures. A future compatible
signature set may enumerate additional accepted signatures, but compatibility
must never be guessed from similar names or a subset of matching fields.

The compiled plan embeds both expected signatures. Execution rejects a
workspace whose model signature does not match the plan.

### Endpoint References

Correspondence rules refer to model elements through validated semantic
references:

- source and target entity endpoints;
- source and target identity endpoints;
- source and target property endpoints;
- source and target relationship endpoints.

An endpoint reference contains the bound model signature and stable model
element identity. Human-readable names support diagnostics but are not resolved
heuristically at execution time.

An endpoint value is not merely a property or relationship name. It pairs one
endpoint reference with a `RecordVariable` visible in the current lexical scope.
This identifies the exact record from which the member is read. Compilation
rejects an endpoint value whose variable is out of scope or bound to a different
entity contract.

### Entity And Record Correspondence

An `EntityCorrespondence` relates source records to target records. It declares:

- participating source and target entities;
- its record construction and decomposition rules;
- source-to-target and target-to-source cardinality facts;
- identity derivation in each executable direction;
- domain constraints;
- source and target coverage;
- dependencies on other entity correspondences.

Cardinality alternatives are modeled facts, such as one-to-one, one-to-many,
many-to-one, and many-to-many. They are not values in a free-text category
property. Exactly one applicable cardinality declaration must exist for each
direction.

These cardinalities are derived against the compiled record-set and constructor
algebra below. A declaration unsupported by that algebra is invalid. A reverse
direction is not available merely because forward records can be enumerated.

### Record Variables And Scopes

Every directional definition has a root `DirectionScope`. A `RecordVariable`
has stable identity, is bound to one exact entity endpoint, and is introduced by
a record-set rule. A scope exposes its own variables and the variables of its
ancestors. Variables in a child scope are not visible to parents or siblings.

Here, typed means statically bound to exact model entities, members, and binding
shapes. It does not introduce a host-language type system into MetaWeave.

Nested scopes are introduced only by modeled constructs:

- relationship traversal;
- correlation;
- grouping and group-member access;
- fixed correspondence branches.

Each scope has one binding shape: an ordered set of record variables and, where
applicable, one group variable or branch variable. Variable ordering is part of
the compiled shape, while semantic record ordering remains governed by the
model contract.

An execution binding assigns exactly one record to each visible record variable.
A group binding additionally carries a grouping key and the complete member
binding set. Rules consume bindings, not ambient current records.

### Closed Record-Set Algebra

`RecordSet` is an identity hub. Exactly one distinct modeled variant entity must
describe each record set. It is not a generic operation node with an operation
name or payload.

The first target contains these variants:

| Modeled variant | Input | Output binding semantics |
| --- | --- | --- |
| `EntityRecords` | An exact entity endpoint | Introduces one record variable and yields one binding for every record of that entity |
| `SelectedRecords` | One record set and a closed domain predicate | Retains only bindings for which the predicate is true; unresolved predicate results are failures, not false |
| `TraversedRecords` | One record set, an in-scope record variable, and an exact relationship endpoint | Introduces the related record variable and yields one binding per related record; an absent optional relationship yields none |
| `CorrelatedRecords` | Two record sets and a closed equality or relationship correlation | Combines bindings that satisfy the modeled correlation and exposes explicitly aliased variables in one child scope |
| `GroupedRecords` | One record set and an ordered tuple of group-key values | Introduces one group variable per extensionally equal key and retains the complete member binding set |
| `GroupMembers` | One in-scope group variable | Re-enters the group's member bindings in a child scope |
| `FixedBranchRecords` | One record set and a finite set of modeled branches | Yields one binding per matching branch and introduces the stable branch identity |

The variants have precise consequences:

- selection is the only implicit record-elimination operation;
- relationship traversal is dynamic fan-out over modeled graph edges;
- several record constructors or fixed branches provide finite declared fan-out;
- correlation is a modeled join and cannot fall back to equal-looking names;
- grouping equality uses the value contracts and presence semantics of all key
  values;
- group membership is explicit and cannot be accessed through a free variable.

Every fixed branch has stable identity and a closed predicate over its parent
scope. Disjointness and exhaustiveness are assessed exactly like discriminator
alternatives; an unproven opaque branch predicate cannot support a totality or
recovery claim without explicit evidence or assumptions.

Arbitrary collection expansion and arbitrary aggregation are not part of the
first closed algebra. They may be added later only as distinct modeled
constructs with finite-output, identity, order, state, and determinism
contracts. This restriction does not prevent one-to-many traversal,
many-to-one group construction, or many-to-many correlation from being modeled
with the variants above.

### Record Selection And Correlation

A selection predicate is built from the closed domain-constraint algebra. Every
value reference in the predicate identifies an in-scope record variable and an
exact model member. Predicate alternatives are distinct modeled entities, not
operator text.

The initial correlation algebra supports:

- equality between two value constructions;
- equality between a value and a group key;
- an already modeled relationship between two record variables.

The equality contract includes presence behavior. `Absent`, `Null`, and a
present value are not equal merely because an implementation uses the same host
sentinel for them. Correlation that requires domain-specific comparison uses a
named extension predicate contract and remains subject to its evidence.

### Grouping And Group Values

A `GroupedRecords` rule declares an ordered key tuple. `GroupKeyValue` exposes a
key member in the group scope. `GroupMembers` exposes the original member
bindings. Group identity is the extensionally equal key tuple, not first-row
identity or traversal order.

`GroupInvariantValue` may expose a member value only when all group members have
extensionally equal presence, content, and represented provenance for that
value. The condition is statically established from constraints or checked at
execution. A disagreement is `GroupInvariantViolation`.

A general aggregate is deliberately outside the first primitive algebra. A
future `AggregateFunctionContract` must define its member input contract, empty-
group behavior, ordering sensitivity, presence and provenance behavior,
determinism, and inverse or loss obligations. Until that contract exists, a
plan requiring a general aggregate cannot compile.

### Record Construction

A record constructor consumes one compiled record set and emits records of one
exact destination entity. It has one of two modeled forms:

- `PerBindingRecordConstruction` emits exactly one destination record for every
  input binding;
- `PerGroupRecordConstruction` emits exactly one destination record for every
  input group.

Every constructor contains exactly one destination identity construction, all
property assignments it owns, and all relationship assignments it owns. Each
assignment is evaluated in the constructor's binding scope. Every emitted
record therefore has a complete, inspectable source scope.

Multiple constructors over one input binding constitute fixed fan-out. A
traversal followed by a per-binding constructor constitutes data-dependent
fan-out. A grouped constructor constitutes many-to-one construction.
Correlation followed by construction can represent records dependent on both
sides of a join. These forms make one-to-one, one-to-many, many-to-one, and
many-to-many claims executable without an untyped query language.

Destination identity is mandatory for every constructor. Static analysis checks
whether identity construction is defined for every emitted binding. Execution
checks uniqueness across all constructors targeting the same entity. Duplicate
identity is a collision; constructors never merge records implicitly.

The dependency graph orders constructors whose relationship assignments require
records emitted by other constructors. Dependencies do not weaken final
referential-integrity validation.

### Record Decomposition

Decomposition is represented by record-set and constructor rules in the
opposite direction. It is not an imperative callback and is not inferred by
reversing a forward constructor.

For example, reverse decomposition of one input record into several destination
records uses several reverse constructors or fixed branches, each with its own
identity and assignments. Recovery analysis relates the forward and reverse
constructor graphs and checks the applicable recovery law over the explicit
recovery domain.

Static consumption analysis walks record-set leaves, predicates, group keys,
identity constructions, property assignments, and relationship assignments. It
therefore determines which record variables and model concepts are read,
constructed, omitted, or lost without observing runtime behavior.

### Record Identity Correspondence

A `RecordIdentityCorrespondence` defines how record identity is preserved or
constructed in each direction. It must establish:

- determinism;
- uniqueness within the destination entity;
- collision behavior;
- reverse recovery when reversibility is claimed;
- stable identity under repeated application;
- identity behavior for constructed child records;
- relationship target identity compatibility.

Identity cannot default to row order, generated counters, object addresses, or
incidental traversal order. If a destination identity combines multiple source
values, the combination function and inverse obligations are part of `K`.

### Property Correspondence

A `PropertyCorrespondence` assigns a value construction to a destination
property in one or both directions. Direct copying is one construction, not an
implicit fallback.

Forward and reverse assignments are separate modeled relationships to their
directional record constructors. Each assignment is evaluated in that
constructor's scope. The existence of both assignments establishes directional
executability only; their inverse relationship is assessed through a recovery
claim.

Every destination property that may be present must be accounted for by one of:

- a value construction;
- a modeled omission rule;
- a modeled default rule;
- a loss declaration that makes the direction incapable of reconstruction;
- a declaration that the property is outside the correspondence's output
  coverage.

Two rules may not assign the same destination property unless a modeled,
deterministic combination rule owns that assignment.

### Relationship Correspondence

A `RelationshipCorrespondence` maps a source relationship to a target
relationship using entity and identity correspondences. It declares:

- source and target relationship endpoints;
- the endpoint entity correspondences used to resolve each side;
- requiredness behavior;
- cardinality behavior;
- omission behavior for absent optional relationships;
- reverse behavior when available.

Forward and reverse relationship rules are independently scoped. A recovery
claim, not shared naming, determines whether they recover one another.

The executor never invents a relationship from property naming. A relationship
target must exist in the candidate result or be constructed by a dependency
that completes before validation.

### Construction And Decomposition

Value and identity transformation inside a record constructor use a closed,
scope-checked expression algebra represented as modeled graph data. The graph
has a `Value` identity hub and distinct variant entities for:

- an endpoint value paired with an in-scope record variable;
- a group-key value paired with an in-scope group variable;
- a group-invariant value paired with an in-scope group variable and member
  endpoint;
- a literal value with explicit presence, content, and provenance;
- a function result;
- a selected member of a constructed value.

The variant is determined by entity participation, not by a `Kind` string.
Validation requires every `Value` to participate in exactly one variant.

A `FunctionApplication` refers to a built-in primitive contract or a named
extension contract. `FunctionArgument` relationships connect input values.
Where argument order is semantic, `ArgumentPrecedence` relationships define a
strict total order for that application. No ordinal or zero-padded text is used
to smuggle sequence into scalar values.

A `FunctionResult` exposes named result members. Forward and reverse
assignments can therefore construct one value from many or decompose one value
into many without expression strings.

The minimum built-in algebra should include only operations whose semantics can
be specified completely, such as identity, constant, tuple construction,
tuple member selection, and explicit presence and provenance selection.
Domain-specific formatting, parsing, normalization, classification, or lookup
belongs in a named extension contract.

### Domain Constraints

Directional domains are modeled with a closed constraint algebra. At minimum it
must express:

- presence, present-content, and represented-provenance requirements;
- equality and membership constraints over modeled values;
- relationship presence;
- cardinality constraints;
- discriminator coverage;
- a named extension predicate when primitives are insufficient.

An extension predicate is subject to the same purity, identity, trust, and
diagnostic rules as an extension function. Its semantic definition comes from
`ExtensionContractCatalog`; execution uses a separately verified
`ExtensionImplementationBinding`. Arbitrary boolean expression text is not a
domain model.

If any valid input workspace can violate an additional directional constraint,
that direction is partial. Documentation cannot upgrade it to total.

### Requiredness And Coverage

Property and relationship requiredness are read from the bound model contract.
`K` does not copy them into mutable text fields.

Coverage declarations state which source concepts are consumed and which
target concepts are constructed. Alternatives are modeled separately:

- complete source coverage;
- selected source coverage;
- complete target construction coverage;
- selected target construction coverage.

Static analysis derives uncovered required targets, unused covered sources, and
unaccounted model elements. A complete forward constructor requires complete
target construction coverage. A complete reverse constructor requires complete
source construction coverage.

Selected coverage does not silently preserve pre-existing destination data.
Application constructs a new destination workspace. Contextual reconciliation
with an existing destination would require a separate, explicitly modeled
contract and is not part of this target.

### Value Presence, Content, And Provenance

Value state is factored rather than represented as one list of overlapping
alternatives.

Presence is:

```text
Absent
Null
Present(Content)
```

When presence is `Present`, provenance is:

```text
Explicit
Defaulted(DefaultRuleIdentity)
```

The first target attaches default provenance only to present values. A model
that requires provenance for null or absent state must model that fact
explicitly; Core does not overload `Null` or `Absent` to carry it.

Emptiness is not a presence state. It is a predicate defined by the endpoint's
content contract and applies only to `Present(Content)`. Text may define the
empty string as empty; a collection contract may define an empty collection;
many content contracts have no meaningful empty value.

A correspondence rule therefore declares independently:

- its transition for each reachable presence alternative;
- its transformation of present content;
- its behavior for content that the endpoint contract defines as empty;
- its transition for represented provenance.

Two alternatives may be equated only by explicit semantic equivalence,
canonicalization, or loss rules at the appropriate dimension.

In the current neutral workspace, an absent property key represents `Absent`.
A present string, including `""`, represents `Present(text, Explicit)`. The empty
string is present content. The current neutral state does not independently
represent `Null` or `Defaulted` provenance. MetaWeave may not claim to preserve
distinctions that an endpoint contract cannot carry. Such a distinction must be
excluded from the directional domain, modeled explicitly by the endpoint,
canonicalized under a valid model equivalence, or declared as loss.

Default rules have stable identity. Their identity participates in logical
provenance and in state equality whenever the endpoint model carries default
provenance. Absence does not become content that a text contract identifies as
empty. Null does not become defaulted content. A present empty string does not
become absent.

### Ordering And Dependencies

Ordering is modeled only when it contributes meaning.

- Ordered value arguments use precedence relationships.
- Ordered record collections require an explicit ordering correspondence.
- Evaluation dependencies form a directed acyclic graph of rules.
- Independent rules have no artificial total order.

Plan compilation rejects dependency cycles and ambiguous argument order. A
deterministic topological order may be selected for execution, but that
incidental order does not become model semantics.

### Discriminators And Polymorphism

General-purpose runtime type guessing is not required. A correspondence that
maps model variants must contain explicit discriminator rules.

Each discriminator alternative identifies a modeled domain constraint and a
destination construction. Capability analysis attempts to establish that
alternatives are disjoint where ambiguity would change output and exhaustive
wherever totality is claimed. If opaque predicates prevent that derivation, the
claim remains `Undetermined` unless checkable evidence or an explicit assumption
supports it. An otherwise valid input matching no alternative makes the
direction partial. An input matching conflicting alternatives is an execution
failure.

The first implementation need not support an open polymorphic expression
language. It must preserve room for modeled discriminator alternatives and
must not replace them with reflection or name inspection.

### Loss Declarations

Loss is a first-class correspondence fact. A loss declaration identifies:

- direction;
- affected source concept, presence alternative, content distinction, or
  provenance;
- the rule responsible;
- whether the loss always occurs or is conditional;
- the modeled condition for conditional loss;
- the information that cannot be recovered;
- the resulting capability restriction.

Distinct loss forms should be modeled separately, including dropped concepts,
many-to-one coalescing, default substitution, precision reduction, discarded
ordering, presence conflation, content conflation, and provenance loss. A
description may explain a declaration but cannot be its only semantics.

Execution reports each encountered conditional loss or a deterministic summary
with exact counts when detailed evidence is intentionally bounded. Static loss
that always applies is discoverable before execution.

### Extension Contracts And Implementation Bindings

`ExtensionContractCatalog` is immutable semantic input to validation, analysis,
and compilation. It contains `ExtensionFunctionContract` definitions with:

- stable identity and semantic revision;
- ordered input ports and named output ports;
- accepted presence, content, and represented-provenance contracts for every
  input;
- produced presence, content, and represented-provenance contracts for every
  output;
- domain constraints;
- purity and determinism requirements;
- declared failure diagnostics;
- forward and inverse identities when an inverse is claimed;
- structured claim evidence and explicit assumptions;
- a deterministic contract digest.

The contract catalog contains no executable delegate. A plan records every
required contract identity, semantic revision, and digest.

`ExtensionImplementationBindings` is separate runtime input. Each binding
contains:

- the exact extension contract identity and semantic digest implemented;
- stable implementation identity and revision or content digest;
- the executable binding;
- concurrency and purity declarations;
- host trust evidence and runtime verification evidence.

Execution requires exactly one compatible implementation binding for every
extension contract used by the plan. A matching semantic contract digest proves
only that the implementation claims the expected contract. It does not prove
that two implementations behave identically or that either implementation is
pure, deterministic, total, or invertible.

Execution evidence and determinism identity therefore include the exact
implementation binding identity and digest. Replacing an implementation binding
creates a distinct execution condition even when the semantic contract remains
unchanged.

An extension implementation may not inspect ambient state, mutate inputs, emit
unmodeled output, or select behavior by workspace location. Exceptions from an
extension are converted into structured execution diagnostics associated with
the function application and input record.

No contract declaration can mechanically prove arbitrary implementation code
pure or invertible. Trust and evidence are reported honestly.

### Canonicalization Contracts

A canonicalization contract identifies:

- its domain model signature;
- the representation freedoms it normalizes;
- a total deterministic canonicalization plan;
- its state-idempotence obligation under `≡M`;
- its semantics-preservation obligation;
- whether it claims a unique normal form for each `≈M` equivalence class;
- its structured assurance evidence and assumptions.

Correspondence-owned canonicalization may normalize only choices introduced or
recognized by `K`. Model-owned canonicalization is supplied as part of the model
contract environment. Compilation records the exact canonicalization contracts
used by a round-trip claim. A normalizer that cannot establish state idempotence
or unique normal form cannot support canonical-state recovery.

### Recovery Claims

`SourceRecoveryClaim` and `TargetRecoveryClaim` are distinct modeled concepts.
Each claim identifies:

- the two directional definitions involved;
- `R_S` or `R_T` through a closed domain contract;
- semantic recovery under `≈` or canonical-state recovery under `≡`;
- the canonicalizer required for canonical-state recovery;
- whether recovery is claimed for every valid endpoint workspace;
- any explicit assumptions or extension-contract dependencies.

The opposite-domain closure obligation is derived from these relationships and
cannot be disabled. Claim assessment records whether domain inclusion, closure,
and the recovery equation are established, refuted, or undetermined. Merely
having both directional definitions does not create a recovery claim.

## Correspondence Capabilities

Capabilities are derived by analysis. Authors do not assign a `Kind` and thereby
claim guarantees. The analyzer returns structured directional and round-trip
capabilities with their domains, recovery domains, closure obligations, losses,
and assurance evidence.

Directional capabilities are independent:

- forward execution may be defined, total, or partial;
- reverse execution may be defined, total, or partial;
- source recovery may be absent or claimed over `R_S`;
- target recovery may be absent or claimed over `R_T`.

The following names describe common capability combinations. Several rows can
apply to the same correspondence; they are not authored alternatives.

| Capability combination | Guarantees | Executable directions | Recovery claim | Loss |
| --- | --- | --- | --- | --- |
| Fully isomorphic | Forward and reverse are total; both recover exact extensional input state; no canonical rewrite is needed | Forward and reverse | `G(F(S)) ≡S S` and `F(G(T)) ≡T T` over every valid input | None |
| Mutually canonical-recovering | Forward and reverse are total; each recovers the unique canonical state of the input's semantic class | Forward and reverse | Both canonical-state laws over every valid input | No semantic loss; only declared canonical representation changes |
| Source-recovering, or left-invertible | Both needed directions execute and `F(R_S)` is contained in `D_G` | Forward and reverse on the required domains | Source recovery only | No loss of source information on `R_S` |
| Target-recovering, or right-invertible | Both needed directions execute and `G(R_T)` is contained in `D_F` | Reverse and forward on the required domains | Target recovery only | No loss of target information on `R_T` |
| Bidirectionally executable | Both functions have valid plans, with no inverse relationship established | Forward and reverse on their independent domains | None unless separately established | Possible and explicit |
| Forward-total projective | Every valid source produces a valid target; some source information is intentionally unrecoverable | Forward; reverse may be absent, partial, or total | Source recovery is absent or restricted; target recovery may independently hold | Required and explicit |
| Reverse-total import | Every valid target produces a valid source; some target information may be unrecoverable | Reverse; forward may be absent, partial, or total | Target recovery is absent or restricted; source recovery may independently hold | Required and explicit when information is dropped |
| Partial correspondence | At least one executable direction has modeled preconditions beyond workspace validity | Any direction with a valid plan, only on its domain | Only over explicit `R_S` or `R_T` satisfying closure | Possible and explicit |
| Invalid correspondence | Definition is contradictory, ambiguous, incompatible, or has undeclared loss | None | None | May include unresolved or undeclared loss |

For every direction, capability analysis exposes:

- whether it is defined;
- whether it is total or partial;
- its domain constraints;
- whether it constructs a complete valid output;
- its static and conditional losses;
- required extension contracts;
- canonicalization contracts;
- independent source and target recovery claims;
- `R_S` or `R_T` and the opposite-domain closure obligation;
- the equality promised by each claim: semantic recovery under `≈` or
  canonical-state recovery under `≡`;
- structured assurance assessment for every claim.

Two directional plans do not establish either recovery direction. A reverse
plan that chooses a canonical representative may be useful and total even when
it is not a left inverse of the forward plan.

### Assurance Evidence And Claim Assessment

Assurance is not a scalar level. Each capability or recovery claim has a
`ClaimAssessment` with one conclusion:

- `Established`: the claim follows from a checked derivation, together with an
  explicit set of assumptions;
- `Refuted`: a contradiction or counterexample disproves the claim;
- `Undetermined`: available evidence establishes neither truth nor falsehood.

The assessment carries an unordered evidence set. Evidence forms are
independent and may coexist:

- `StructuralDerivation`: derivation from model contracts and the closed plan
  algebra;
- `CheckableProofEvidence`: independently checkable evidence supplied for a
  function or predicate contract;
- `TrustedContractDependency`: an extension semantic contract treated as an
  explicit assumption;
- `ImplementationTrustEvidence`: host trust attached to one exact runtime
  implementation binding;
- `EmpiricalCaseEvidence`: specified examples or generated cases with no found
  counterexample;
- `RuntimeVerificationEvidence`: observations from executions under exact plan
  and implementation identities;
- `CounterexampleEvidence`: a concrete refutation;
- `Assumption`: any premise not established by Core.

An opaque extension declaration does not mechanically establish purity,
determinism, totality, exhaustiveness, disjointness, or invertibility. A caller
may adopt the extension contract as a named assumption, in which case the claim
is established only relative to that assumption and is reported as such.
Passing examples or runtime checks supplies empirical evidence but cannot
establish a universal claim.

Caller policy is a predicate over claim conclusion, evidence forms, assumptions,
extension dependencies, and implementation trust. It is not an ordinal
comparison such as "at least trusted." Policy can permit directional execution
while rejecting publication of an unresolved recovery claim.

## Core Laws And Invariants

### Determinism

For the same validated plan, exact extension implementation binding identities,
trace policy, and state-equal input, semantic evaluation produces state-equal
output and the same semantic diagnostics, loss evidence, provenance event
stream, and deterministic receipt data. Caller-owned sink delivery status is an
infrastructure outcome and is excluded from semantic determinism.

```text
S1 ≡S S2  =>  F_K(S1) ≡T F_K(S2)
T1 ≡T T2  =>  G_K(T1) ≡S G_K(T2)
```

Semantic congruence is a separate requirement:

```text
S1 ≈S S2  =>  F_K(S1) ≈T F_K(S2)
T1 ≈T T2  =>  G_K(T1) ≈S G_K(T2)
```

The transformation output and loss detection do not depend on trace retention
policy. Any ordering in diagnostics or provenance events uses stable semantic
keys.

### Input Nonmutation

The source input to forward application and the target input to reverse
application remain semantically and structurally unchanged whether execution
succeeds or fails. Extension functions receive immutable values.

### Model Compatibility

Validation and compilation require the correspondence's bound model signatures.
Execution rechecks the input signature. The output uses the exact destination
model contract embedded in the plan.

Unknown or extra model elements are not ignored. Under the initial exact-
signature policy they produce a model mismatch. A future compatible-signature
policy must explicitly account for them through coverage and loss rules.

### Validity Preservation

For every executable direction:

```text
Valid(input) and input in domain
    => successful output is Valid(output)
```

Invalid inputs are outside the domain. MetaWeave is not a workspace repair
mechanism. A candidate output is validated before success is returned.

### Totality And Partiality

A total direction accepts every valid workspace for its exact input model
contract. If any extension, discriminator, value pattern, relationship shape,
or cardinality can reject an otherwise valid input, the direction is partial
and its domain constraint must identify that condition.

Outside-domain application returns `InputOutsideDirectionDomain` with a
counterexample reference and no workspace result.

### Coverage

Every required destination concept is constructed exactly once in every
successful output. Every source concept declared covered is consumed, preserved
through a reversible rule, canonicalized, or covered by a loss declaration.

Unmapped required concepts invalidate the correspondence at definition time
when statically knowable. Conditional omissions that violate requiredness fail
execution.

### Identity

Destination identities are deterministic and unique under the destination
model's identity comparison. A collision between distinct logical source
records produces `IdentityCollision`; records are never merged implicitly.

When identity preservation is claimed, the inverse identity rule recovers the
canonical source identity. Repeated application cannot allocate a different
identity for the same semantic input.

### Referential Integrity

Every emitted relationship refers to a destination record that exists in the
same candidate workspace. Relationship resolution uses compiled identity rules,
not name or value guessing. Missing targets fail output validation.

### Cardinality

Constructed records and relationships satisfy both endpoint cardinalities and
the correspondence's cardinality facts. One-to-many and many-to-one mappings
must prove or validate grouping uniqueness. Ambiguous decomposition fails.

### Ordering

Declared significant order is preserved or transformed by an explicit ordering
rule. Discarding significant order is declared loss. Unordered input cannot
acquire semantically meaningful order from traversal accidents.

### Value Presence, Content, And Provenance Preservation

Presence, present content, and represented provenance remain independent through
every executable direction unless an explicit rule maps or equates one
dimension. Type-defined emptiness is evaluated only for present content. The
transition table for every reachable presence alternative and provenance value
must be complete, and content transformation must preserve or explicitly
account for the endpoint's empty-content predicate.

### No Undeclared Loss

Static analysis must account for every covered source concept and every
many-to-one value transformation. Execution must report conditional loss when
its condition occurs. Detectable information loss without a matching
declaration makes the definition invalid or execution fail; policy cannot turn
it into a silent warning.

### Canonicalization

Every canonicalization used in a round-trip claim is total on valid workspaces,
deterministic, semantics-preserving under `≈M`, and state-idempotent under `≡M`.
A unique-normal-form claim additionally maps any two semantically equivalent
inputs to state-equal outputs. Failure of any obligation invalidates the
canonical-state recovery claim. A merely semantics-preserving normalizer cannot
stand in for `C_S` or `C_T` in such a claim.

### Diagnostics And Provenance Stability

Diagnostic codes, phases, correspondence element references, model element
references, and counterexample identities are stable for state-equal inputs.
For semantically equivalent noncanonical inputs, diagnostics identify the same
semantic rules while retaining any representation difference relevant to the
result. Human wording may improve without changing diagnostic identity.

The compiled plan provides logical provenance for every output record and
assigned output member: constructor, rule, input variable roles, extension
contract dependencies, canonicalization, and possible loss. Execution produces
concrete provenance events containing contributing input record and member
identities and the exact extension implementation bindings used. Whether those
events are materialized is an explicit trace policy defined below. Logical
provenance never depends on a file path, line number, table location, or other
physical representation.

### Version And Signature Mismatch

A signature mismatch prevents plan execution. A version label match does not
override a signature mismatch. A correspondence revision comparison may report
compatibility, but it cannot silently rebind an already compiled plan.

## Round-Trip Laws

### Source Recovery

A source canonical-state recovery claim contains `R_S`, `C_S`, and the closure
obligation:

```text
R_S subset-of D_F
F_K(R_S) subset-of D_G

for every S in R_S:
    G_K(F_K(S)) ≡S C_S(S)
```

A source semantic-recovery claim is weaker:

```text
for every S in R_S:
    G_K(F_K(S)) ≈S S
```

It does not establish a stable canonical state. If canonical-state recovery is
claimed, the semantic law follows because `C_S(S) ≈S S` and state equality
implies semantic equivalence.

### Target Recovery

A target canonical-state recovery claim contains `R_T`, `C_T`, and the closure
obligation:

```text
R_T subset-of D_G
G_K(R_T) subset-of D_F

for every T in R_T:
    F_K(G_K(T)) ≡T C_T(T)
```

A target semantic-recovery claim guarantees only:

```text
for every T in R_T:
    F_K(G_K(T)) ≈T T
```

For any input covered by a recovery claim, an intermediate result outside the
opposite direction's domain refutes the claim with
`RecoveryDomainNotClosed`. It never makes the law inapplicable.

### Isomorphism And Canonical Recovery

A fully isomorphic correspondence requires both laws over all valid source and
target workspaces, no semantic loss, and identity canonicalizers:

```text
G_K(F_K(S)) ≡S S
F_K(G_K(T)) ≡T T
```

A mutually canonical-recovering correspondence also requires both recovery
domains to contain every valid endpoint workspace, but compares each result to
the unique normal form selected by `C_S` or `C_T`. This is a genuinely weaker
state guarantee than full isomorphism even though it preserves semantic
meaning.

A directional or partial correspondence claims only the source or target laws
explicitly established over `R_S` or `R_T`. Two executable directions without a
recovery claim remain independent functions.

A counterexample contains:

- the failed law and correspondence revision;
- the smallest identifiable source or target record set needed to reproduce it;
- the first differing model element under stable semantic order;
- original, round-tripped, and canonical presence, content, and represented
  provenance;
- the responsible record-set rule, constructor, value rule, extension contract,
  and exact implementation binding evidence;
- any loss declaration that was expected to justify the difference.

An intermediate outside the opposite domain is a closure counterexample.
Object reference inequality or harmless unordered collection layout is not a
counterexample. For canonical-state recovery, a different declared but
noncanonical spelling is a counterexample because `≡` requires the selected
normal form. A changed identity, relationship, presence, content, represented
provenance, significant order, or undeclared dropped concept is also a
counterexample.

## Core Functions

The signatures below are language-neutral. `Result<X>` is either a successful
immutable value plus diagnostics and evidence, or a failure with no `X`.

### Model Validation

```text
ValidateModelContract(M) -> ModelValidationResult
```

Precondition: `M` is available as neutral model state.

Postcondition: success establishes internal model identity, references,
requiredness, cardinality, ordering, and signature consistency required by
MetaWeave. Failure identifies model elements and no correspondence work begins.

### Workspace Comparison And Canonicalization

```text
CompareSemanticState(M, W1, W2) -> SemanticComparisonResult
CompareExtensionalState(M, W1, W2) -> StateComparisonResult
ApplyCanonicalizer(C_M, W) -> Result<CanonicalWorkspace>
ValidateCanonicalizer(C_M, M) -> CanonicalizationAssessment
```

`CompareSemanticState` implements `≈M` using only equivalences explicitly
declared by `M`. `CompareExtensionalState` implements `≡M` and reports the first
stable state difference. If a model declares no nontrivial representation
equivalence, `≈M` and `≡M` coincide.

`ValidateCanonicalizer` assesses semantic preservation, state idempotence, and
the claimed unique-normal-form law separately. A failed or undetermined
obligation remains visible in the claim assessment. `ApplyCanonicalizer`
returns a workspace only if the input and result are valid under the exact model
signature.

### Correspondence Validation

```text
ValidateCorrespondence(
    K,
    M_S,
    M_T,
    ExtensionContractCatalog)
    -> CorrespondenceValidationResult
```

Validation resolves every endpoint and record variable, checks signatures,
record-set and value variants, lexical scope, argument order, dependency cycles,
constructor identity, coverage, loss declarations, domains, presence/content/
provenance transitions, and extension semantic contracts. It does not require
executable extension implementations.

Success means the definition is internally coherent. It does not by itself
claim totality or reversibility.

### Capability Analysis

```text
AnalyzeCapabilities(
    validated K,
    M_S,
    M_T,
    ExtensionContractCatalog)
    -> CapabilityAnalysis
```

The result contains forward and reverse availability, totality, domain
constraints, output completeness, loss, canonicalization, applicable
source and target recovery claims, recovery-domain closure, and structured claim
assessments. Analysis is deterministic and contains the derivation, assumptions,
evidence, counterexamples, and unresolved dependencies behind each conclusion.
An opaque extension predicate cannot support an established exhaustiveness,
disjointness, totality, or invertibility claim without independently checkable
evidence or an explicit trusted assumption.

### Plan Compilation

```text
CompileForward(
    validated K,
    CapabilityAnalysis,
    M_S,
    M_T,
    ExtensionContractCatalog)
    -> Result<ForwardPlan>

CompileReverse(
    validated K,
    CapabilityAnalysis,
    M_S,
    M_T,
    ExtensionContractCatalog)
    -> Result<ReversePlan>
```

Compilation resolves semantic references and lexical scopes, compiles the
record-set and constructor graph, constructs dependency graphs, selects
deterministic evaluation order, derives required identity, correlation, and
grouping indexes, binds extension contract digests, records logical provenance,
and embeds model signatures.

A plan is:

- immutable;
- bound to one correspondence revision and exact source and target signatures;
- safe for concurrent execution;
- cacheable by correspondence, model, canonicalization, and extension contract
  digests;
- serializable as plan data when all nodes have stable contracts;
- required to bind and verify exact extension implementations before every
  execution context, including after deserialization.

Executable extension delegates are never serialized.

### Directional Application

```text
ApplyForward(
    ForwardPlan,
    S,
    ExtensionImplementationBindings,
    TraceConfiguration)
    -> ApplicationResult<T>

ApplyReverse(
    ReversePlan,
    T,
    ExtensionImplementationBindings,
    TraceConfiguration)
    -> ApplicationResult<S>
```

Application verifies signatures, validity, directional domain, and extension
contract and implementation binding identities. It constructs a private
candidate workspace, emits provenance events according to the trace policy,
records complete loss counts, validates the candidate, and returns it only on
success. Trace policy cannot change selection, construction, loss detection, or
validation.

Expected domain, rule, extension, identity, or validation failures are
structured results. Unexpected implementation faults are captured as an
internal execution diagnostic without exposing a partial candidate as success.

### Output Validation

```text
ValidateApplicationResult(
    Plan,
    Candidate,
    LossEvidence)
    -> OutputValidationResult

ValidateProvenanceResult(
    Plan,
    ProvenanceReceipt)
    -> ProvenanceValidationResult
```

This verifies the destination model signature, workspace validity, plan
postconditions, complete destination coverage, and loss accounting.
`ValidateProvenanceResult` separately verifies logical provenance coverage,
event-set digests, and trace-policy fulfillment. Directional application invokes
both, but trace-delivery failure does not rewrite semantic output validation.

### Round-Trip Verification

```text
VerifySourceRoundTrip(
    ForwardPlan,
    ReversePlan,
    SourceRecoveryClaim,
    S,
    ExtensionImplementationBindings)
    -> RoundTripVerificationResult

VerifyTargetRoundTrip(
    ForwardPlan,
    ReversePlan,
    TargetRecoveryClaim,
    T,
    ExtensionImplementationBindings)
    -> RoundTripVerificationResult
```

Verification first confirms that the input lies in the explicit recovery domain.
It executes the first direction, treats failure of opposite-domain closure as a
counterexample, executes the opposite direction, canonicalizes the original when
the claim requires it, and compares under the claim's exact relation: `≡` for
canonical-state recovery or `≈` for semantic recovery. It returns evidence or a
minimal stable counterexample. Passing selected inputs provides empirical
evidence only unless the plan algebra establishes the universal law.

### Logical Provenance, Trace, And Explanation

The compiled plan contains `LogicalProvenance`: a static graph connecting every
destination constructor and member assignment to input variable roles, source
members, function contracts, canonicalization, and possible loss. This graph
exists regardless of trace policy.

Logical provenance establishes that every output position has an attributable
derivation shape. By itself it does not identify the concrete input records used
for one execution. That requires materialized provenance events or verified
replay.

During application, `ProvenanceEvent` values instantiate that graph with actual
input and output record identities, value-dimension transitions, extension
implementation identities, and encountered loss. Events are deterministic but
need not all reside in memory.

`TracePolicy` is explicit and has distinct modeled alternatives:

- `MaterializeTrace` returns all provenance events in the application result;
- `StreamTrace` sends every event to an explicitly supplied caller-owned sink
  and returns a receipt identifying the completed event set;
- `ReplayableTrace` retains a deterministic receipt containing plan identity,
  exact implementation binding identities, input and output state digests, and
  event count; per-record explanation later requires the exact original input
  and bindings and a verified replay;
- `SummaryTrace` returns exact event counts and stable summaries only and makes
  no claim that arbitrary per-record explanation remains available.

`TraceConfiguration` pairs one policy with any required runtime binding. Only
`StreamTrace` requires a `TraceEventSink`. The sink and an optional caller-owned
`TraceStore` are runtime infrastructure, not correspondence data and not part of
the compiled plan.

A trace sink cannot influence transformation semantics. If the selected policy
requires complete streaming and the sink fails, the semantic transformation
outcome remains unchanged while the trace outcome reports `TraceSinkFailure`
and an incomplete receipt. The application or caller may then refuse to publish
the output. A caller-owned trace store and its physical location are outside
MetaWeave semantics.

Every `ApplicationResult` contains a `ProvenanceReceipt` stating the selected
policy, logical provenance plan identity, exact implementation binding
identities, input and output state digests, total event count, and which
explanation operations remain available. A caller-owned store handle may
accompany the receipt, but the handle is infrastructure context and is excluded
from semantic result equality; the event-set digest remains deterministic.

```text
ExplainFromMaterializedTrace(
    MaterializedTrace,
    ProvenanceReceipt,
    OutputElementReference)
    -> ExplanationResult

ExplainByReplay(
    Plan,
    OriginalInput,
    ExtensionImplementationBindings,
    ProvenanceReceipt,
    OutputElementReference)
    -> ExplanationResult
```

`ExplainFromMaterializedTrace` also applies to a complete caller-owned trace
store. `ExplainByReplay` verifies every digest and binding identity before
re-execution. `SummaryTrace` without replay inputs returns
`TraceDetailUnavailable` for arbitrary output elements.

An explanation is structured: record-set rule, constructor, value rule, input
contributions, presence/content/provenance transitions, extension contract and
implementation identities, canonicalization, and loss. Presentation prose is
an application concern.

### Loss Enumeration

```text
EnumerateDeclaredLoss(validated K, direction) -> LossContract
EnumerateEncounteredLoss(ApplicationResult) -> LossEvidence
```

Static and encountered loss remain separate. A caller can reject a plan before
execution based on its static loss contract.

### Revision Compatibility

```text
CompareCorrespondenceRevisions(
    K_old,
    K_new,
    M_S,
    M_T,
    ExtensionContractCatalog)
    -> CorrespondenceCompatibilityResult
```

The comparison reports changes to domains, coverage, identities, loss,
canonicalization, extensions, plan signatures, and claimed laws. It does not
reduce compatibility to version text.

## Execution Lifecycle

```text
source and target model contracts
              +
correspondence definition and ExtensionContractCatalog
              |
              v
validate models and correspondence
              |
              v
analyze directional capabilities, recovery, and evidence
              |
              v
compile immutable forward and/or reverse plan
              |
              v
bind exact ExtensionImplementationBindings and TraceConfiguration
              |
              v
apply plan to neutral workspace state
              |
              v
validate private candidate, provenance receipt, and loss evidence
              |
              v
return complete result or structured failure
```

Execution means correspondence execution over neutral workspace state. It does
not mean executing a physical artifact.

Compilation and application remain separate so a validated plan can be reused
across many workspace instances with the same exact model signatures and
extension contracts. Each execution still records the exact implementation
bindings used.

## Atomicity And Concurrency

Input workspaces and plans are immutable from Core's perspective. An application
builds a private candidate. A failed semantic application returns no candidate
as a successful transformation result and does not expose partially constructed
state through the public contract. A trace-delivery failure is reported
separately and does not turn a validated candidate into a partial workspace.

Implementations may use internal mutable builders for efficiency, provided the
mutation is unobservable and discarded on failure. Workspace publication, if
requested by an application after success, is owned by the selected workspace
surface and is outside MetaWeave Core.

An immutable plan may execute concurrently against independent input
workspaces. Extension implementations must meet the same concurrency and purity
declarations in their implementation bindings. Any extension requiring per-
execution state receives isolated state constructed from explicit inputs; it
cannot use shared ambient state. Core reports implementation identity and trust
evidence but cannot prove arbitrary implementation code follows its declaration.

## Scalability Requirements

Core plans classify each rule's data access needs without selecting a storage
technology:

- record-local transformation;
- identity-keyed lookup;
- relationship traversal;
- correlation and grouping;
- global validation.

Record-local rules may stream. Identity and relationship rules require stable
indexes. Correlation, grouping, and global rules may require a complete
partition or graph. General aggregation is not a first-target primitive; if a
future aggregate contract is added, its access requirements become part of the
compiled plan.

Partitioned execution is valid only when the plan establishes that:

- all dependencies needed by a partition are available;
- identity collision checks combine deterministically across partitions;
- relationship targets can be resolved completely;
- cardinality checks remain global where required;
- ordering semantics are preserved;
- extension functions do not depend on traversal or partition order.

The plan reports constraints that prevent safe streaming or partitioning. Core
does not silently switch to weaker validation for scale.

Diagnostic detail may be bounded by an explicit execution policy. When bounded,
the result preserves total counts, stable first examples, and a
`DiagnosticLimitReached` fact. Loss and failure are never hidden by truncation.

## Results And Diagnostics

### Result Structure

A directional application result contains:

- a semantic transformation outcome containing success or failure and the
  complete validated output workspace on semantic success only;
- a separate trace outcome containing its receipt or delivery failure;
- structured diagnostics;
- static and encountered loss evidence;
- correspondence revision and plan identity;
- source and target model signatures;
- exact extension implementation binding identities and digests;
- structured claim assessments and execution evidence.

Expected invalidity is not communicated solely through exception prose.

### Diagnostic Identity

Every diagnostic contains:

- stable code;
- severity;
- phase;
- correspondence revision and element reference;
- source and target model element references when applicable;
- record and member identities when applicable;
- presence, present content, and represented provenance without accidental
  normalization;
- structured parameters;
- human-readable message.

Diagnostics do not contain physical artifact locations as semantic identity.
Applications may add their own location context after Core returns.

### Definition-Time Diagnostics

- `SourceModelMismatch`
- `TargetModelMismatch`
- `UnsupportedModelVersion`
- `UnmappedRequiredConcept`
- `AmbiguousCorrespondence`
- `ContradictoryCorrespondence`
- `DependencyCycle`
- `AmbiguousArgumentOrder`
- `RecordVariableOutOfScope`
- `RecordVariableEntityMismatch`
- `UnsupportedRecordSetCardinality`
- `IncompleteRecordConstruction`
- `UndeclaredInformationLoss`
- `InvalidCanonicalization`
- `UnsupportedExtensionFunction`

### Capability-Analysis Diagnostics

- `DirectionTotalityUndetermined`
- `DiscriminatorExhaustivenessUndetermined`
- `DiscriminatorDisjointnessUndetermined`
- `RecoveryDomainClosureUndetermined`
- `InverseClaimUndetermined`
- `NonInvertibleTransformation`
- `RecoveryClaimRefuted`

### Compilation-Time Diagnostics

- `DirectionNotAvailable`
- `IncompleteDirectionDomain`
- `UnboundExtensionContract`
- `ExtensionContractDigestMismatch`
- `UnsupportedPlanPrimitive`
- `IncompatibleCanonicalization`

### Execution-Time Diagnostics

- `InputOutsideDirectionDomain`
- `IdentityCollision`
- `MissingRelationshipTarget`
- `CardinalityViolation`
- `RequiredValueOmitted`
- `AmbiguousDiscriminator`
- `GroupInvariantViolation`
- `MissingExtensionImplementationBinding`
- `ExtensionImplementationDigestMismatch`
- `ExtensionFunctionFailure`
- `ConditionalLossEncountered`
- `OutputValidationFailure`

### Trace-Delivery And Explanation Diagnostics

- `TraceSinkFailure`
- `TraceIncomplete`
- `TraceDetailUnavailable`
- `ProvenanceReceiptMismatch`

### Verification-Time Diagnostics

- `NoncanonicalInput`
- `CanonicalizationNotIdempotent`
- `CanonicalFormsNotUnique`
- `RecoveryDomainNotClosed`
- `RoundTripCounterexample`
- `LossEvidenceMismatch`

`NonInvertibleTransformation` applies only when an authored recovery or inverse
claim is contradicted or cannot meet its obligations. An independently useful
reverse function that selects a representative is not itself an error.

The exact eventual code catalog is model data. The categories above are minimum
semantic requirements, not permission to encode all meaning in a message.

## Worked Example: Reversible Person And Contact

### Model Fragments

Source model `People`:

```text
Person
  identity Id
  required GivenName under TextAlphabet A
  required FamilyName under TextAlphabet A
  required relationship Address -> Address

Address
  identity Id
  required Line
```

Target model `Contacts`:

```text
Contact
  identity Id
  required PackedName under TextAlphabet A
  required relationship Location -> Location

Location
  identity Id
  required Line
```

Both models treat record collections as unordered. All listed properties and
relationships are required. `Absent` and `Null` names are outside both model
contracts. A name is `Present(text, Explicit)`, and the content may be the empty
string because the text contract defines it.

### Correspondence

`Person` corresponds one-to-one with `Contact`. `Address` corresponds one-to-one
with `Location`.

The forward direction contains:

- `EntityRecords(Person)` introducing record variable `person`;
- one per-binding `Contact` constructor over `person`;
- `EntityRecords(Address)` introducing record variable `address`;
- one per-binding `Location` constructor over `address`;
- a traversal from `person` through `Person.Address` introducing
  `personAddress` for the relationship assignment.

The reverse direction contains the symmetrical target record sets, constructors,
and `Contact.Location` traversal. Every endpoint value names one of these record
variables.

The assignments are:

- `Contact.Id` is the identity value of `person.Id`.
- `Location.Id` is the identity value of `address.Id`.
- `Contact.PackedName` is
  `PairText(person.GivenName, person.FamilyName)`.
- reverse assignments use `PairText.First(contact.PackedName)` and
  `PairText.Second(contact.PackedName)`.
- `Location.Line` and `Address.Line` copy from their in-scope source variables.
- `Contact.Location` uses the address/location identity correspondence applied
  to `personAddress`.

`PairText` is a closed primitive with fully specified semantics, not an extension
declaration. Let the model's text alphabet be a fixed non-empty finite alphabet.
`RankText` enumerates all finite strings first by length and then
lexicographically, giving a bijection between text and natural numbers. Let:

```text
PairNat(a, b) = ((a + b) * (a + b + 1)) / 2 + b

PairText(x, y) =
    UnrankText(PairNat(RankText(x), RankText(y)))

For z = PairNat(a, b):
    w = floor((sqrt(8 * z + 1) - 1) / 2)
    t = (w * (w + 1)) / 2
    b = z - t
    a = w - b
```

The inverse equations, `RankText`, and `UnrankText` define `PairText.First` and
`PairText.Second`. The fixed alphabet identity is part of the primitive contract.
This is a total bijection including empty text. Its usefulness here is formal:
it demonstrates construction and decomposition without relying on an
unverified parser or delimiter convention.

### Forward Behavior

For each source `Person`, the forward plan constructs one `Contact` with the
same identity, packs the two name values, resolves the related `Address`, and
points to the corresponding `Location`. Each source `Address` constructs one
`Location`.

### Reverse Behavior

For each target `Contact`, the reverse plan constructs one `Person` with the
same identity, decomposes `PackedName` into `GivenName` and `FamilyName`, resolves
the related `Location`, and points to the corresponding `Address`. Each target
`Location` constructs one `Address`.

### Classification And Laws

Both directions are total for all valid workspaces under their exact contracts.
Identity and relationships are bijective. All concepts have complete coverage.
There is no loss and no canonicalization beyond identity.

Therefore the correspondence is fully isomorphic and requires:

```text
G_K(F_K(S)) ≡S S
F_K(G_K(T)) ≡T T
```

The claim is structurally established from the record constructor graph,
identity mappings, complete coverage, and the closed `PairText` bijection. It
does not depend on an extension contract or examples that happen to parse.

## Worked Example: Projecting A Reading To A Band

Source model `Readings`:

```text
Measurement
  identity Id
  required ExactReading under ordered ReadingValue contract
```

Target model `Bands`:

```text
BandObservation
  identity Id
  required Label in { low, normal, high }
```

The forward correspondence preserves identity and classifies `ExactReading`
into one of `low`, `normal`, or `high`. Classification is total for every valid
source reading and produces a valid target. It is authored as
`EntityRecords(Measurement)` followed by one per-binding `BandObservation`
constructor. The reverse direction, when present, uses
`EntityRecords(BandObservation)` and one per-binding `Measurement` constructor.

For this example, `ReadingValue` is the model's ordered integer content contract
and classification is a closed primitive:

```text
x < 10       -> low
10 <= x < 100 -> normal
x >= 100     -> high
```

The function is many-to-one. Distinct values such as `17` and `18` may both
produce `normal`. No reverse function can recover every exact source value from
the target label.

If `K` declares `ExactReading` as dropped precision through a modeled loss
declaration, the analyzer classifies the correspondence as forward-total and
projective. Forward execution is allowed, encountered loss is reported, and no
source round-trip law is claimed.

`K` may also define an independently executable, total reverse function that
selects canonical representatives:

```text
G(low) = 0
G(normal) = 17
G(high) = 100
```

Assume the target model admits exactly these three labels and the classification
boundaries make each representative classify back to its label. Then:

```text
F(G(label)) ≡T label
```

for every valid target label. The reverse function is a right inverse of `F` and
establishes total target recovery. It is not a left inverse:

```text
R_T = every valid Bands workspace
G_K(R_T) subset-of D_F

for every T in R_T:
    F_K(G_K(T)) ≡T T
```

```text
G(F(18)) = 17
G(F(18)) not-equivalent-to 18
```

This correspondence is bidirectionally executable, forward-total projective,
reverse-total, and target-recovering. It is not source-recovering. Defining the
representative constructor is valid and does not produce
`NonInvertibleTransformation` by itself.

If `K` claims source recovery over a domain containing `18`, capability analysis
or verification reports `NonInvertibleTransformation` or
`RoundTripCounterexample`, with `18` as the counterexample. The diagnostic is
about the false recovery claim, not the existence of the reverse function.

If `K` omits the loss declaration, validation reports
`UndeclaredInformationLoss` and the correspondence is invalid. The pair of
source records with values `17` and `18` is a precise counterexample because
they have distinct source meaning and the same target result.

## Composition Decision

General correspondence composition is deferred from the first implementation.
It combines domain restrictions, canonicalization boundaries, loss, extension
contracts, implementation evidence, provenance, and claim assessment.
Implementing it before those contracts are stable would encourage an unsafe
rule-splicing shortcut.

The first design must nevertheless preserve composition by ensuring:

- every model endpoint has a stable signature and semantic element references;
- plan inputs and outputs are explicit workspace contracts;
- every rule and logical provenance contribution has stable identity;
- loss is machine-readable and directional;
- canonicalization is explicit and signature-bound;
- extension contracts have stable identity and digest;
- capability analysis is structured rather than reduced to a label.

A future composition:

```text
K_AB : A <-> B
K_BC : B <-> C
K_AC = K_BC o K_AB
```

is valid only when the produced `B` contract of `K_AB` is compatible with the
consumed `B` contract of `K_BC`.

Let `D_AB^F` and `D_BC^F` be the forward domains. The composed forward function
is `F_AC = F_BC o F_AB` with domain:

```text
D_AC^F = { A in D_AB^F | F_AB(A) in D_BC^F }
D_AC^F = D_AB^F intersect inverse-image(F_AB, D_BC^F)
```

The domains are not directly intersected because one contains `A` workspaces
and the other contains `B` workspaces.

Let `D_BC^G` be the reverse domain from `C` to `B`, and `D_AB^G` the reverse
domain from `B` to `A`. The composed reverse function is
`G_AC = G_AB o G_BC` with domain:

```text
D_AC^G = { C in D_BC^G | G_BC(C) in D_AB^G }
D_AC^G = D_BC^G intersect inverse-image(G_BC, D_AB^G)
```

Losses do not compose as an unqualified set union. Each loss cause is translated
through logical provenance across `B`. A later rule may make an earlier
distinction irrelevant on the composed domain, a canonicalizer may normalize a
representation without semantic loss, and domain restriction may exclude a
loss condition. Composition preserves original cause identities and derives the
net source-to-target loss contract from the composed graph.

Logical provenance composes rule contributions through `B`; execution events
retain both original plan and implementation binding identities. Claim evidence
composes as a dependency graph of derivations, assumptions, empirical evidence,
and counterexamples. It is not reduced to a weakest scalar level.

Canonicalization at `B` must be compatible or be made an explicit composition
step. Forward and reverse totality, source and target recovery domains, closure,
and canonical-state laws are analyzed again for `K_AC`. Reversibility is not
inherited from labels or from the independent executability of the inputs.

## Ownership Boundaries

| Owner | Responsibility |
| --- | --- |
| MetaWeave model | Declarative correspondence semantics, domains, coverage, loss, canonicalization references, and extension contracts |
| MetaWeave Core | Model and correspondence validation, capability analysis, plan compilation, pure directional application, output validation, round-trip verification, diagnostics, loss evidence, logical provenance, trace policy, and provenance receipts |
| Workspace surfaces | Opening, reading, creating, saving, locking, and publishing workspace representations |
| Future artifact-model packages | Modeled semantic contracts for an artifact family |
| Future artifact adapters | Faithful physical artifact reading, writing, and execution |
| Applications and CLIs | Workspace acquisition, surface choice, orchestration, policy choice, and presentation |

Artifact adapters may reason only about faithfully reading, writing, or
executing their artifact contract. They may not decide domain-to-artifact
semantics, infer missing mappings, or normalize domain values without a
correspondence rule.

## Forbidden Shortcuts

- Stringly typed source or target paths in correspondence semantics.
- Workspace surface selection inside MetaWeave Core.
- Semantic transformation logic in artifact adapters.
- Hidden conversion through XML or any other preferred representation.
- Mutation of source workspaces during application.
- Best-effort reverse mapping.
- Silent dropping, merging, truncation, or defaulting of data.
- Empty-string normalization without modeled presence and content rules.
- Naming-convention inference, including `...Id` relationship guessing.
- Reflection-based matching of entities or properties.
- Runtime inference of omitted correspondence rules.
- Arbitrary scripts or expression strings presented as a correspondence model.
- Ambient extension registries, clocks, randomness, environment access, or
  process-dependent behavior.
- Traversal order used as record identity or semantic ordering.
- Treating two directional implementations as proof of reversibility.
- Claiming isomorphism from example-based tests alone.
- Preserving target data through an undeclared destination baseline.
- Encoding alternatives in a generic `Kind` property.

## Open Decisions

### Model Compatibility Beyond Exact Signatures

Decision: whether later revisions may bind structurally compatible model
contracts instead of exact signatures.

Options:

- Require exact signatures permanently.
- Enumerate explicitly accepted signatures per correspondence revision.
- Define a structural compatibility relation and prove mapping coverage against
  each input model.

Consequences: exact signatures are simple and safe but require new revisions for
harmless model evolution. Enumerated signatures scale modestly. General
structural compatibility is powerful but can make coverage and unknown-element
behavior difficult to reason about.

Recommendation: exact signatures for the first implementation, followed by
explicit accepted signatures if real revision pressure justifies it.

Evidence that could overturn it: repeated correspondence revisions whose only
difference is a mechanically demonstrable, semantics-preserving model change.

### Extension Trust Policy

Decision: which evidence and assumptions a caller accepts for claims depending
on extension contracts and exact implementation bindings.

Options:

- Permit only structurally derived or independently checkable proof evidence.
- Permit named extension-contract assumptions while requiring exact contract
  identity and digest.
- Additionally require trusted implementation identity, host evidence, and
  specified runtime verification.

Consequences: proof-only policy is strongest but excludes opaque domain
functions. Contract assumptions enable practical extensions but do not prove an
implementation. Implementation trust and runtime checks improve operational
confidence without establishing universal semantics.

Recommendation: keep `ExtensionContractCatalog` and
`ExtensionImplementationBindings` architecturally fixed as separate explicit
inputs. Express caller acceptance as a predicate over claim conclusion,
evidence, assumptions, contract dependencies, and implementation identities.

Evidence that could overturn it: independently checkable proof-carrying
extensions capable of removing a trust assumption.

### Canonicalization Ownership

Decision: how model-owned and correspondence-owned canonicalization combine.

Options:

- Models own all canonicalization.
- Correspondences own all canonicalization.
- Both own distinct contracts composed explicitly by plans.

Consequences: model-only ownership cannot describe normalization introduced by
a bridge. Correspondence-only ownership duplicates intrinsic model semantics.
Unqualified dual ownership creates ambiguous round-trip laws.

Recommendation: retain both as separate signature-bound contracts and record
their exact composition in each round-trip claim. Only contracts establishing
semantic preservation, state idempotence, and any claimed unique normal form may
serve as `C_S` or `C_T` for canonical-state recovery.

Evidence that could overturn it: a formal model contract capable of expressing
all bridge-specific representational choices without coupling models to their
correspondences.

### Default Trace Policy And Store Contract

Decision: which explicit trace policy should be the application default and
what receipt protocol a caller-owned trace store must implement.

Options:

- Return complete trace for every output record and member.
- Stream every event to a caller-owned store and return a verified receipt.
- Default to replayable trace with exact plan, input, output, and implementation
  digests.
- Default to summary trace and explicitly disable arbitrary output explanation.

Consequences: complete materialization gives immediate explanation but can
approach output size. Streaming shifts retention outside Core but requires a
receipt and failure contract. Replay avoids permanent event storage but requires
the exact original input and implementation bindings. Summary is cheapest but
cannot explain arbitrary records later.

Recommendation: logical provenance remains mandatory and independent of this
choice. Prefer `StreamTrace` when a durable trace store is available and
`ReplayableTrace` otherwise. Never claim direct per-record explanation from a
summary-only receipt.

Evidence that could overturn it: measured workloads showing complete trace is
cheap enough to remain the only mode, or a compact provenance store that can
answer arbitrary explanations without full events or replay.

### Plan Serialization

Decision: whether compiled plans must be durable across processes in the first
implementation.

Options:

- In-process immutable plans only.
- Serializable plan data with explicit extension rebinding.

Consequences: serialization improves reuse but adds compatibility and security
contracts before the plan algebra has settled.

Recommendation: define a serializable, deterministic plan representation but
do not make cross-process persistence an initial acceptance criterion. Never
serialize executable delegates. A deserialized plan rebinds exact
`ExtensionImplementationBindings` and records those identities in execution
evidence.

Evidence that could overturn it: a concrete host requirement where compilation
cost or deployment topology makes durable plans necessary.

### Static Proof And Runtime Verification

Decision: which claims Core may establish mechanically and which remain trusted
or empirical.

Options:

- Restrict reversibility claims to the closed primitive algebra.
- Permit extension inverse contracts as named assumptions under caller policy.
- Accept independently checkable proof evidence for extension claims.

Consequences: primitive-only derivation is strongest but too restrictive for
many domains. Trusted assumptions are practical if reported, but do not become
mechanical proof. Checkable proof evidence can establish more claims. Samples
cannot prove arbitrary functions.

Recommendation: report `Established`, `Refuted`, or `Undetermined` with the full
evidence and assumption set. Allow caller policy to accept an undetermined or
assumption-relative claim for execution without advertising it as unqualified
proof. Report runtime verification as empirical evidence only.

Evidence that could overturn it: a proof-carrying extension format that Core can
independently check.

### Composition In The First Implementation

Decision: whether to implement general composition before one correspondence is
fully executable and verified.

Options:

- Include general composition immediately.
- Defer execution but preserve composition-compatible identities, plans, loss,
  provenance, structured evidence, and canonicalization.

Consequences: immediate composition multiplies unresolved semantics. Deferral
risks future incompatibility only if early contracts use local, unstable
identities or opaque plan nodes.

Recommendation: defer composition execution and enforce the future-compatible
contracts listed in the composition section.

Evidence that could overturn it: an early required workflow that cannot be
expressed as separately executed correspondences without losing atomicity or
provenance.

### Explicit Null And Default Provenance

Decision: whether the neutral workspace contract should later represent explicit
`Null` presence and `Defaulted(DefaultRuleIdentity)` provenance independently
from `Absent` and `Present(Content, Explicit)`.

Options:

- Keep current absent-versus-present text semantics and require endpoint models
  to model further distinctions explicitly.
- Extend the neutral value representation with factored presence and
  present-value provenance.

Consequences: the first option preserves the foundation but limits which value
distinctions a correspondence can carry. The second broadens the foundation and
has a repository-wide blast radius.

Recommendation: MetaWeave defines the abstract distinction now but does not
change the foundation. Provenance applies to present values in the first target.
A correspondence can claim preservation only for presence, content, emptiness,
and provenance distinctions its bound endpoint contracts represent.

Evidence that could overturn it: sanctioned models requiring an explicit null
distinct from absence across multiple workspace surfaces.

## Proposed Implementation Sequence

| Stage | Entry Conditions | Output | Acceptance Criteria |
| --- | --- | --- | --- |
| 1. Formal MetaWeave model | This target is reviewed; endpoint identity and exact signature policy are agreed | A sanctioned correspondence model containing record variables and scopes, closed record-set and value algebras, constructors, domains, recovery domains, coverage, loss, canonicalization, extension contracts, and factored value semantics | No paths, scripts, `Kind`, heuristics, surface concepts, implicit defaults, or untyped operation nodes; both worked examples can be authored completely |
| 2. Model and correspondence validator | Stage 1 model is loadable through neutral workspace contracts | Structured validators and diagnostic catalog | Scope, record binding, correlation, grouping, construction, identity, dependency, value-dimension, extension-contract, canonicalization, and recovery-domain invariants have positive and negative conformance cases |
| 3. Capability and claim analyzer | Validated definitions and `ExtensionContractCatalog` are available | Structured directional domains, source and target recovery claims, closure obligations, loss, and `Established`/`Refuted`/`Undetermined` assessments with evidence and assumptions | Independent directional functions are not reported as inverses; opaque extensions cannot establish universal claims without checkable evidence or named assumptions |
| 4. Immutable plan representation | Analyzer output and both closed algebras are stable | Signature-bound forward and reverse plan IR with scopes, constructors, dependencies, indexes, extension contract digests, and logical provenance | Plans are immutable, deterministic, concurrently reusable, cache-keyed, and contain no surface, implementation delegate, or physical trace location |
| 5. Pure forward executor | Forward plan conformance fixtures and implementation-binding contract exist | Atomic forward application over neutral workspace state using exact `ExtensionImplementationBindings` | Determinism, nonmutation, domain, record construction, identity, relationship, cardinality, factored value, loss, provenance-event, and output-validity laws pass |
| 6. Pure reverse executor | Independently valid reverse plans and representative-constructor fixtures exist | Atomic reverse application using the same algebras | Reverse execution does not imply recovery; valid representative constructors execute; false source or target recovery claims remain refuted |
| 7. Provenance, trace, and diagnostic model | Both executors expose deterministic provenance events | Logical provenance, materialized/streamed/replayable/summary trace policies, receipts, loss evidence, bounded diagnostics, and explanation queries | Every output is logically attributable; each policy states exactly what is materialized and explainable; policy cannot affect output or loss detection |
| 8. Recovery and round-trip verifier | Semantic equivalence, state equality, canonicalizers, recovery domains, and closure contracts are implemented | Source and target verification with minimal closure or state counterexamples | Canonical-state laws use `≡`; semantic recovery uses `≈`; intermediate domain failure refutes rather than disables a claim; empirical evidence is not reported as proof |
| 9. Conformance suite | All core phases are integrated | A surface-free suite covering record cardinalities, classifications, equality laws, recovery closure, failure phases, extensions, trace policies, scale boundaries, and examples | The suite uses neutral state only and includes mutation guards, collisions, relationships, absence, null, present content defined as empty, default provenance, implementation identity, and counterexamples |
| 10. Orchestration and consumers | Core contracts and conformance suite are accepted | Application integration, CLI authoring, and only afterward future artifact-model and adapter packages | Consumers acquire and publish workspaces outside Core; adapters remain mechanical; no unrelated system such as MetaSql is changed to implement the core |

No stage may use the next stage to compensate for a missing invariant. In
particular, CLI validation cannot make an under-modeled correspondence safe, and
an artifact adapter cannot repair a weak semantic plan.

## Review Checklist

- [ ] Every claimed guarantee corresponds to a validator, analyzer rule, plan
      invariant, execution check, or verification law.
- [ ] Every executable direction has an explicit model signature, domain,
      totality statement, output coverage, and failure behavior.
- [ ] Every recovery claim has an explicit `R_S` or `R_T`, proves the required
      opposite-domain closure, and names `≈` or `≡` as its comparison.
- [ ] Every covered source concept is preserved, canonicalized, or covered by
      explicit loss.
- [ ] Every required destination concept has exactly one construction path.
- [ ] Every endpoint value identifies both an exact model member and an in-scope
      record or group variable.
- [ ] Record selection, traversal, correlation, grouping, fan-out, construction,
      and reverse decomposition use the closed typed algebra.
- [ ] Every inverse claim is assessed as established, refuted, or undetermined
      with independent evidence and assumptions rather than a scalar level.
- [ ] Independent forward and reverse functions are not called inverses without
      a source or target recovery law.
- [ ] Identity derivation is deterministic, collision-checked, and reversible
      wherever identity preservation is claimed.
- [ ] Relationship mapping preserves referential integrity and declared
      cardinality in each executable direction.
- [ ] Presence, present content, type-defined emptiness, and represented
      provenance behavior is explicit for every reachable endpoint state.
- [ ] Canonicalization preserves `≈`, is idempotent under `≡`, establishes any
      claimed unique normal form, and remains distinct from loss.
- [ ] Unknown elements and model signature mismatches fail or follow an explicit
      compatible-signature contract.
- [ ] Plans are immutable, signature-bound, deterministic, concurrently safe,
      and independent of workspace surfaces.
- [ ] Extension semantic contracts and executable implementation bindings are
      separate, and execution records the exact implementation identity.
- [ ] Logical provenance covers every output; trace policy states whether full
      explanation is materialized, stored, replayable, or unavailable.
- [ ] Trace and diagnostics identify semantic correspondence and model elements,
      not physical locations, and trace policy cannot affect transformation.
- [ ] No file, database, source-code, connection-string, artifact, or surface
      concept appears in the MetaWeave semantic model or Core execution plan.
- [ ] No adapter is permitted to decide domain-to-artifact semantics.
- [ ] No arbitrary path, script, expression string, reflection guess, naming
      heuristic, or runtime inference stands in for modeled semantics.
- [ ] Example-based testing is not used to claim general invertibility.
- [ ] Future composition remains possible through stable endpoint, rule, loss,
      provenance, extension, evidence, and canonicalization identities, with
      domains defined by inverse images rather than cross-space intersection.
- [ ] The target can be implemented and tested over neutral workspace state
      without modifying unrelated systems such as MetaSql.
