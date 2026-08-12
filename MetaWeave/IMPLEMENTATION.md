# MetaWeave Implementation Target

## Status

This is draft layer 4 of the MetaWeave specification ladder. It derives from
[`EXECUTION.md`](EXECUTION.md) and remains subordinate to
[`KERNEL.md`](KERNEL.md).

This document reaches implementation-level architecture: project ownership,
component contracts, a closed first feature slice, delivery stages, and
decision gates. It does not authorize model or code changes.

## Layer Question

The execution layer defines a surface-neutral partial-function runtime. This
layer answers:

> What is the smallest implementation in the Meta architecture that can prove
> the ladder coherent and measure whether MetaWeave is worth completing?

## Target Project Boundary

```text
MetaWeave.Model
  authored K product model and typed views
          |
          v
MetaWeave.Core
  correspondence validation
  capability analysis and compilation
  immutable directional products
  atomic directional execution
          ^
          |
Application / MetaWeave CLI
  acquire K and input workspace through surfaces
  select a direction
  invoke Core
  present diagnostics
  publish successful output through surfaces

Meta.Operations
  neutral model contracts, workspace state, equality support, validation
```

**IM-1 (D; `EX-5`, kernel `K-B`).** `MetaWeave.Core` depends on neutral
workspace contracts and the authored MetaWeave model. It does not depend on
workspace loading, publication, filesystem paths, databases, concrete
surfaces, command parsing, or presentation.

An application may depend on `Meta.Integration` to acquire and publish neutral
state. That dependency does not flow back into Core or into `K`.

**IM-2 (C; satisfies endpoint feasibility).** The initial compiled product is
an immutable in-process object interpreted by Core. Plan persistence, code
generation, and cross-process reuse are deferred until measured compilation
cost justifies them.

## Core Component Contracts

Names are descriptive API targets, not required CLR names.

### Correspondence validator

```text
Validate(K, M_S, M_T) -> CorrespondenceValidation
```

Implements `CM-1` through `CM-16`. It resolves exact endpoints, checks logical
structure, assesses each direction independently, and returns structured
diagnostics. It performs no instance-data sampling and opens no workspace.

### Correspondence compiler

```text
Compile(validated K, M_S, M_T) -> CompiledCorrespondence
```

Implements `CP-1` through `CP-14`. The result contains optional immutable
forward and reverse products, derived capabilities, and evidence. Compilation
is deterministic and has no ambient registry.

### Direction executor

```text
Apply(compiled direction, input workspace) -> ApplicationResult
```

Implements `EX-1` through `EX-16`. It checks exact contracts and domain,
constructs an isolated candidate, validates it, and returns atomic success or
structured failure.

### Recovery verifier

```text
VerifyRecovery(compiled correspondence, claim, fixtures/evidence)
    -> ClaimAssessment
```

This component is not required for the first forward slice. When introduced,
it evaluates explicit claim domains and closure; it never infers recovery from
the existence of both products. Universal proof and fixture-based empirical
evidence remain distinct.

## Runtime Model Contract

**IM-3 (C; realizes `CM-1`, `CP-1`, `EX-2`).** Core receives each exact model
contract as an explicit immutable `WorkspaceSemanticContract`-equivalent
binding containing:

- deterministic contract identity;
- the neutral structural model;
- complete validity evaluation for `W_M`;
- state equality;
- model-owned semantic equivalence;
- an optional model-owned canonicalizer and its stated laws.

The name describes a responsibility, not a required CLR interface. The binding
is passed directly to validation and compilation and is retained or rebound by
exact identity for execution. Core does not find it in an ambient registry.

The first slice accepts only contracts whose complete validity is the neutral
structural validator, whose semantic equivalence coincides with state equality,
and which require no canonicalizer. Richer product contracts remain valid
kernel concepts but are unsupported until their explicit binding and law tests
exist.

## Proposed Authored Model Shape

**IM-4 (C; realizes `CORRESPONDENCE-MODEL.md`).** Before changing the sanctioned
product model, the following logical records are reviewed as a Meta model. They
are intentionally semantic and contain no workspace locations.

