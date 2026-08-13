# MetaWeave Implementation Target

## Status

This is draft layer 4 of the MetaWeave specification ladder. It derives from
the `E1` abstract machine in [`4-EXECUTION.md`](4-EXECUTION.md) and remains
subordinate to [`1-KERNEL.md`](1-KERNEL.md).

The input to this layer is the named `K1`, `DIR1`, and `E1` abstractions. The
output is a concrete Meta/.NET component design and a bounded delivery slice.
This document does not authorize model or code changes.

The existing MetaWeave implementation is not evidence for this design. It may
be replaced. Existing project names are retained only where they fit the
derived ownership boundary.

## Layer Question

The execution layer supplies a complete surface-neutral machine. This layer
adds the final abstraction needed before implementation:

> Which Meta records, immutable .NET values, services, and project boundaries
> realize each preceding construct without introducing new semantics?

## Project Boundary

```text
MetaWeave.Model
  generated typed representation of authored K1 records
          |
          v
MetaWeave.Core
  K1 validation
  K1 -> DIR1 compilation
  E1 application over neutral workspace state
          ^
          |
MetaWeave application / CLI
  acquire K1 and contracts through surfaces
  acquire input workspace through a surface
  invoke Core
  present diagnostics
  publish successful output through a surface

Meta.Operations
  GenericModel and neutral workspace state
```

**IM-1 (D; `EX-3`, `EX-13`).** `MetaWeave.Core` references
`MetaWeave.Model` and representation-neutral Meta contracts only. It has no
reference to `Meta.Integration`, XML/SQL/C# surfaces, filesystem APIs, database
clients, command parsing, or presentation. Application projects may reference
those packages.

**IM-2 (C; realizes `EX-15`).** `DIR1` is initially an immutable in-process
object interpreted by Core. Durable plans, generated executors, and
cross-process caches are deferred until measured compilation cost requires
them.

## Exact Runtime Contract Binding

Core receives, rather than discovers, this responsibility:

```csharp
public sealed record WorkspaceSemanticContract(
    ContractIdentity Identity,
    GenericModelSnapshot Model,
    ImmutableArray<SignificantOrderContract> SignificantOrders,
    IWorkspaceValidator Validator,
    IWorkspaceStateEquality StateEquality,
    IWorkspaceSemanticEquivalence SemanticEquivalence,
    IComparer<string> IdentityOrder,
    IWorkspaceCanonicalizer? Canonicalizer);
```

`GenericModelSnapshot` is a deep immutable projection of a validated
`Meta.Operations.Domain.GenericModel`: entity, member, relationship,
and optionality declarations are copied into value collections before the
contract identity is computed. `SignificantOrders` supplies any modeled order
facts not currently carried by `GenericModel`. Retaining a caller-mutable
`GenericModel` reference in `DIR1` would violate program immutability.

The interfaces are model-owned semantic contracts, not delegates embedded in
authored `K1`. `IdentityOrder` supplies deterministic processing and diagnostic
ordering for modeled record identities; it does not make record order
significant.

Each semantic-contract service is immutable and referentially transparent, and
its semantic revision participates in `ContractIdentity`. It may inspect only
the supplied contract or workspace; it cannot consult a registry, process
state, environment, clock, network, or persistence surface.

The first compilation profile supports directions only for contract bindings
for which:

- complete validity is supplied by the neutral structural validator;
- semantic equivalence is state equality;
- modeled property values are neutral Unicode text with ordinal equality;
- modeled identities are text with ordinal deterministic ordering; and
- no canonicalizer is required.

**IM-3 (C; realizes `EX-1`, `EX-2`, `EX-10`, `EX-11`).** Contract identity is a
versioned deterministic signature over every neutral model distinction used by
validity and state equality. A richer future signature must additionally name
the revision of nontrivial validity, equivalence, ordering, or canonicalization
semantics. Model names are diagnostic labels, not compatibility evidence.

## Sanctioned `K1` Meta Model

The first sanctioned product model contains these exact logical entities. The
table names are proposed Meta entity names, not informal responsibilities.

### Revision and contract records

