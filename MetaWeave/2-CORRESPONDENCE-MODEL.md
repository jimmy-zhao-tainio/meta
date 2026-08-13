# MetaWeave Correspondence Model

## Status

This is draft layer 1 of the MetaWeave specification ladder. It is subordinate
to [`1-KERNEL.md`](1-KERNEL.md) and follows [`0-LADDER.md`](0-LADDER.md).

The input to this layer is the kernel vocabulary. The output is a concrete
logical correspondence language, `K1`. `K1` is abstract syntax with a defined
meaning; it is not a serialized Meta model, compiler plan, or executable
function.

This revision deliberately defines a small closed language. Later language
versions may add grouping, fan-out, computed terms, or richer predicates only
by extending this layer and then revalidating every later rung. Their absence
from `K1` does not narrow the kernel's possible future meaning.

## Layer Question

The kernel says that `K` is authored correspondence truth between exact source
and target contracts. This layer makes that statement concrete:

> What records can an author state, and what workspace relation does each
> record denote, before any compiler or executor exists?

## The `K1` Document

A validated correspondence revision has this logical shape:

```text
K1 = (
  Key,
  Revision,
  SourceContract,
  TargetContract,
  Associations,
  Forward?,
  Reverse?,
  Claims
)
```

- `Key` identifies the correspondence across revisions.
- `Revision` identifies one immutable authored meaning.
- `SourceContract` and `TargetContract` bind exact model contracts `M_S` and
  `M_T`.
- `Associations` is a non-empty set of first-class correspondence facts.
- `Forward` and `Reverse` are optional, independently authored directional
  definitions.
- `Claims` contains optional canonicalization and recovery claims.

The non-empty association set means that a valid zero-direction `K1` still
states correspondence truth. It is not merely a name joining two contracts.

## Association Algebra

An association connects exact modeled concepts without itself defining an
executable direction:

```text
Association ::=
    EntityAssociation(
      Id, SourceEntity, TargetEntity)
  | PropertyAssociation(
      Id, SourceEntity.Property, TargetEntity.Property)
  | RelationshipAssociation(
      Id, SourceEntity.Relationship, TargetEntity.Relationship)
  | OrderAssociation(
      Id, SourceEntity.Order, TargetEntity.Order)
```

An association asserts only that the two concepts participate in the authored
correspondence. It does not assert value equality, define a consistency
relation, select a direction, or imply recovery. Directional definitions cite
associations when they preserve corresponding concepts.

**CM-1 (D; `K-C`, `K-W`).** Every association endpoint resolves against the
exact source or target contract bound by the same `K1` revision. Contract
identity includes every modeled distinction that affects validity, equality,
equivalence, significant order, or transformation meaning.

A property or order association requires an entity association for its owning
entities. A relationship association requires both an entity association for
its owning entities and one for its target entities. These requirements make
the association set a coherent model correspondence rather than an unrelated
bag of member pairs.

**CM-2 (D; `K-B`).** Contract bindings and association endpoints contain no
workspace path, surface, table, file, CLR type, connection string, acquisition
policy, or publication policy.

**CM-3 (C; satisfies `K-C`, preserves `K-P`).** A valid `K1` contains at least
one association. It may contain neither directional definition. This is the
concrete zero-product correspondence case: correspondence is authored, while
no executable direction is promised.

## Directional Definition Algebra

For orientation `X`, a directional definition is:

```text
Delta_X = (
  DirectionId,
  Orientation,
  Domain,
  EntityRules,
  InputCoverage,
  LossDeclarations
)
```

The orientation fixes which bound contract is input and which is output. The
same syntax is used independently for forward and reverse definitions.

```text
Forward: Input = M_S, Output = M_T
Reverse: Input = M_T, Output = M_S
```

Association citations follow that orientation. A reverse rule citing an entity
association reads its target endpoint and constructs its source endpoint; this
does not derive the reverse rule from the association or from the forward rule.

### Domain formulas

`K1` has a closed domain language:

```text
Domain ::=
    AllValid
  | All(DomainAtom, ...)

DomainAtom ::=
    EveryRecordPropertyPresent(InputEntity.Property)
  | EveryRecordPropertyEquals(
      InputEntity.Property, Literal)
  | EveryRecordRelationshipPresent(
      InputEntity.Relationship)

Literal ::= Text(UnicodeString)
```

`AllValid` denotes the complete valid input workspace space. `All(...)` is
conjunction. Each atom quantifies over every record of its exact input entity.
The empty record set satisfies these universal predicates. Equality to a
literal uses the exact input contract's scalar-value equality. `K1`
deliberately has only a neutral text scalar; adding numeric, temporal, binary,
or structured scalar kinds requires a later language version.

**CM-4 (D; `K-D`, `K-E`, `K-L`).** The denotation `[[Domain]]` is an explicit,
decidable subset of the valid input workspace space and is invariant under the
input contract's semantic equivalence. A construction precondition is either
represented in this formula or ruled out structurally by the language; it may
not become an ordinary failure after an input has been admitted.

### Entity rules

Every output entity has exactly one rule in a complete direction:

```text
EntityRule ::=
    MapEach(
      RuleId,
      EntityAssociationId,
      InputEntity as Variable,
      OutputEntity,
      IdentityAssignment,
      PropertyAssignments,
      RelationshipAssignments,
      OrderAssignment)
  | ConstructEmpty(
      RuleId,
      OutputEntity)
```

`MapEach` constructs exactly one output record for every record of the named
input entity. `ConstructEmpty` constructs no records and is permitted only
when the exact output contract allows that entity's record set to be empty.
Grouping, joins, filtering, and fan-out are not expressible in `K1`.

**CM-5 (C; satisfies `K-W`, `K-L`).** Exactly one rule owns each output entity
in `K1`. This is a language design choice that makes record ownership and
collision analysis closed; it is not promoted to a universal kernel fact.

### Identity and property assignments

The construction terms are also closed:

```text
IdentityAssignment ::=
    CopyInputIdentity(Variable)

PropertyAssignment ::=
    CopyProperty(
      PropertyAssociationId,
      Variable.InputProperty,
      OutputProperty)
  | Constant(
      Literal,
      OutputProperty)
  | Absent(
      OutputProperty)
```

For a `MapEach` rule, every modeled output property has exactly one assignment.
`Absent` is legal only for an optional output property. `Constant` is explicit
modeled construction; it is not a runtime default. `CopyProperty` must cite the
exact property association that it realizes and copies the input property's
exact presence and text value. A `ConstructEmpty` rule needs no member
assignments because it constructs no record on which a member could occur.

**CM-6 (D; `K-I`, `K-L`).** Every output identity and property value is
determined by one visible term. Identity allocation, arbitrary expressions,
host callbacks, and implicit defaults are outside `K1`.

### Relationship and order assignments

```text
RelationshipAssignment ::=
    CopyRelationship(
      RelationshipAssociationId,
      Variable.InputRelationship,
      OutputRelationship,
      ReferencedEntityRuleId)
  | AbsentRelationship(
      OutputRelationship)

OrderAssignment ::=
    PreserveOrder(OrderAssociationId)
  | NoSignificantOutputOrder
```

`CopyRelationship` reads the referenced input identity and targets the output
record with that identity constructed by `ReferencedEntityRuleId`. That rule
must use `CopyInputIdentity`. Absence is legal only for an optional output
relationship. `PreserveOrder` is required when corresponding significant order
is retained. `NoSignificantOutputOrder` is legal only when the output entity's
contract declares order insignificant.

If the input relationship of `CopyRelationship` is absent, the output
relationship is absent. That case is legal only when the output relationship
is optional; otherwise the direction's domain must guarantee input presence.

**CM-7 (D; `K-W`, `K-E`, `K-I`, `K-L`).** Relationship target derivation,
absence, requiredness, cardinality, and significant order are explicit. Name
similarity, `...Id` convention, observed values, reflection, and traversal
order have no semantic role.

