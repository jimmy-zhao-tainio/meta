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
  requiredness, cardinality, ordering, and value-state semantics that the model
  can express.
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

### Equivalence

`≈S` and `≈T` are model-bound semantic equivalence relations over source and
target workspaces. Each relation must be reflexive, symmetric, and transitive.
It compares modeled meaning, not object references or incidental collection
layout.

By default, semantic equivalence requires:

- equivalent model contracts;
- the same record identities and entity membership;
- the same property presence state and value;
- the same relationship targets;
- the same significant ordering;
- no difference hidden by an undeclared loss.

Incidental in-memory collection order may be ignored when the model declares
the collection unordered. Identifier spelling, empty values, absent values, and
declared ordering remain significant unless an explicit canonicalization rule
says otherwise.

Ordinary object equality is insufficient because two workspace values may use
different object identities or harmless container order while carrying the
same modeled graph.

### Canonicalization

`C_S : W_S -> W_S` and `C_T : W_T -> W_T` are total, deterministic,
semantics-preserving canonicalization functions over valid workspaces. They
must be idempotent:

```text
C_S(C_S(S)) ≈S C_S(S)
C_T(C_T(T)) ≈T C_T(T)
```

Canonicalization may select one representation among explicitly equivalent
forms. It may not conceal information loss. If a rule drops or conflates
modeled information, that behavior is loss, not canonicalization.

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
- producing stable diagnostics, trace, and loss evidence;
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
and the explicitly supplied extension catalog. Time, randomness, environment
variables, process state, network state, and ambient registries cannot influence
the result.

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
- semantic rules;
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

One-to-many and many-to-one rules require explicit grouping, decomposition, and
identity rules. A reverse direction is not available merely because the forward
records can be enumerated.

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

The executor never invents a relationship from property naming. A relationship
target must exist in the candidate result or be constructed by a dependency
that completes before validation.

### Construction And Decomposition

Value and identity transformation use a closed expression algebra represented
as modeled graph data. The graph has a `Value` identity hub and distinct
variant entities for:

- an endpoint value;
- a literal value with an explicit value state;
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
tuple member selection, and explicit presence-state selection. Domain-specific
formatting, parsing, normalization, classification, or lookup belongs in a
named extension contract.

### Domain Constraints

Directional domains are modeled with a closed constraint algebra. At minimum it
must express:

- value-state requirements;
- equality and membership constraints over modeled values;
- relationship presence;
- cardinality constraints;
- discriminator coverage;
- a named extension predicate when primitives are insufficient.

An extension predicate is subject to the same purity, identity, trust, and
diagnostic rules as an extension function. Arbitrary boolean expression text is
not a domain model.

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

### Missing, Null, Empty, And Defaulted Values

The abstract value domain distinguishes:

```text
Absent
Null
PresentEmpty
Present(value)
Defaulted(value, default-rule)
```

An endpoint model contract declares which states it can represent. A
correspondence must provide an explicit state transition for every reachable
input state. Two states may be equated only by an explicit canonicalization or
loss rule.

In the current neutral workspace, an absent property key represents no supplied
value and a present empty string represents an explicit empty value. It does
not independently represent explicit null or default provenance. MetaWeave may
not pretend to preserve distinctions that an endpoint contract cannot carry.
Such a distinction must be excluded from the domain, modeled in the endpoint,
or declared as loss.

Defaults are applied by named default rules. The rule identity is part of trace
and, when the target model carries it, part of semantic state. Missing values do
not become empty strings. Null values do not become defaults. Empty strings do
not become absent.

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
destination construction. Static validation requires alternatives to be
disjoint where ambiguity would change output and exhaustive wherever totality
is claimed. An otherwise valid input matching no alternative makes the
direction partial. An input matching conflicting alternatives is an execution
failure.

The first implementation need not support an open polymorphic expression
language. It must preserve room for modeled discriminator alternatives and
must not replace them with reflection or name inspection.

### Loss Declarations

Loss is a first-class correspondence fact. A loss declaration identifies:

- direction;
- affected source concept or value state;
- the rule responsible;
- whether the loss always occurs or is conditional;
- the modeled condition for conditional loss;
- the information that cannot be recovered;
- the resulting capability restriction.