| Entity | Required content |
| --- | --- |
| `Correspondence` | `Id` stable across revisions. |
| `CorrespondenceRevision` | `Id`, relationship to `Correspondence`, immutable `Revision`. |
| `ContractBinding` | `Id`, relationship to revision, `Role` (`Source` or `Target`), exact `ContractIdentity`, diagnostic `ModelName`. Exactly one of each role per revision. |

### First-class association records

| Entity | Required content |
| --- | --- |
| `EntityAssociation` | Revision, source entity identifier, target entity identifier. |
| `PropertyAssociation` | Revision, exact source entity/property and target entity/property identifiers. |
| `RelationshipAssociation` | Revision, exact source entity/relationship and target entity/relationship identifiers. |
| `OrderAssociation` | Revision and exact source and target entity-order identifiers. |

A revision must own at least one association. Directional records reference
these entities directly; they do not repeat association meaning in strings.

### Direction and domain records

| Entity | Required content |
| --- | --- |
| `Direction` | Revision and `Orientation` reference (`Forward` or `Reverse`); at most one per orientation. |
| `DomainConjunction` | Exactly one per direction; zero predicate children represents `AllValid`. |
| `EveryRecordPropertyPresent` | Domain, exact input entity/property identifiers. |
| `EveryRecordPropertyEquals` | Domain, exact input entity/property identifiers and `LiteralText`. |
| `EveryRecordRelationshipPresent` | Domain, exact input entity/relationship identifiers. |

`Orientation` is a closed reference value, not a generic kind-and-payload node.
Each predicate variant has only the fields meaningful to that variant.

### Rule and assignment records

| Entity | Required content |
| --- | --- |
| `MapEachRule` | Direction, `EntityAssociation`, exact input and output entity identifiers. |
| `ConstructEmptyRule` | Direction and exact output entity identifier. |
| `CopyIdentityAssignment` | Exactly one per `MapEachRule`. |
| `CopyPropertyAssignment` | Rule, `PropertyAssociation`, exact input and output property identifiers. |
| `ConstantPropertyAssignment` | Rule, `LiteralText`, exact output property identifier. |
| `AbsentPropertyAssignment` | Rule and exact optional output property identifier. |
| `CopyRelationshipAssignment` | Rule, `RelationshipAssociation`, exact input/output relationships, referenced `MapEachRule`. |
| `AbsentRelationshipAssignment` | Rule and exact optional output relationship identifier. |
| `PreserveOrderAssignment` | Rule and `OrderAssociation`. |
| `IgnoreOrderAssignment` | Rule whose output entity has insignificant order. |

The validator treats `MapEachRule` and `ConstructEmptyRule` as a closed union.
The assignment entities are closed typed variants. There is no expression
string, converter type name, script body, or generic operation payload.

### Input concepts, coverage, and loss

| Entity | Required content |
| --- | --- |
| `EntityPopulationConcept` | Direction and exact input entity identifier. |
| `IdentityConcept` | Direction and exact input entity identity. |
| `PropertyConcept` | Direction and exact input entity/property identifiers. |
| `RelationshipConcept` | Direction and exact input entity/relationship identifiers. |
| `SignificantOrderConcept` | Direction and exact input entity-order identifier. |
| `PreservedInputCoverage` | One input-concept variant and exact preserving rule-component identity. |
| `LostInputCoverage` | One input-concept variant and `LossDeclaration`. |
| `LossDeclaration` | Direction, stable loss identity, explanation. |

Exactly one coverage record owns every variable input concept. The preserving
component identity is validated against a typed rule or assignment; it is not
runtime reflection.

### Claim records

| Entity | Required content |
| --- | --- |
| `CanonicalizationClaim` | Revision, exact contract identity, canonicalizer identity, comparison reference. |
| `RecoveryClaim` | Revision, side, recovery-domain conjunction, comparison reference, optional canonicalizer identity. |

Claim records are authorable in the model but are not accepted as capability
evidence.

`LiteralText` is the exact Unicode text value from the closed `K1` literal
algebra, not an expression or surface-specific spelling. A later scalar kind
requires a new typed entity and a predecessor language revision.

**IM-4 (C; realizes the complete `K1` grammar).** The sanctioned model contains
one typed entity for every grammar variant above. Model review must demonstrate
that the customer witness can be serialized without paths, opaque payloads, or
unstated defaults before typed views are generated.

## Immutable `DIR1` .NET Values

The in-process IR maps directly to these value shapes:

```csharp
public sealed record CompilationHeader(
    LanguageVersion K1LanguageVersion,
    CompilationProfileIdentity CompilerProfile,
    CorrespondenceIdentity Correspondence,
    CorrespondenceRevision Revision,
    ContractIdentity SourceContract,
    ContractIdentity TargetContract);

public sealed record CorrespondenceCapabilities(
    DirectionalCapabilities Forward,
    DirectionalCapabilities Reverse);

public abstract record DirectionalCapabilities;
public sealed record NoProductCapabilities(
    NoProductReason Reason,
    ImmutableArray<UnsupportedFeature> RequiredFeatures)
    : DirectionalCapabilities;
public sealed record ProductCapabilities(
    bool DomainTotal,
    bool CompleteOutput,
    ImmutableArray<CompiledLoss> DeclaredLosses)
    : DirectionalCapabilities;

public sealed record CompiledCorrespondence(
    CompilationHeader Header,
    DirectionAssessment Forward,
    DirectionAssessment Reverse,
    CorrespondenceCapabilities Capabilities,
    ImmutableArray<CompiledClaimAssessment> ClaimAssessments,
    CompilationEvidence Evidence);

public sealed record CompilationEvidence(
    ImmutableArray<AssociationResolution> Associations);

public sealed record AssociationResolution(
    AssociationIdentity Association,
    AssociationKind Kind,
    ModelEndpoint Source,
    ModelEndpoint Target,
    ImmutableArray<ProgramComponentIdentity> Citations);

public sealed record CompiledClaimAssessment(
    ClaimIdentity Claim,
    ClaimKind Kind,
    ClaimAssessment Assessment);

public abstract record ClaimAssessment;
public sealed record EstablishedClaim(ClaimEvidence Evidence) : ClaimAssessment;
public sealed record RefutedClaim(ClaimCounterexample Counterexample) : ClaimAssessment;
public sealed record UnresolvedClaim(string Reason) : ClaimAssessment;
public sealed record NotApplicableClaim : ClaimAssessment;

public abstract record DirectionAssessment
{
    public sealed record Absent : DirectionAssessment;
    public sealed record Unsupported(
        ImmutableArray<UnsupportedFeature> RequiredFeatures,
        ImmutableArray<CompilationDiagnostic> Diagnostics) : DirectionAssessment;
    public sealed record Compiled(
        CompiledDirection Program) : DirectionAssessment;
}

public sealed record CompiledDirection(
    ProgramIdentity Identity,
    Orientation Orientation,
    WorkspaceSemanticContract InputContract,
    WorkspaceSemanticContract OutputContract,
    CompiledDomain Domain,
    ImmutableArray<CompiledConstructor> Constructors,
    ImmutableArray<CompiledInputFate> InputFates,
    ImmutableArray<CompiledLoss> Losses,
    CoverageCertificate Coverage);
```