For `CopyRelationship`, the input relationship's target entity must equal the
referenced rule's input entity, and the output relationship's target entity
must equal that rule's output entity. This makes target resolution a closed
consequence of the authored rules.

## Coverage and Loss Algebra

Target coverage is closed by the entity rules and assignments: every output
entity has one rule; a `MapEach` rule assigns every identity, property,
relationship, and significant-order position; and a `ConstructEmpty` rule
carries proof that the output contract admits an empty entity population.

Input coverage is an authored ledger. "Input" is relative to the direction: it
means `M_S` for forward and `M_T` for reverse.

```text
InputCoverageEntry ::=
    Preserved(InputConcept, RuleComponentId)
  | Lost(InputConcept, LossDeclarationId)

InputConcept ::=
    EntityPopulation(InputEntity)
  | Identity(InputEntity)
  | Property(InputEntity.Property)
  | Relationship(InputEntity.Relationship)
  | SignificantOrder(InputEntity)

LossDeclaration = (
  LossDeclarationId,
  InputConcepts,
  Explanation)
```

`Preserved` points to the exact construction component that carries the
distinction. `Lost` points to one directional declaration. Reading a concept in
a domain predicate does not by itself preserve it.

**CM-8 (D; `K-W`, `K-L`).** Complete target coverage is checked against the
exact output contract, including optional members, rather than only the members
mentioned by an author.

**CM-9 (D; `K-I`).** Every modeled input distinction that can vary inside the
declared domain has exactly one fate in the input ledger. Loss is directional,
explicit, and attributable. A loss declaration records behavior; it grants no
permission to construct invalid output or advertise recovery.

## Denotation of a Direction

The meaning of `Delta_X` is a mathematical partial function, not an executable
callback. For a valid input workspace `W`:

```text
[[Delta_X]](W) is undefined
  when W is not in [[Domain]].

[[Delta_X]](W) = O
  when W is in [[Domain]], where O is constructed as follows:

  1. ConstructEmpty contributes an empty output entity set.
  2. MapEach contributes one output record per bound input record.
  3. CopyInputIdentity determines its identity.
  4. Property assignments determine every output property.
  5. Relationship assignments resolve against records contributed by the
     referenced entity rules.
  6. Order assignments determine every significant output order.
  7. The union of rule contributions is the complete output workspace.
```

The definition is valid only when this denotation produces one complete valid
output for every valid input admitted by the domain.

**CM-10 (D; `K-P`, `K-L`).** `[[Delta_F]]` and `[[Delta_G]]`, when present, have
the signatures of `F_K` and `G_K`. They define the authored meaning a compiler
must realize; they are not executable products themselves.

**CM-11 (D; `K-P`).** Forward and reverse definitions are independently
authored. They may cite the same associations, but neither definition is
derived by reading the other backward. Their domains, rules, input coverage,
and loss remain independent. Two present definitions imply no recovery.

## Claims

```text
Claim ::=
    CanonicalizationClaim(
      ContractId, CanonicalizerId, Comparison)
  | RecoveryClaim(
      Side,
      RecoveryDomain,
      Comparison,
      CanonicalizerId?)

Comparison ::= StateEquality | SemanticEquivalence
```

The recovery domain uses a domain formula over the claimed side. A canonical
claim references model-owned semantics; it does not contain implementation
code.

**CM-12 (D; `K-N`, `K-R`).** A recovery claim explicitly records its side,
domain, comparison strength, and canonicalizer when applicable. Authorship does
not establish closure or the round-trip law.

## Validation Outcomes

This layer distinguishes authored-document state from correspondence truth:

```text
ValidateK1(document, M_S, M_T)
  -> Valid(ValidatedK1)
   | Invalid(diagnostics)
```

**CM-13 (C; satisfies `K-C`).** `K1` is valid when its identity and revision are
well formed, both exact contracts resolve, at least one association exists,
every present direction has valid closed syntax and denotation, claims are well
formed, and no forbidden boundary concept occurs.