Distinct loss forms should be modeled separately, including dropped concepts,
many-to-one coalescing, default substitution, precision reduction, discarded
ordering, and state conflation. A description may explain a declaration but
cannot be its only semantics.

Execution reports each encountered conditional loss or a deterministic summary
with exact counts when detailed evidence is intentionally bounded. Static loss
that always applies is discoverable before execution.

### Extension Contracts

An `ExtensionFunctionContract` has:

- stable identity and semantic revision;
- ordered input ports and named output ports;
- accepted value states for every input;
- produced value states for every output;
- domain constraints;
- purity and determinism requirements;
- declared failure diagnostics;
- forward and inverse identities when an inverse is claimed;
- assurance evidence;
- a deterministic contract digest.

The implementation is supplied through an explicit extension catalog. A plan
records the contract identity and digest. Execution refuses an implementation
whose contract digest differs.

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
- its idempotence obligation;
- its semantics-preservation obligation;
- its assurance evidence.

Correspondence-owned canonicalization may normalize only choices introduced or
recognized by `K`. Model-owned canonicalization is supplied as part of the model
contract environment. Compilation records the exact canonicalization contracts
used by a round-trip claim.

## Correspondence Capabilities

Capabilities are derived by analysis. Authors do not assign a `Kind` and thereby
claim guarantees. The analyzer returns structured directional and round-trip
capabilities with their obligations, losses, and assurance.

The following names describe common capability combinations.

| Classification | Guarantees | Executable Directions | Round-Trip Laws | Loss |
| --- | --- | --- | --- | --- |
| Fully isomorphic | Both directions are total and information preserving; canonicalization is identity or semantically invisible | Forward and reverse | Both laws | None |
| Reversible up to canonicalization | Both directions are total on their declared contracts and recover canonical semantic form | Forward and reverse | Both laws with explicit `C_S` and `C_T` | No semantic loss beyond declared equivalence |
| Forward-total projective | Every valid source can produce a valid target; some source information is intentionally not recoverable | Forward; reverse absent or partial | Source round trip is not claimed; target law may apply on `D_G` | Required and explicit |
| Reverse-total import | Every valid target can produce a valid source; forward reconstruction is absent or partial | Reverse; forward absent or partial | Target round trip is not claimed; source law may apply on `D_F` | Required and explicit when applicable |
| Partial correspondence | At least one executable direction has additional modeled preconditions | Only directions with valid plans, on their domains | Only laws explicitly proven over those domains | Possible, explicit |
| Invalid correspondence | Definition is contradictory, ambiguous, incompatible, or has undeclared loss | None | None | May include unresolved or undeclared loss |

For every direction, capability analysis exposes:

- whether it is defined;
- whether it is total or partial;
- its domain constraints;
- whether it constructs a complete valid output;
- its static and conditional losses;
- required extension contracts;
- canonicalization contracts;
- applicable round-trip claims;
- assurance for each claim.

Two directional implementations do not establish reversibility. Reversibility
requires the applicable round-trip laws and evidence.

### Assurance Levels

Assurance attaches to individual claims, not to the correspondence as one vague
rating.

| Assurance | Meaning |
| --- | --- |
| Structurally established | The claim follows from the closed primitive algebra, model constraints, and mechanically checked plan structure |
| Extension-contract trusted | The claim depends on a named extension whose contract and implementation identity are trusted but whose semantics are not mechanically proven by Core |
| Empirically exercised | Tests or runtime verification found no counterexample in specified cases; this is evidence, not proof |
| Declared only | The author asserted the claim without sufficient structural or trusted evidence; execution may be allowed by policy, but the claim is not advertised as established |

An empirically exercised claim never becomes structurally established because
more examples passed. Policy may reject plans below a required assurance level.

## Core Laws And Invariants

### Determinism

For the same validated plan, extension catalog identities, and semantically
equivalent input, application produces semantically equivalent output,
diagnostics, loss evidence, and trace.

```text
S1 ≈S S2  =>  F_K(S1) ≈T F_K(S2)
T1 ≈T T2  =>  G_K(T1) ≈S G_K(T2)
```

Any ordering in diagnostics or trace uses stable semantic keys.

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

### Value-State Preservation

Absent, null, present empty, present non-empty, and defaulted states remain
distinct through every executable direction unless an explicit rule maps or
equates them. The state transition table for every reachable input state must
be complete.