```csharp
public sealed record CompiledDomain(
    ImmutableArray<CompiledDomainTest> AllOf);

public sealed record CompiledTextLiteral(string Value);

public abstract record CompiledDomainTest;
public sealed record PropertyPresentTest(
    EntityIdentity Entity,
    PropertyIdentity Property) : CompiledDomainTest;
public sealed record PropertyEqualsTest(
    EntityIdentity Entity,
    PropertyIdentity Property,
    CompiledTextLiteral Literal) : CompiledDomainTest;
public sealed record RelationshipPresentTest(
    EntityIdentity Entity,
    RelationshipIdentity Relationship) : CompiledDomainTest;

public abstract record CompiledConstructor;
public sealed record CompiledMapConstructor(
    ProgramComponentIdentity Identity,
    EntityIdentity InputEntity,
    EntityIdentity OutputEntity,
    CopyRecordIdentityWrite IdentityWrite,
    ImmutableArray<CompiledPropertyWrite> PropertyWrites,
    ImmutableArray<CompiledRelationshipWrite> RelationshipWrites,
    CompiledOrderWrite OrderWrite) : CompiledConstructor;
public sealed record CompiledEmptyConstructor(
    ProgramComponentIdentity Identity,
    EntityIdentity OutputEntity) : CompiledConstructor;

public sealed record CopyRecordIdentityWrite(
    ProgramComponentIdentity Identity);

public abstract record CompiledPropertyWrite;
public sealed record CopyScalarWrite(
    ProgramComponentIdentity Identity,
    PropertyIdentity InputProperty,
    PropertyIdentity OutputProperty) : CompiledPropertyWrite;
public sealed record ConstantScalarWrite(
    ProgramComponentIdentity Identity,
    CompiledTextLiteral Literal,
    PropertyIdentity OutputProperty) : CompiledPropertyWrite;
public sealed record AbsentScalarWrite(
    ProgramComponentIdentity Identity,
    PropertyIdentity OutputProperty) : CompiledPropertyWrite;

public abstract record CompiledRelationshipWrite;
public sealed record CopyReferenceWrite(
    ProgramComponentIdentity Identity,
    RelationshipIdentity InputRelationship,
    RelationshipIdentity OutputRelationship,
    ProgramComponentIdentity ReferencedConstructor) : CompiledRelationshipWrite;
public sealed record AbsentReferenceWrite(
    ProgramComponentIdentity Identity,
    RelationshipIdentity OutputRelationship) : CompiledRelationshipWrite;

public abstract record CompiledOrderWrite;
public sealed record CopyRecordOrderWrite(
    ProgramComponentIdentity Identity) : CompiledOrderWrite;
public sealed record IgnoreIncidentalOrderWrite(
    ProgramComponentIdentity Identity) : CompiledOrderWrite;

public abstract record CompiledInputDisposition;
public sealed record PreservedInput(
    ProgramComponentIdentity Component) : CompiledInputDisposition;
public sealed record LostInput(
    LossIdentity Loss) : CompiledInputDisposition;

public sealed record CompiledInputFate(
    InputConceptIdentity Concept,
    CompiledInputDisposition Disposition);
public sealed record CompiledLoss(
    LossIdentity Identity,
    ImmutableArray<InputConceptIdentity> Concepts,
    string Explanation);
public sealed record CoverageCertificate(
    ImmutableDictionary<EntityIdentity, ProgramComponentIdentity> OutputEntityOwners,
    ImmutableDictionary<EntityIdentity, ProgramComponentIdentity> EmptyPopulationProofs,
    ImmutableDictionary<EntityIdentity, ProgramComponentIdentity> OutputIdentityOwners,
    ImmutableDictionary<PropertyIdentity, ProgramComponentIdentity> OutputPropertyOwners,
    ImmutableDictionary<RelationshipIdentity, ProgramComponentIdentity> OutputRelationshipOwners,
    ImmutableDictionary<OrderIdentity, ProgramComponentIdentity> OutputOrderOwners,
    ImmutableDictionary<InputConceptIdentity, CompiledInputFate> InputFateOwners);
```

These are closed values rather than extension dictionaries. The small identity,
diagnostic, and evidence value types named above carry only the corresponding
semantic identifiers and records already defined by `K1` or `DIR1`.

**IM-5 (D; `EX-1`, `EX-5`, `EX-6`).** Every concrete IR variant is immutable
and has one executor branch. No IR type carries `Func`, `Delegate`, service
locator, path, surface descriptor, or arbitrary payload.

## Core Service Interfaces

```csharp
public abstract record K1ValidationResult;
public sealed record Validated(
    ValidatedK1 Correspondence) : K1ValidationResult;
public sealed record Invalid(
    ImmutableArray<MetaWeaveDiagnostic> Diagnostics) : K1ValidationResult;
public sealed record ValidationUnsupported(
    ImmutableArray<UnsupportedFeature> RequiredFeatures,
    ImmutableArray<MetaWeaveDiagnostic> Diagnostics) : K1ValidationResult;

public abstract record ApplicationResult;
public sealed record ApplicationSucceeded(
    InMemoryWorkspace Output,
    ApplicationEvidence Evidence) : ApplicationResult;
public sealed record ApplicationFailed(
    FailureCode Code,
    ImmutableArray<MetaWeaveDiagnostic> Diagnostics) : ApplicationResult;

K1ValidationResult Validate(
    MetaWeaveModel document,
    WorkspaceSemanticContract source,
    WorkspaceSemanticContract target);

CompiledCorrespondence Compile(
    ValidatedK1 correspondence,
    CompilationProfile profile);

ApplicationResult Apply(
    CompiledDirection direction,
    InMemoryWorkspace input,
    CancellationToken cancellationToken);
```