**CM-14 (D; `K-D`, `K-L`, `K-I`).** A present direction is valid only when its
domain is explicit, all references resolve, target coverage is complete,
input coverage and loss are complete, relationship resolution is closed, and
every admitted valid input denotes a complete valid output. Its domain is
invariant under input semantic equivalence, and equivalent admitted inputs
denote equivalent outputs under the exact output contract. An incomplete
authored document may be saved, but it is not a validated `K1` revision.

Validation also checks scalar compatibility for copies, literal validity for
constants, member optionality for explicit absence, identity compatibility,
and every order constraint used by the denotation.

**CM-15 (D; `K-A`, `K-R`).** Validation establishes syntax and denotation, not
recovery. Claims remain `Unassessed` until compilation or a separate verifier
derives evidence. A refuted claim does not silently become a capability.

**CM-16 (C; preserves `K-B`).** `K1` is a versioned language boundary. A
compiler may support all or a declared subset of valid `K1`; compiler support
does not redefine correspondence validity. Opaque expressions or callbacks are
not an escape hatch for unsupported language.

`ValidateK1` above denotes the complete language judgment. A bounded concrete
validator that cannot establish one of its proof obligations must report that
validation feature as unsupported; it must not turn inability to decide into
`Invalid` or manufacture a `ValidatedK1` value.

## Concrete Witness: Customer Correspondence

All later rungs use this same witness.

```text
M_S = SalesCatalog
  Region(Id, Name)
  Customer(Id, DisplayName, Region -> Region required)

M_T = PartyDirectory
  Territory(Id, Label)
  Party(Id, Name, Territory -> Territory required)

Id denotes modeled record identity. All displayed scalar properties are
required.
Significant order: none in either contract
```

The authored `K1` is:

```text
Key: customer-party
Revision: 1

Associations:
  A1 EntityAssociation(Region, Territory)
  A2 PropertyAssociation(Region.Name, Territory.Label)
  A3 EntityAssociation(Customer, Party)
  A4 PropertyAssociation(Customer.DisplayName, Party.Name)
  A5 RelationshipAssociation(Customer.Region, Party.Territory)

Forward:
  Domain: AllValid

  R1 MapEach A1 Region as region -> Territory
     identity: CopyInputIdentity(region)
     Label: CopyProperty A2 region.Name
     order: NoSignificantOutputOrder

  R2 MapEach A3 Customer as customer -> Party
     identity: CopyInputIdentity(customer)
     Name: CopyProperty A4 customer.DisplayName
     Territory: CopyRelationship A5 customer.Region via R1
     order: NoSignificantOutputOrder

  InputCoverage:
     Region population -> Preserved by R1
     Region identity -> Preserved by R1 identity
     Region.Name -> Preserved by R1.Label
     Customer population -> Preserved by R2
     Customer identity -> Preserved by R2 identity
     Customer.DisplayName -> Preserved by R2.Name
     Customer.Region -> Preserved by R2.Territory

  LossDeclarations: none

Reverse: absent
Claims: none
```

This witness is forward-only, total, lossless for the modeled input concepts,
and silent about recovery. Those properties follow from the authored records;
they are not labels attached by the example.

## Predecessor Validation

| Kernel obligation | Concrete `K1` realization | Check |
| --- | --- | --- |
| First-class `K` | Non-empty association algebra plus immutable revision | A zero-direction witness can still contain actual correspondence facts. |
| Zero, one, or two products | Optional independent directional definitions | Presence is represented without claiming compiler availability. |
| Explicit partial domain | Closed domain formula | Its denotation is a subset of exact valid input state. |
| Complete valid output | Entity rules and exhaustive assignments | `[[Delta]]` is defined only when every admitted input yields exact-contract output. |
| No silent loss or invention | Input ledger, loss declarations, constants | Every input distinction has a fate and every constructed value has a term. |
| Recovery separate | Explicit unassessed claim records | No direction count establishes a claim. |
| No surface semantics | Exact concept references only | The grammar contains no physical location or representation construct. |

The customer witness validates locally against the kernel: it binds exact
contracts, carries real correspondence facts, defines one explicit total
direction, constructs complete state, declares no unsupported recovery, and
contains no acquisition or persistence information.
