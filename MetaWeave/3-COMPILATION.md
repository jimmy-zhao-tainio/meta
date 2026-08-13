# MetaWeave Compilation

## Status

This is draft layer 3 of the MetaWeave specification ladder. It derives from
the concrete `K1` language in
[`2-CORRESPONDENCE-MODEL.md`](2-CORRESPONDENCE-MODEL.md) and remains
subordinate to [`1-KERNEL.md`](1-KERNEL.md).

The input to this layer is a `ValidatedK1` value retaining its exact contract
bindings. The output is a concrete immutable directional intermediate
representation, `DIR1`. This document defines the records and lowering relation
of `DIR1`; it does not select compiler passes, code generation, serialization,
or CLR types.

## Layer Question

The correspondence layer supplies abstract syntax and denotation. This layer
adds the next abstraction:

> What closed directional program can be derived from that syntax while
> preserving its denotation and exposing every decision needed by execution?

## Compilation Interface

```text
CompileK1(ValidatedK1, Profile) ->
  CompiledCorrespondence(
    Header,
    ForwardAssessment,
    ReverseAssessment,
    Capabilities,
    ClaimAssessments,
    Evidence)
```

```text
Header = (
  K1LanguageVersion,
  CompilerProfileId,
  CorrespondenceKey,
  CorrespondenceRevision,
  SourceContractIdentity,
  TargetContractIdentity)
```

`Profile` identifies the exact `K1` language version, syntax variants, and
bound-contract semantic features supported by a compiler. It is not a policy
for narrowing a direction's semantic domain.

For each orientation:

```text
DirectionAssessment ::=
    Absent
  | Unsupported(RequiredFeatures, Diagnostics)
  | Compiled(DIR1)
```

- `Absent` means no authored definition exists.
- `Unsupported` means the definition is valid `K1` but the selected compiler
  profile cannot lower one or more of its constructs.
- `Compiled` contains the immutable program whose denotation is the authored
  direction.

`ValidatedK1` retains the exact contract bindings used by validation. Invalid,
incomplete, or differently bound authored syntax cannot be passed through this
typed interface; `ValidateK1` rejects it before compilation. An unsupported
direction does not invalidate `K1` and does not discard a successfully compiled
opposite direction.

The outer evidence value has a closed structural form:

```text
CompilationEvidence = AssociationResolution(...)*

AssociationResolution = (
  AssociationId,
  AssociationKind,
  ResolvedSourceEndpoint,
  ResolvedTargetEndpoint,
  CitingProgramComponentIds)
```

Every authored association has one row, including an empty citation set when
no authored direction uses it. This is revision-to-program derivation evidence,
not an execution trace or a source of runtime decisions.

**CP-1 (D; `CM-13`, `CM-14`).** Compilation accepts only `ValidatedK1`, which
contains the exact contracts used by validation. There is no compiler overload
that accepts raw authored syntax or replacement contracts; names or structural
resemblance cannot substitute.

**CP-2 (D; `CM-2`, `CM-16`).** Compilation observes no workspace instance,
location, surface, artifact, environment, registry, or host callback. Its
profile is an explicit capability input and cannot change the denotation of a
supported construct.

**CP-3 (C; satisfies `CM-16`).** Direction support is assessed independently.
The compilation result may therefore contain zero, one, or two `DIR1` programs
even when both authored definitions are semantically valid.

## The `DIR1` Intermediate Representation

A compiled direction has this exact logical shape:

```text
DIR1 = (
  ProgramId,
  CorrespondenceKey,
  CorrespondenceRevision,
  Orientation,
  InputContract,
  OutputContract,
  DomainProgram,
  Constructors,
  InputFateTable,
  LossTable,
  CoverageCertificate
)
```

`InputContract` and `OutputContract` are the exact immutable semantic bindings
supplied during validation and compilation; their identities participate in
program identity. Retaining them prevents runtime lookup through an ambient
registry. `DIR1` otherwise contains resolved semantic identifiers. It contains
no authoring names that require runtime lookup and no executable delegate
hidden behind a node.

Association records lower into resolved compilation evidence:

| `K1` association | `CompilationEvidence` row |
| --- | --- |
| `EntityAssociation` | resolved source/target entity pair and every constructor that cites it |
| `PropertyAssociation` | resolved source/target property pair and every `CopyScalar` that cites it |
| `RelationshipAssociation` | resolved source/target relationship pair and every `CopyReference` that cites it |
| `OrderAssociation` | resolved source/target order pair and every `CopyRecordOrder` that cites it |