- validation converts typed Meta records into validated `K1` values;
- compilation lowers validated `K1` to `DIR1`;
- application runs the `E1` machine over neutral state.

The compiler accepts only the `Validated` branch. `Invalid` means the authored
records refute a `K1` rule; `ValidationUnsupported` means this implementation
cannot establish a required language judgment and says nothing contrary about
the correspondence's semantic validity. The executor does not accept authoring
records. This prevents phase responsibility from being duplicated.

**IM-6 (D; `EX-4` through `EX-9`).** `Apply` owns an application-local
`ExecutionContext` containing the candidate builder, output index, pending
references, cursors, and evidence accumulator from `E1State`. Only its
`Succeeded` branch freezes and returns a workspace.

```csharp
internal enum ExecutionPhase
{
    Accept,
    TestDomain,
    ConstructRecords,
    ResolveRelationships,
    ApplyOrder,
    RecordLoss,
    ValidateCandidate,
    Succeeded,
    Failed
}

internal sealed class ExecutionContext
{
    public ExecutionPhase Phase { get; private set; }
    public required CompiledDirection Program { get; init; }
    public required InMemoryWorkspace Input { get; init; }
    public required CandidateWorkspaceBuilder Candidate { get; init; }
    public required OutputRecordIndex OutputIndex { get; init; }
    public required PendingReferenceList PendingReferences { get; init; }
    public required ApplicationEvidenceBuilder Evidence { get; init; }
}
```

Constructor and record cursors are local enumerators inside
`ConstructRecords`; they carry no correspondence decisions. The names above
are the direct implementation counterparts of the `E1State` fields and phases.

## Diagnostic Catalog

```text
MetaWeaveDiagnostic = (
  Code,
  Phase,
  Severity,
  CorrespondenceRevision,
  Direction?,
  AuthoredElement?,
  ModelEndpoint?,
  InputRecordIdentity?,
  MessageArguments)
```

Codes are stable; presentation text is not semantic. Record identity appears
only for data-dependent application failures. Applications may add editor or
file locations during presentation.

**IM-7 (C; realizes terminal `E1` failures).** The first catalog has distinct
codes for invalid correspondence, unsupported feature, invalid program,
contract mismatch, invalid input, outside domain, evaluation defect, invalid
output, and cancellation.

## First Compiler Profile

`K1-Core-1` supports every construct currently defined by the closed `K1`
grammar:

- association records for entities, properties, relationships, and order;
- `AllValid` and the three universal domain predicates;
- `MapEach` and `ConstructEmpty`;
- identity copy;
- property copy, typed constant, and explicit absence;
- relationship copy through an identity-copy map and explicit absence;
- significant-order preservation and explicit insignificant order;
- complete input coverage and declared loss; and
- canonicalization and recovery claim records, retained as assessment rows.

It supports no grouping, filtering, fan-out, arbitrary expressions, external
semantic functions, implicit defaults, repair, inference, composition,
incremental synchronization, or in-place update.

Recovery and canonicalization claims may be stored, but `K1-Core-1` reports
them `Unresolved` until the corresponding verifier is delivered. A validation
obligation requiring nontrivial semantic equivalence returns
`ValidationUnsupported` in this profile; it is not treated as invalid `K1` and
is never encoded as a narrower domain.

**IM-8 (C; realizes `EX-6` in the first delivery slice).** Relationship
construction is part of the first profile and the first forward conformance
slice. This is no longer an open question: without it, the slice would test
record copying rather than workspace-graph transformation.

## Application Boundary

An application performs this sequence:

```text
1. Acquire the authored MetaWeave workspace through a supported surface.
2. Acquire exact source and target model contracts.
3. Call Validate, then Compile.
4. Select one compiled direction.
5. Acquire one exact-contract input workspace.
6. Call Apply.
7. Present diagnostics or publish the successful neutral output through a
   caller-selected surface.
```

Paths and surface selections used by steps 1, 2, 5, and 7 are application
inputs. They are never copied into `K1`, `DIR1`, or `E1State`.

**IM-9 (D; `EX-13`, `EX-14`).** Applications own acquisition, direction
selection, cancellation, diagnostics presentation, and publication. They
cannot enlarge the compiled domain or promote recovery evidence.