| Logical record | Responsibility |
| --- | --- |
| `Correspondence` | Stable authored identity. |
| `CorrespondenceRevision` | One immutable semantic revision and its source and target contract bindings. |
| `SourceModelContract` | Exact source semantic contract identity and diagnostic name. |
| `TargetModelContract` | Exact target semantic contract identity and diagnostic name. |
| `ForwardDirection` | Optional authored `Delta_F`, its domain root, rules, coverage, and loss. |
| `ReverseDirection` | Optional authored `Delta_G`, independently defined. |
| `PerRecordEntityConstruction` | One destination record for every record of one exact source entity. |
| `EmptyEntityConstruction` | An explicit empty record set for a destination entity when its contract permits one. |
| `IdentityConstruction` | Explicit destination identity derivation. |
| `PropertyConstruction` | Explicit destination property construction. |
| `RelationshipConstruction` | Explicit destination relationship construction. |
| `DomainPredicate` variants | Closed, typed domain vocabulary. |
| `SourceCoverage` | Preservation, transformation, canonicalization, or loss accounting. |
| `LossDeclaration` | Directional, attributable modeled loss. |
| `CanonicalizationClaim` | Optional exact model-bound canonicalization reference. |
| `RecoveryClaim` | Optional domain, closure, equality strength, and canonicalizer. |

Alternative constructs are represented as distinct typed variants or
relationships, not an opaque `Kind` plus payload and not expression strings.
The final entity and relationship design is produced through the normal Meta
model review and generation workflow only after the logical slice is accepted.

## Closed First Feature Slice

The first slice is intentionally smaller than the complete logical model. Its
purpose is to test the architecture with one real correspondence, not to claim
general model transformation.

### Supported

**IM-5 (C; bounded realization of `CM-3` through `CM-10`).** A direction in the
first slice supports:

- exact source and target contract identities;
- structurally complete model contracts for which semantic equivalence is
  state equality and no canonicalizer is required;
- optional forward and reverse definitions using the same independent shape;
- total domain, or a conjunction of `EveryRecordPropertyPresent`,
  `EveryRecordPropertyEqualsConstant`, and
  `EveryRecordRelationshipPresent` workspace predicates over exact source
  endpoints;
- per-source-record construction of one destination record;
- explicit empty construction for a destination entity when its contract and
  the correspondence permit no records;
- exactly one per-record or empty construction for every target entity, so no
  two constructors can collide in the first slice;
- destination identity copied from the bound source record identity;
- property values copied from one bound source property, supplied as an
  explicit constant, or explicitly absent where the target permits absence;
- relationships copied through participating entity constructions whose
  destination identities are preserved, or explicitly absent where the target
  permits absence;
- explicit source-entity, identity, property, relationship, and significant-order
  coverage;
- explicit loss for covered source facts not preserved;
- complete target coverage validation;
- exact-contract output validation;
- independently authored reverse execution;
- exact recovery assessment on finite conformance fixtures, clearly reported
  as empirical unless established symbolically by the closed primitives.

The closed predicates quantify over every record of their exact source entity.
`EveryRecordPropertyEqualsConstant` requires the property to be present on
every record and equal under the source contract's text-value semantics. The
relationship predicate requires the exact relationship to be present on every
record. The empty entity set satisfies all three universal predicates.
Predicates cannot call scripts or arbitrary host functions.

### Rejected as unsupported

**IM-6 (C; protects `CM-15`, `CP-8`, `CP-9`).** The first compiler rejects:

- many-to-one grouping or aggregation;
- one-to-many fan-out;
- correlations or joins between independently selected record sets;
- multiple constructors targeting the same destination entity;
- computed identities other than exact identity copy;
- general value expressions or arbitrary conversion functions;
- relationship synthesis not backed by explicitly participating records;
- contracts requiring unsupported significant-order transformation;
- contracts whose validity, semantic equivalence, or canonicalization requires
  an unsupported product semantic binding;
- external semantic extensions;
- implicit defaults, repair, inference, or name-based matching;
- canonical recovery that depends on an unimplemented canonicalizer;
- general composition, incremental synchronization, or in-place update.

Unsupported input is a bounded product limitation, not hidden best effort. A
new construct enters only through a later correspondence-model refinement,
compiler obligation, executor behavior, and conformance case.

## Semantic Contract Identity

**IM-7 (C; realizes `CP-3`).** The first implementation computes a deterministic
contract signature from neutral structural model state in canonical order. The
signature version is part of the identity. A future richer contract identity
also incorporates the explicit identity and revision of product-owned validity,
equivalence, and canonicalization semantics. Diagnostic names are stored
separately and do not participate as compatibility guesses.

The signature algorithm must be tested against every model distinction used by
validation and state equality. If the foundation cannot yet expose a complete
semantic signature, implementation stops at this gate rather than substituting
model names.

## Candidate Construction

**IM-8 (C; realizes `EX-4`, `EX-7`, `EX-9`).** Execution uses an isolated
neutral workspace builder for the exact target contract:

1. validate product and input contract;
2. validate the input workspace;
3. decide domain membership;
4. enumerate source bindings in canonical semantic order where order is
   otherwise insignificant;
5. construct identities and values into isolated candidate state;
6. resolve compiled relationship assignments;
7. validate collisions, coverage, and the complete target workspace;
8. publish the candidate only as an in-memory success result.

Canonical processing order is an implementation device for determinism. It
does not make incidental order semantically significant.

## Diagnostics

**IM-9 (C; realizes `CP-13`, `EX-12`).** Validator, compiler, and executor share
a stable diagnostic catalog. A diagnostic includes:

- code and phase;
- severity;
- direction when applicable;
- correspondence revision and logical element identity;
- exact model endpoint identity when applicable;
- record identity only for data-dependent execution failures;
- invariant message arguments independent of presentation wording.

Applications may attach file or editor locations while presenting diagnostics.
Those attachments do not enter semantic equality, cache identity, or execution.

## Delivery Stages and Gates

| Stage | Output | Gate before continuing |
| --- | --- | --- |
| 0. Specification conformance | Accepted ladder documents and trace matrix | Every layer validates against its predecessor; unresolved questions are not assumed. |
| 1. Logical model fixture | An in-memory authored `K` fixture for one real source/target pair | The fixture accounts for domain, target coverage, source coverage, identity, relationships, and loss without scripts or paths. |
| 2. Sanctioned Meta model | Reviewed product model and generated typed views | The model expresses the fixture without opaque payloads or runtime inference. |
| 3. Validator and compiler | Structured validation plus one compiled direction | Invalid endpoints, uncovered targets, undeclared loss, and unsupported constructs fail before execution. |
| 4. Forward executor | Atomic `F_K` over neutral state | Determinism, nonmutation, domain, validity, loss, and atomic-failure tests pass. |
| 5. Value gate | Cost and clarity comparison with one direct handwritten converter | Continue only if authored correspondence and generic machinery demonstrate credible reuse or material maintainability benefit. |
| 6. Independent reverse slice | `G_K` only for a real product need | Reverse value is demonstrated independently; no recovery language appears without a valid claim. |
| 7. Recovery and expansion | Claim verifier and the next smallest required construct | Each added construct pays for itself in a real correspondence and passes the full ladder. |

No full implementation or adoption program begins before stage 5. Failure at a
gate is a valid result and may justify stopping MetaWeave.

## Value Measurement

**IM-10 (C; decision discipline).** The stage-5 comparison records:

- authored correspondence size and conceptual count;
- compiler/executor code and test cost attributable to the slice;
- diagnostic quality for invalid authoring and outside-domain inputs;
- time to implement and review the correspondence;
- change cost when one endpoint contract evolves;
- reuse across a second direction or second correspondence;
- semantic obligations made explicit that a direct converter would otherwise
  hide;
- runtime and memory cost on representative workspaces.

The comparison includes framework cost rather than counting only the concise
authored fixture. Sunk work and architectural elegance are not continuation
criteria.

## Predecessor Validation

| Requirement | Execution basis | Implementation realization |
| --- | --- | --- |
| Neutral complete workspaces | `EX-2`, `EX-7` | Core consumes and returns neutral state under explicitly bound exact contracts. |
| Explicit domain before evaluation | `EX-3` | Closed predicates compile to deterministic membership checks. |
| No ambient semantics | `EX-5`, `EX-6` | Core APIs contain no paths, surfaces, registries, or inference fallback. |
| Nonmutation and atomicity | `EX-4`, `EX-9` | Isolated candidate builder and validation gate. |
| Determinism and congruence | `EX-10`, `EX-11` | Immutable products, canonical processing, and model-owned equality tests. |
| Structured failure | `EX-12` | Shared phase-specific diagnostic catalog. |
| No runtime capability promotion | `EX-13`, `EX-14` | Capability evidence remains compiler-owned; recovery is a separate component. |

The execution counterexamples remain implementable: invalid input, exact
contract mismatch, outside-domain input, collision, invalid candidate, explicit
loss, and successful independent directions without recovery.

## Open Decisions

- **IM-O1:** The first real source/target contract pair used for the value gate.
- **IM-O2:** The exact Meta entity design for closed predicates and construction
  variants.
- **IM-O3:** Whether model semantic signatures belong in the generic foundation
  or remain a MetaWeave-owned derivation over neutral contracts.
- **IM-O4:** Whether the first slice needs relationship construction or should
  prove property-only construction before adding it.
- **IM-O5:** The quantitative continuation threshold at stage 5.