### No Undeclared Loss

Static analysis must account for every covered source concept and every
many-to-one value transformation. Execution must report conditional loss when
its condition occurs. Detectable information loss without a matching
declaration makes the definition invalid or execution fail; policy cannot turn
it into a silent warning.

### Canonicalization

Every canonicalization used in a round-trip claim is total on valid workspaces,
deterministic, idempotent, and semantics-preserving under its declared
equivalence relation. Failure of any obligation invalidates that round-trip
claim.

### Diagnostics And Trace Stability

Diagnostic codes, phases, correspondence element references, model element
references, and counterexample identities are stable for equivalent inputs.
Human wording may improve without changing diagnostic identity.

Every output record and assigned output member is traceable to the
correspondence rule, contributing input records and members, extension contract
revisions, canonicalization, and encountered loss. Trace never depends on a
file path, line number, table location, or other physical representation.

### Version And Signature Mismatch

A signature mismatch prevents plan execution. A version label match does not
override a signature mismatch. A correspondence revision comparison may report
compatibility, but it cannot silently rebind an already compiled plan.

## Round-Trip Laws

The source round-trip law is:

```text
G_K(F_K(S)) ≈S C_S(S)
```

It is required for all `S` for which forward application succeeds and the
result lies in `D_G`, when the correspondence claims source recovery.

The target round-trip law is:

```text
F_K(G_K(T)) ≈T C_T(T)
```

It is required for all `T` for which reverse application succeeds and the
result lies in `D_F`, when the correspondence claims target recovery.

A fully isomorphic correspondence requires both laws over all valid source and
target workspaces, with no semantic loss. A reversible-canonical correspondence
requires both laws against the exact canonicalization contracts. A directional
or partial correspondence claims only the laws valid on its declared domains.

A counterexample contains:

- the failed law and correspondence revision;
- the smallest identifiable source or target record set needed to reproduce it;
- the first differing model element under stable semantic order;
- original, round-tripped, and canonical value states;
- the responsible rule and extension evidence;
- any loss declaration that was expected to justify the difference.

Object reference inequality, harmless unordered collection layout, or a
different but declared canonical spelling is not a counterexample. A changed
identity, relationship, value state, significant order, or undeclared dropped
concept is.

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

### Correspondence Validation

```text
ValidateCorrespondence(K, M_S, M_T, E) -> CorrespondenceValidationResult
```

`E` is the explicit extension contract catalog. Validation resolves every
endpoint, checks signatures, rule variants, argument order, dependency cycles,
coverage, loss declarations, domains, state tables, and extension contracts.

Success means the definition is internally coherent. It does not by itself
claim totality or reversibility.

### Capability Analysis

```text
AnalyzeCapabilities(validated K, M_S, M_T, E) -> CapabilityAnalysis
```

The result contains forward and reverse availability, totality, domain
constraints, output completeness, loss, canonicalization, applicable
round-trip laws, and assurance. Analysis is deterministic and contains the
evidence behind each conclusion.

### Plan Compilation

```text
CompileForward(validated K, CapabilityAnalysis, M_S, M_T, E)
    -> Result<ForwardPlan>

CompileReverse(validated K, CapabilityAnalysis, M_S, M_T, E)
    -> Result<ReversePlan>
```

Compilation resolves semantic references, constructs dependency graphs,
selects deterministic evaluation order, derives required indexes, binds
extension contract digests, and embeds model signatures.

A plan is:

- immutable;
- bound to one correspondence revision and exact source and target signatures;
- safe for concurrent execution;
- cacheable by correspondence, model, canonicalization, and extension contract
  digests;
- serializable as plan data when all nodes have stable contracts;
- required to rebind and verify extension implementations after deserialization.

Executable extension delegates are never serialized.

### Directional Application

```text
ApplyForward(ForwardPlan, S, E) -> ApplicationResult<T>
ApplyReverse(ReversePlan, T, E) -> ApplicationResult<S>
```

Application verifies signatures, validity, directional domain, and extension
catalog identity. It constructs a private candidate workspace, records trace
and loss evidence, validates the candidate, and returns it only on success.

Expected domain, rule, extension, identity, or validation failures are
structured results. Unexpected implementation faults are captured as an
internal execution diagnostic without exposing a partial candidate as success.