This evidence preserves first-class correspondence identity while constructors
carry the directional operations. It does not infer an operation from an
association that no direction cites.

### Domain program

The domain syntax is normalized to:

```text
DomainProgram = AllOf(DomainTest, ...)

DomainTest ::=
    PropertyPresent(EntityId, PropertyId)
  | PropertyEquals(EntityId, PropertyId, TypedLiteral)
  | RelationshipPresent(EntityId, RelationshipId)

TypedLiteral ::= TextValue(UnicodeString)
```

`AllValid` lowers to `AllOf()`; the empty conjunction is true. Contract
resolution gives each test its exact entity/member identity and scalar equality
semantics.

| `K1` domain atom | `DIR1` domain test |
| --- | --- |
| `EveryRecordPropertyPresent` | `PropertyPresent` |
| `EveryRecordPropertyEquals` | `PropertyEquals` |
| `EveryRecordRelationshipPresent` | `RelationshipPresent` |

**CP-4 (D; `CM-4`).** `DomainProgram` has the same membership denotation as the
authored domain formula. Lowering neither adds a hidden precondition nor treats
a compiler limitation as a smaller domain.

### Constructors

```text
Constructor ::=
    MapConstructor(
      ConstructorId,
      InputEntityId,
      OutputEntityId,
      IdentityWrite,
      PropertyWrites,
      RelationshipWrites,
      OrderWrite)
  | EmptyConstructor(
      ConstructorId,
      OutputEntityId)
```

```text
IdentityWrite = CopyRecordIdentity

PropertyWrite ::=
    CopyScalar(InputPropertyId, OutputPropertyId)
  | WriteConstant(TypedLiteral, OutputPropertyId)
  | WriteAbsent(OutputPropertyId)

RelationshipWrite ::=
    CopyReference(
      InputRelationshipId,
      OutputRelationshipId,
      ReferencedConstructorId)
  | WriteAbsentReference(OutputRelationshipId)

OrderWrite ::=
    CopyRecordOrder
  | IgnoreIncidentalOrder
```

Constructors are indexed by exact output entity. Every output entity has one
constructor. Property and relationship writes are indexed by exact output
member. These indexes are semantic lookup tables, not traversal-dependent
runtime inference. `CopyScalar` preserves exact presence and text value.
`CopyReference` preserves relationship presence and, when present, carries the
referenced input identity to the named output constructor.

**CP-5 (D; `CM-5`, `CM-6`, `CM-7`).** Each `MapEach` lowers to one
`MapConstructor`, and each `ConstructEmpty` lowers to one `EmptyConstructor`.
Every authored assignment lowers according to the tables below; no additional
write may be synthesized.

| `K1` construct | `DIR1` construct |
| --- | --- |
| `CopyInputIdentity(variable)` | `CopyRecordIdentity` on that variable's constructor |
| `CopyProperty(..., input, output)` | `CopyScalar(resolved input, resolved output)` |
| `Constant(literal, output)` | `WriteConstant(typed literal, resolved output)` |
| `Absent(output)` | `WriteAbsent(resolved output)` |
| `CopyRelationship(..., input, output, rule)` | `CopyReference(resolved input, resolved output, lowered rule)` |
| `AbsentRelationship(output)` | `WriteAbsentReference(resolved output)` |
| `PreserveOrder(...)` | `CopyRecordOrder` |
| `NoSignificantOutputOrder` | `IgnoreIncidentalOrder` |

### Input fate and loss tables

```text
InputFate = (
  InputConceptId,
  PreservedByProgramComponentId | LostByLossId)

CompiledLoss = (
  LossId,
  InputConceptIds,
  Explanation)
```

Association citations are retained in compilation evidence so an application
can explain which authored correspondence fact a write realizes. They do not
need to be rediscovered during execution.

**CP-6 (D; `CM-9`).** Every authored input-coverage entry lowers to exactly one
input-fate row. Every loss reference resolves to one compiled loss row.
Compilation rejects a lowering defect that drops or invents a row; it does not
repair authored coverage.

### Coverage certificate

The compiler emits a structural certificate:

```text
CoverageCertificate = (
  OutputEntityOwner[OutputEntityId -> ConstructorId],
  EmptyPopulationProof[EmptyOutputEntityId -> EmptyConstructorId],
  OutputIdentityOwner[MappedOutputEntityId -> IdentityWriteId],
  OutputPropertyOwner[MappedOutputPropertyId -> PropertyWriteId],
  OutputRelationshipOwner[MappedOutputRelationshipId -> RelationshipWriteId],
  OutputOrderOwner[MappedSignificantOrderId -> OrderWriteId],
  InputFateOwner[InputConceptId -> InputFateId]
)
```