## Customer Witness Mapping

The same witness now has a one-to-one implementation representation:

| Preceding abstraction | Meta/.NET realization |
| --- | --- |
| `K1 customer-party/1` | one `Correspondence`, one revision, two contract bindings |
| associations `A1` through `A5` | two entity, two property, and one relationship association records |
| total forward domain | one `Direction`, one empty `DomainConjunction` |
| rules `R1`, `R2` | two `MapEachRule` records with identity, scalar, relationship, and order assignment records |
| seven input-fate entries | seven input-concept records and seven `PreservedInputCoverage` records |
| no loss | no `LossDeclaration` or `LostInputCoverage` records |
| compiled witness | one `CompiledDirection` with two `CompiledMapConstructor` values |
| execution trace | one application-local `ExecutionContext` traversing all `E1` phases |

No implementation record in this table lacks a predecessor construct, and no
predecessor construct is represented by an opaque implementation field.

## Delivery Stages and Gates

| Stage | Concrete output | Gate before continuing |
| --- | --- | --- |
| 0. Ladder acceptance | Reviewed `K1`, `DIR1`, `E1`, implementation map, and conformance witness | Every rung passes its local predecessor check. |
| 1. In-memory witness | Customer `K1` represented with immutable test values | It validates without serialized model or hidden callbacks. |
| 2. Sanctioned Meta model | Reviewed model above and generated typed views | It represents the same witness one-for-one. |
| 3. Validator and compiler | `Validate` plus `K1-Core-1` lowering to the displayed `DIR1` | Negative authoring and exact lowering tests pass. |
| 4. Forward machine | `Apply` produces the displayed `PartyDirectory` result, including relationship resolution | All applicable `E1` laws and failure cases pass. |
| 5. Product value gate | One separately selected real product correspondence compared with a direct converter | Continue only when reuse or maintainability benefit justifies total framework cost. |
| 6. Independent reverse need | A real reverse `K1` definition and compiled program | Reverse value is demonstrated independently; no recovery wording is inferred. |
| 7. Recovery verifier | Explicit claim assessment over a closed supported case | Closure and comparison strength are reported separately from direction execution. |

The customer witness proves architecture, not product value. Before stage 5,
the exact real source/target pair and continuation threshold must be recorded.
No broad implementation or migration begins before the stage-5 decision.

## Value Measurement

**IM-10 (C; decision discipline).** The stage-5 record includes:

- authored `K1` record and concept count;
- validator, compiler, executor, model, and conformance cost attributable to the
  slice;
- diagnostic quality for invalid, unsupported, and outside-domain cases;
- implementation and review time for the direct converter and MetaWeave form;
- change cost when either endpoint contract evolves;
- reuse across another correspondence or independently needed direction;
- runtime and memory cost on representative workspaces; and
- semantic obligations made visible rather than hidden in converter code.

Sunk work and architectural elegance are not continuation criteria.

## Predecessor Validation

| `E1` abstraction | Implementation realization | Local validation |
| --- | --- | --- |
| immutable program and exact contracts | `CompiledDirection`, `WorkspaceSemanticContract` | Core receives both explicitly. |
| fresh machine state | application-local `ExecutionContext` | No state is stored in the program or input. |
| domain phase | typed domain-test evaluator | One branch exists for every `DIR1` test variant. |
| record and scalar phase | candidate builder plus typed constructors/writes | No authoring syntax is interpreted at runtime. |
| relationship phase | pending-reference list and output index | Resolution uses compiled constructor identifiers. |
| loss phase | evidence accumulator | Every compiled loss row is visited. |
| validation and atomic terminal states | neutral validator and result union | Only frozen validated state enters `Success`. |
| no ambient semantics | project references and architecture tests | Core cannot acquire or publish workspaces. |

## Open Decisions

- **IM-O1:** The real product source/target pair used at stage 5. This must be
  closed before product implementation begins; it is not needed to define the
  architecture witness.
- **IM-O2:** Whether deterministic semantic contract signatures belong in the
  generic Meta foundation or are initially derived inside MetaWeave.Core.
- **IM-O3:** The quantitative stage-5 continuation threshold.

No open decision is assumed by `K1-Core-1`, the customer witness, or the stage-4
acceptance gate.