### Output Validation

```text
ValidateApplicationResult(Plan, Candidate, Trace, LossEvidence)
    -> OutputValidationResult
```

This verifies the destination model signature, workspace validity, plan
postconditions, complete destination coverage, trace completeness, and loss
accounting. Directional application invokes it before returning success, but it
remains a distinct phase and contract.

### Round-Trip Verification

```text
VerifySourceRoundTrip(ForwardPlan, ReversePlan, S, E)
    -> RoundTripVerificationResult

VerifyTargetRoundTrip(ForwardPlan, ReversePlan, T, E)
    -> RoundTripVerificationResult
```

Verification executes the applicable composition, canonicalizes the original,
compares under the model-bound equivalence relation, and returns either evidence
or a minimal stable counterexample. Passing selected inputs provides empirical
evidence only unless the plan algebra establishes the law structurally.

### Explanation And Trace

```text
ExplainOutput(Trace, OutputElementReference) -> Explanation
TraceInputs(Trace, OutputElementReference) -> InputContributionSet
```

An explanation is structured: rule identity, input contributions, value-state
transitions, function contracts, canonicalization, and loss. Presentation prose
is an application concern.

### Loss Enumeration

```text
EnumerateDeclaredLoss(validated K, direction) -> LossContract
EnumerateEncounteredLoss(ApplicationResult) -> LossEvidence
```

Static and encountered loss remain separate. A caller can reject a plan before
execution based on its static loss contract.

### Revision Compatibility

```text
CompareCorrespondenceRevisions(K_old, K_new, M_S, M_T, E)
    -> CorrespondenceCompatibilityResult
```

The comparison reports changes to domains, coverage, identities, loss,
canonicalization, extensions, plan signatures, and claimed laws. It does not
reduce compatibility to version text.

## Execution Lifecycle

```text
source and target model contracts
              +
correspondence definition and extension contracts
              |
              v
validate models and correspondence
              |
              v
analyze directional capabilities and assurance
              |
              v
compile immutable forward and/or reverse plan
              |
              v
apply plan to neutral workspace state
              |
              v
validate private candidate, trace, and loss evidence
              |
              v
return complete result or structured failure
```

Execution means correspondence execution over neutral workspace state. It does
not mean executing a physical artifact.

Compilation and application remain separate so a validated plan can be reused
across many workspace instances with the same exact model signatures and
extension contracts.

## Atomicity And Concurrency

Input workspaces and plans are immutable from Core's perspective. An application
builds a private candidate. A failed application returns no candidate as a
successful result and does not expose partially constructed state through the
public contract.

Implementations may use internal mutable builders for efficiency, provided the
mutation is unobservable and discarded on failure. Workspace publication, if
requested by an application after success, is owned by the selected workspace
surface and is outside MetaWeave Core.

An immutable plan may execute concurrently against independent input
workspaces. Extension implementations must meet the same concurrency and purity
contract. Any extension requiring per-execution state receives isolated state
constructed from explicit inputs; it cannot use shared ambient state.

## Scalability Requirements

Core plans classify each rule's data access needs without selecting a storage
technology:

- record-local transformation;
- identity-keyed lookup;
- relationship traversal;
- grouping or aggregation;
- global validation.

Record-local rules may stream. Identity and relationship rules require stable
indexes. Grouping and global rules may require a complete partition or graph.

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

- success or failure;
- the complete validated output workspace on success only;
- structured diagnostics;
- trace;
- static and encountered loss evidence;
- correspondence revision and plan identity;
- source and target model signatures;
- assurance actually used during execution.

Expected invalidity is not communicated solely through exception prose.

### Diagnostic Identity

Every diagnostic contains:

- stable code;
- severity;
- phase;
- correspondence revision and element reference;
- source and target model element references when applicable;
- record and member identities when applicable;
- value state without accidental normalization;
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
- `UndeclaredInformationLoss`
- `InvalidCanonicalization`
- `UnsupportedExtensionFunction`
- `UntrustedInverseDeclaration`

### Compilation-Time Diagnostics

- `DirectionNotAvailable`
- `IncompleteDirectionDomain`
- `UnboundExtensionContract`
- `ExtensionContractDigestMismatch`
- `UnsupportedPlanPrimitive`
- `IncompatibleCanonicalization`
- `NonInvertibleTransformation`