The member-owner maps range only over entities owned by a `MapConstructor`.
For an `EmptyConstructor`, `EmptyPopulationProof` records both its ownership and
the validated fact that the exact output contract permits an empty population;
member coverage is then vacuous rather than fabricated.

This is finite evidence about the exact contracts and program. It is not a
runtime trace and does not claim that arbitrary future languages are decidable.

**CP-7 (D; `CM-8`, `CM-9`, `CM-14`).** Certificate construction succeeds only
when each required target fact, every empty-population proof, and every variable
input distinction has the owner required by valid `K1`. The certificate makes
that predecessor result available to the executor without asking it to
reinterpret authoring syntax.

## Denotation of `DIR1`

`DIR1` has a mathematical meaning independent of any production executor:

```text
[[DIR1]](W) is undefined
  when any DomainTest in AllOf(...) is false.

[[DIR1]](W) = O otherwise, where:
  1. each EmptyConstructor contributes an empty output entity set;
  2. each MapConstructor contributes one output record per input record;
  3. CopyRecordIdentity supplies its identity;
  4. property writes supply every scalar presence and value;
  5. a reference copy preserves input presence and, when present, targets the
     record with the copied identity produced by the named referenced
     constructor;
  6. order writes supply every significant output order; and
  7. the union of constructor contributions is O under OutputContract.
```

The coverage certificate establishes that this construction is structurally
closed. The input-fate and loss tables describe the fate of input distinctions
but do not alter `O`.

## Lowering Relation

Lowering is defined by a relation rather than a prescribed sequence of passes:

```text
Lower(Profile, ValidatedK1, Delta_X) = DIR1
```

The relation holds only when all of these checks hold:

1. every association, entity, property, relationship, order, and rule reference
   resolves to its exact validated identity;
2. `Profile` supports every syntax form used by `Delta_X`;
3. the domain, constructor, assignment, fate, and loss mappings follow the
   tables in this document;
4. the coverage certificate is complete;
5. the denotation-preservation obligation below is established for the closed
   mappings.

The explanatory ordering of checks is not an implementation algorithm.

**CP-8 (D; `CM-10`).** Denotation preservation is the defining compiler law:

```text
for every valid W in [[Delta_X.Domain]]:
  [[DIR1]](W) ≡_M_out [[Delta_X]](W)

and

W outside [[Delta_X.Domain]]
  iff
W outside [[DIR1.DomainProgram]]
```

The `E1` machine in [`4-EXECUTION.md`](4-EXECUTION.md) must realize this already
defined meaning. A compiler optimization is conformant only when this law
remains true.

**CP-9 (D; `CM-11`).** Lowering one direction reads no rule from the opposite
direction. Shared associations may appear in evidence for both programs, but
they do not manufacture reverse construction.

## Capabilities and Claims

For each direction the compiler reports:

```text
CorrespondenceCapabilities = (
  ForwardCapabilities,
  ReverseCapabilities)

DirectionalCapabilities ::=
    NoProduct(Absent)
  | NoProduct(Unsupported, RequiredFeatures)
  | ProductCapabilities(
      DomainTotal,
      CompleteOutput,
      DeclaredLosses)
```

- the capability variant must agree with the direction assessment;
- `DomainTotal` is true when `AllOf(...)` is a tautology over the exact valid
  input workspace space. `AllOf()` is always total; a presence test over a
  required member may also be total. If tautology cannot be established, the
  capability is not reported as total.
- `CompleteOutput` is true for every `ProductCapabilities`, because incomplete `K1`
  never enters lowering and coverage is certified.
- `DeclaredLosses` is the compiled loss table; losslessness means it is empty.
- `RequiredFeatures` occurs only in the unsupported no-product variant.

Claim evidence is separate:

```text
ClaimAssessment ::= Established(Evidence)
                  | Refuted(Counterexample)
                  | Unresolved(Reason)
                  | NotApplicable

ClaimAssessmentRow = (
  ClaimId,
  CanonicalizationClaim | RecoveryClaim,
  ClaimAssessment)
```

Every authored claim has exactly one assessment row. `NotApplicable` is used
only when the named prerequisite does not exist, for example a recovery claim
whose required direction is absent; absence of a verifier is `Unresolved`, not
`NotApplicable`.