### Execution-Time Diagnostics

- `InputOutsideDirectionDomain`
- `IdentityCollision`
- `MissingRelationshipTarget`
- `CardinalityViolation`
- `RequiredValueOmitted`
- `AmbiguousDiscriminator`
- `ExtensionFunctionFailure`
- `ConditionalLossEncountered`
- `OutputValidationFailure`

### Verification-Time Diagnostics

- `NoncanonicalInput`
- `CanonicalizationNotIdempotent`
- `RoundTripCounterexample`
- `TraceIncomplete`
- `LossEvidenceMismatch`

The exact eventual code catalog is model data. The categories above are minimum
semantic requirements, not permission to encode all meaning in a message.

## Worked Example: Reversible Person And Contact

### Model Fragments

Source model `People`:

```text
Person
  identity Id
  required GivenName
  required FamilyName
  required relationship Address -> Address

Address
  identity Id
  required Line
```

Target model `Contacts`:

```text
Contact
  identity Id
  required PackedName
  required relationship Location -> Location

Location
  identity Id
  required Line
```

Both models treat record collections as unordered. All listed properties and
relationships are required. Missing, null, and defaulted names are outside both
model contracts; explicit empty is a valid text value.

### Correspondence

`Person` corresponds one-to-one with `Contact`. `Address` corresponds one-to-one
with `Location`.

- `Contact.Id` is the identity value of `Person.Id`.
- `Location.Id` is the identity value of `Address.Id`.
- `Contact.PackedName` is `PairText(Person.GivenName, Person.FamilyName)`.
- Reverse assignments use `PairText.First` and `PairText.Second` to recover the
  two source properties.
- `Location.Line` and `Address.Line` copy in either direction.
- `Contact.Location` maps through the address/location identity correspondence.

`PairText` is a named function contract defining a total bijection between an
ordered pair of text atoms and one text atom. It is not ordinary concatenation.
Its inverse is part of the same contract, including behavior for explicit empty
values. For this example, the function has structurally established inverse
evidence.

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
G_K(F_K(S)) ≈S S
F_K(G_K(T)) ≈T T
```

The claim depends on the established bijection of `PairText`, not on examples
that happen to parse.

## Worked Example: Projecting A Reading To A Band

Source model `Readings`:

```text
Measurement
  identity Id
  required ExactReading
```

Target model `Bands`:

```text
BandObservation
  identity Id
  required Label
```

The forward correspondence preserves identity and classifies `ExactReading`
into one of `low`, `normal`, or `high`. Classification is total for every valid
source reading and produces a valid target.

The function is many-to-one. Distinct values such as `17` and `18` may both
produce `normal`. No reverse function can recover the exact source value from
the target label.

If `K` declares `ExactReading` as dropped precision through a modeled loss
declaration, the analyzer classifies the correspondence as forward-total and
projective. Forward execution is allowed, encountered loss is reported, and no
source round-trip law is claimed.

If `K` declares a reverse mapping from `normal` to an arbitrary representative,
the analyzer reports `NonInvertibleTransformation`. A preferred representative
does not recover the source.

If `K` omits the loss declaration, validation reports
`UndeclaredInformationLoss` and the correspondence is invalid. The pair of
source records with values `17` and `18` is a precise counterexample because
they have distinct source meaning and the same target result.

## Composition Decision

General correspondence composition is deferred from the first implementation.
It combines domain restrictions, canonicalization boundaries, loss, extension
trust, trace, and assurance. Implementing it before those contracts are stable
would encourage an unsafe rule-splicing shortcut.

The first design must nevertheless preserve composition by ensuring:

- every model endpoint has a stable signature and semantic element references;
- plan inputs and outputs are explicit workspace contracts;
- every rule and trace contribution has stable identity;
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
consumed `B` contract of `K_BC`. Its domain is the intersection induced by both
plans. Its losses are the propagated union, with duplicate causes preserved.
Its trace composes rule contributions through `B`. Its assurance cannot exceed
the weakest required claim. Canonicalization at `B` must be compatible or be
made an explicit composition step. Reversibility is derived again; it is not
inherited from labels on the two inputs.

## Ownership Boundaries

| Owner | Responsibility |
| --- | --- |
| MetaWeave model | Declarative correspondence semantics, domains, coverage, loss, canonicalization references, and extension contracts |
| MetaWeave Core | Model and correspondence validation, capability analysis, plan compilation, pure directional application, output validation, round-trip verification, diagnostics, loss evidence, and trace |
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
- Empty-string normalization without a modeled value-state rule.
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

### Extension Registration And Trust

Decision: how an application binds extension contracts to implementations and
chooses minimum assurance.

Options:

- An explicit immutable catalog passed to validation, compilation, and
  execution.
- A process-global registry.
- Dynamic discovery by reflection or naming.

Consequences: an explicit catalog preserves determinism and makes dependencies
testable. Global or discovered registries introduce hidden environment and
version ambiguity.

Recommendation: an explicit immutable catalog keyed by contract identity and
digest, with caller policy specifying acceptable assurance.

Evidence that could overturn it: a deterministic composition mechanism that
retains the same explicit catalog identity while simplifying application setup.

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
their exact composition in each round-trip claim.

Evidence that could overturn it: a formal model contract capable of expressing
all bridge-specific representational choices without coupling models to their
correspondences.

### Trace Retention

Decision: whether complete trace detail is mandatory in every application
result or may be retained through a deterministic policy.

Options:

- Return complete trace for every output record and member.
- Require semantic trace but allow deterministic partitioning or summarized
  retention with counts and stable samples.
- Make trace optional.

Consequences: complete trace gives the strongest explanation but can approach
the size of the output. Optional trace weakens auditability and round-trip
counterexamples.

Recommendation: semantic trace is mandatory. The storage and retention policy
may be explicit and bounded, but every omitted detail must be represented by
counts and stable trace partitions that can be reproduced from the same input.

Evidence that could overturn it: measured workloads showing complete trace is
cheap enough to remain the only mode, or a stronger compact provenance algebra.

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
serialize executable delegates.

Evidence that could overturn it: a concrete host requirement where compilation
cost or deployment topology makes durable plans necessary.

### Static Proof And Runtime Verification

Decision: which claims Core may establish mechanically and which remain trusted
or empirical.

Options:

- Restrict reversibility claims to the closed primitive algebra.
- Permit trusted extension inverse contracts with explicit assurance.
- Treat passing round-trip samples as proof.

Consequences: primitive-only proof is strongest but too restrictive for many
domains. Trusted extensions are practical if accurately labeled. Samples cannot
prove arbitrary functions.

Recommendation: mechanically establish structural claims, allow extension-
trusted claims under caller policy, and report runtime verification as empirical
evidence only.

Evidence that could overturn it: a proof-carrying extension format that Core can
independently check.

### Composition In The First Implementation

Decision: whether to implement general composition before one correspondence is
fully executable and verified.

Options:

- Include general composition immediately.
- Defer execution but preserve composition-compatible identities, plans, loss,
  trace, and canonicalization.

Consequences: immediate composition multiplies unresolved semantics. Deferral
risks future incompatibility only if early contracts use local, unstable
identities or opaque plan nodes.

Recommendation: defer composition execution and enforce the future-compatible
contracts listed in the composition section.

Evidence that could overturn it: an early required workflow that cannot be
expressed as separately executed correspondences without losing atomicity or
trace.

### Explicit Null And Default Provenance

Decision: whether the neutral workspace contract should later represent explicit
null and default provenance independently from absence.

Options:

- Keep current absent-versus-present text semantics and require endpoint models
  to model further distinctions explicitly.
- Extend neutral value state with explicit null and default provenance.

Consequences: the first option preserves the foundation but limits which value
distinctions a correspondence can carry. The second broadens the foundation and
has a repository-wide blast radius.

Recommendation: MetaWeave defines the abstract distinction now but does not
change the foundation. A correspondence can claim preservation only for states
its bound endpoint contracts represent.

Evidence that could overturn it: sanctioned models requiring an explicit null
distinct from absence across multiple workspace surfaces.

## Proposed Implementation Sequence

| Stage | Entry Conditions | Output | Acceptance Criteria |
| --- | --- | --- | --- |
| 1. Formal MetaWeave model | This target is reviewed; endpoint identity and exact signature policy are agreed | A sanctioned correspondence model containing contracts, rules, domains, coverage, loss, canonicalization, and extension references | No paths, scripts, `Kind`, heuristics, surface concepts, or implicit defaults; both worked examples can be authored completely |
| 2. Model and correspondence validator | Stage 1 model is loadable through neutral workspace contracts | Structured validators and diagnostic catalog | Every definition-time invariant has focused positive and negative conformance cases; invalid definitions produce stable element references |
| 3. Capability analyzer | Validated definitions and extension contracts are available | Structured directional, totality, loss, round-trip, and assurance analysis | The analyzer derives all classifications in this document and never trusts an authored capability label |
| 4. Immutable plan representation | Analyzer output and primitive expression algebra are stable | Signature-bound forward and reverse plan IR with dependencies, indexes, and trace identities | Plans are immutable, deterministic, concurrently reusable, cache-keyed, and contain no surface or executable delegate |
| 5. Pure forward executor | Forward plan conformance fixtures exist | Atomic forward application over neutral workspace state | Determinism, nonmutation, domain, identity, relationship, cardinality, state, loss, and output-validity laws pass for primitive plans |
| 6. Pure reverse executor | Reverse-capable plans and inverse fixtures exist | Atomic reverse application using the same plan algebra | Reverse laws pass without best-effort fallback; unsupported or ambiguous inverse plans cannot compile |
| 7. Trace and diagnostic model | Both executors expose stable semantic events | Structured trace, loss evidence, bounded diagnostic policy, and explanation queries | Every output member is attributable; equivalent executions produce stable codes, references, ordering, and loss counts |
| 8. Round-trip verifier | Canonicalization and equivalence contracts are implemented | Source and target verification with minimal counterexamples | Both laws are checked where claimed; failures identify the first semantic difference; empirical evidence is not reported as proof |
| 9. Conformance suite | All core phases are integrated | A surface-free suite covering classification, laws, failure phases, extensions, scale boundaries, and examples | The suite proves core behavior using neutral state only and includes mutation guards, collision cases, missing/null/empty/default cases, and extension trust levels |
| 10. Orchestration and consumers | Core contracts and conformance suite are accepted | Application integration, CLI authoring, and only afterward future artifact-model and adapter packages | Consumers acquire and publish workspaces outside Core; adapters remain mechanical; no unrelated system such as MetaSql is changed to implement the core |

No stage may use the next stage to compensate for a missing invariant. In
particular, CLI validation cannot make an under-modeled correspondence safe, and
an artifact adapter cannot repair a weak semantic plan.

## Review Checklist

- [ ] Every claimed guarantee corresponds to a validator, analyzer rule, plan
      invariant, execution check, or verification law.
- [ ] Every executable direction has an explicit model signature, domain,
      totality statement, output coverage, and failure behavior.
- [ ] Every covered source concept is preserved, canonicalized, or covered by
      explicit loss.
- [ ] Every required destination concept has exactly one construction path.
- [ ] Every inverse claim identifies structural, trusted-extension, or empirical
      evidence without conflating them.
- [ ] Identity derivation is deterministic, collision-checked, and reversible
      wherever identity preservation is claimed.
- [ ] Relationship mapping preserves referential integrity and declared
      cardinality in each executable direction.
- [ ] Missing, null, present empty, present non-empty, and defaulted behavior is
      explicit for every reachable state the endpoint models can represent.
- [ ] Canonicalization is total, idempotent, semantics-preserving, and distinct
      from loss.
- [ ] Unknown elements and model signature mismatches fail or follow an explicit
      compatible-signature contract.
- [ ] Plans are immutable, signature-bound, deterministic, concurrently safe,
      and independent of workspace surfaces.
- [ ] Trace and diagnostics identify semantic correspondence and model elements,
      not physical locations.
- [ ] No file, database, source-code, connection-string, artifact, or surface
      concept appears in the MetaWeave semantic model or Core execution plan.
- [ ] No adapter is permitted to decide domain-to-artifact semantics.
- [ ] No arbitrary path, script, expression string, reflection guess, naming
      heuristic, or runtime inference stands in for modeled semantics.
- [ ] Example-based testing is not used to claim general invertibility.
- [ ] Future composition remains possible through stable endpoint, rule, loss,
      trace, extension, and canonicalization identities.
- [ ] The target can be implemented and tested over neutral workspace state
      without modifying unrelated systems such as MetaSql.