**CP-10 (D; `CM-12`, `CM-15`).** Recovery is established only when both required
programs exist, recovery-domain inclusion and opposite-domain closure are
established, and the claimed round-trip comparison is established. Product
count alone supplies no recovery evidence.

**CP-11 (D; `CM-9`, `CM-15`).** Losslessness and totality are derived from the
compiled structures, not author-assigned labels. A loss declaration cannot
establish recovery or canonicalization.

## Product Identity and Immutability

**CP-12 (C; realizes `CM-10`, supports `K-L`).** A `DIR1` value is immutable.
Its semantic identity is determined by the correspondence key and revision,
orientation, exact contract identities, normalized domain program,
constructors, fate and loss tables, certificate, and compiler-language version.
Reordering authored collections declared insignificant does not change this
identity; changing a semantic component does.

The digest algorithm and physical object representation remain implementation
choices. A semantic identity cannot contain a path, timestamp, random value, or
process-local object identity.

## Diagnostics

**CP-13 (C; realizes `CM-13`, `CM-14`, `CM-16`).** Compilation diagnostics have
stable codes and identify phase, orientation, correspondence revision, authored
element, resolved endpoint when available, and unsupported syntax or contract
feature when applicable. Applications may attach physical editor locations
without making them semantic identifiers.

**CP-14 (D; `CM-16`).** An unsupported language or bound-contract feature
returns `Unsupported`; it never becomes an opaque delegate, best-effort plan,
or implicit domain restriction.

## Compiled Customer Witness

For a profile supporting all `K1` constructs, the customer correspondence from
the preceding layer lowers to:

```text
DIR1 customer-party/1/F
  input:  SalesCatalog
  output: PartyDirectory

  domain:
    AllOf()

  constructors:
    C1 MapConstructor Region -> Territory
       identity: CopyRecordIdentity
       Label: CopyScalar Region.Name -> Territory.Label
       order: IgnoreIncidentalOrder

    C2 MapConstructor Customer -> Party
       identity: CopyRecordIdentity
       Name: CopyScalar Customer.DisplayName -> Party.Name
       Territory: CopyReference Customer.Region
                  -> Party.Territory via C1
       order: IgnoreIncidentalOrder

  input-fate:
    Region.population       -> C1
    Region.identity         -> C1.identity
    Region.Name             -> C1.Label
    Customer.population     -> C2
    Customer.identity       -> C2.identity
    Customer.DisplayName    -> C2.Name
    Customer.Region         -> C2.Territory

  loss-table: empty

  certificate:
    Territory -> C1
    Territory.identity -> C1.identity
    Territory.Label -> C1.Label
    Party -> C2
    Party.identity -> C2.identity
    Party.Name -> C2.Name
    Party.Territory -> C2.Territory
    every input concept -> exactly one input-fate row
```

The surrounding `CompiledCorrespondence` reports:

```text
ForwardAssessment = Compiled(customer-party/1/F)
ReverseAssessment = Absent

ForwardCapabilities = ProductCapabilities(
  DomainTotal = true
  CompleteOutput = true
  DeclaredLosses = empty
)

ReverseCapabilities = NoProduct(Absent)

ClaimAssessments = none
```

It also contains these five `CompilationEvidence` rows:

```text
A1 Region <-> Territory -> C1
A2 Region.Name <-> Territory.Label -> C1.Label
A3 Customer <-> Party -> C2
A4 Customer.DisplayName <-> Party.Name -> C2.Name
A5 Customer.Region <-> Party.Territory -> C2.Territory
```

This artifact is more specific than the authored `K1`: names are resolved,
domain syntax is normalized, assignments are executable IR operations, target
ownership is indexed, and coverage is certified. It adds no correspondence
meaning.

## Predecessor Validation

| `K1` abstraction | `DIR1` realization | Local validation |
| --- | --- | --- |
| Exact contracts and concepts | Resolved semantic identifiers | Every identifier traces to its authored endpoint. |
| Domain formula | `AllOf(DomainTest...)` | Membership denotation is equal. |
| Entity and assignment rules | Constructors and writes | The lowering table is total for the selected profile. |
| Input coverage and loss | Fate and loss tables | Row-for-row preservation is checked. |
| Complete target coverage | Coverage certificate | Every exact output fact has its required owner. |
| Independent directions | Independent assessments and programs | One unsupported direction cannot erase the other. |
| Claims remain claims | Separate assessments | No product-count inference occurs. |

For the customer witness, each authored record has one displayed `DIR1`
counterpart and the compiled capability values can be recomputed from the shown
program. This is the local evidence that layer 3 contributes lowering and an IR
rather than a restatement of layer 2.
