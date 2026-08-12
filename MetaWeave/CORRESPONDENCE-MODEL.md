# MetaWeave Correspondence Model

## Status

This is draft layer 1 of the MetaWeave specification ladder. It is subordinate
to [`KERNEL.md`](KERNEL.md) and follows the method in
[`LADDER.md`](LADDER.md).

This document defines the logical content and definition-time validity of an
authored correspondence. It does not define the concrete MetaWeave product
model, serialized authoring form, compiler representation, or execution
algorithm.

## Layer Question

The kernel says that `K` is authored declarative correspondence truth between
`M_S` and `M_T`. This layer answers:

> What must `K` state so that directional executability, domain, output,
> information loss, and recovery claims can be derived rather than guessed?

## Logical Form

A correspondence has the abstract form:

```text
K = (identity, revision, M_S, M_T, Delta_F?, Delta_G?, claims)
```

`identity` names the correspondence across revisions. `revision` identifies
one immutable semantic definition. `M_S` and `M_T` are exact semantic model
contracts, not workspace locations or representation descriptors.

`Delta_F` and `Delta_G` are optional authored directional definitions. They are
declarative parts of `K`; they are not executable functions. Either, both, or
neither may be present. A compiler later determines whether a present
definition is complete and can produce the corresponding `F_K` or `G_K`.

`claims` contains optional canonicalization and recovery assertions. A claim
is not made true by being authored. It remains subject to validation and
capability derivation.

This form does not introduce a primary consistency relation. `K` is the
authored correspondence model itself.

## Contract Binding

**CM-1 (D; `K-C`, `K-W`).** `K` binds exactly one source model contract and one
target model contract. Contract identity includes every modeled distinction
that can affect validity, equality, equivalence, ordering, or transformation
meaning.

Names and version labels are useful diagnostics but are not sufficient proof
that two contracts are exact. The first implementation may choose deterministic
contract signatures; that representation choice belongs to the implementation
layer.

**CM-2 (D; `K-B`).** A contract binding contains no workspace path, surface,
file, table, source-code type, connection string, or acquisition policy. A
caller supplies workspaces already bound to the required contracts.

## Directional Definition

For a direction `Delta_X`, the input and output contracts are fixed by its
orientation. The definition must state enough modeled truth to determine all
of the following.

### Domain

**CM-3 (D; `K-D`).** A directional definition owns an explicit domain
declaration over valid input workspaces. It may declare the whole valid input
space or a strict subset described by modeled predicates.

The domain describes where the function is defined. It is not a list of
recoverable errors, a best-effort policy, or a postcondition discovered after
partial output has been exposed.

The declaration includes every semantic precondition required for deterministic
construction of a complete valid output. A valid input admitted to the domain
cannot be expected to fail merely because a construction precondition was left
unstated.

Domain membership respects the input contract's semantic equivalence: two
semantically equivalent valid inputs cannot be separated merely by a
representation distinction that the contract declares meaningless.

### Record correspondence and construction

**CM-4 (D; `K-W`, `K-L`).** A directional definition accounts for how input
records contribute to complete output records. It must make record selection,
fan-out, grouping, omission, and destination construction explicit wherever
the direction uses them.

The logical vocabulary must be able to distinguish at least:

- the input entity and records participating in a rule;
- the destination entity constructed by that rule;
- the input binding from which each destination record is derived;
- whether input records are selected, combined, or expanded;
- the condition under which a rule applies.

This requirement does not choose a query language, graph-rewrite system, or
record-plan algebra. Those are possible implementation designs, not facts at
this layer.

### Identity

**CM-5 (D; `K-I`, `K-L`).** Every constructed output record has an explicit,
deterministic identity derivation. Identity copying, composition,
canonicalization, allocation from modeled inputs, and deliberate replacement
are distinct semantic acts and cannot be conflated by an implementation.

Potential collisions are excluded by the explicit domain. Discovering one for
an input already admitted to the domain refutes the compiled product's
conformance; records are never merged merely because two rules derive the same
identity.

### Values and presence

**CM-6 (D; `K-I`, `K-E`).** Every output value owned by a rule has an explicit
construction. Every input value within declared source coverage is accounted
for as preserved, transformed, canonicalized, or lost.

The accounting respects every distinction represented by the bound contracts,
including absence versus presence, present content, and significant order.
An explicitly authored constant or default is modeled construction; an
unwritten runtime default is silent invention.

### Relationships

**CM-7 (D; `K-W`, `K-L`, `K-I`).** Relationship construction identifies both
the output relationship and the derived target identity or record. It states
the conditions for absence and presence and preserves the target contract's
requiredness and cardinality.

Name similarity, `...Id` conventions, reflection, and observed value matches
may support authoring suggestions. They are not correspondence truth until the
author accepts an explicit modeled rule.

### Significant order

**CM-8 (D; `K-E`, `K-I`).** If either contract declares order significant, the
direction accounts for how that order is preserved, derived, canonicalized, or
lost. Runtime enumeration order cannot supply modeled order implicitly.

### Destination coverage

**CM-9 (D; `K-L`, `K-I`).** For every reachable input in the declared domain,
the directional definition accounts for every modeled destination entity,
record, identity, value, relationship, and significant order. Coverage may
explicitly construct absence or an empty record set where the target contract
permits it. Every required fact is constructed on every applicable path. Two
rules may not silently compete to own the same destination fact.

Coverage is assessed against the exact output contract, not merely against the
members mentioned by the author.

### Source coverage and loss

**CM-10 (D; `K-I`).** Every modeled input distinction that can vary within the
declared domain is classified as preserved, transformed, canonicalized, or
lost, whether or not a construction rule reads it. Any deliberate omission,
merge, truncation, replacement, or many-to-one collapse has an explicit
directional loss declaration attributable to a rule or source concept.

A loss declaration describes behavior; it does not grant permission to return
an invalid output or to advertise unsupported recovery.

## Direction Independence

**CM-11 (D; `K-P`).** `Delta_F` and `Delta_G` are independently authored and
validated. A reverse definition is not synthesized by reading a forward
definition backward. Shared correspondence facts may be referenced by both,
but each direction owns its domain, construction, coverage, and loss.

The presence of both definitions establishes no recovery fact. A useful reverse
direction may intentionally choose representatives, discard target-only state,
or operate over a domain different from the range of the forward direction.

## Canonicalization and Recovery Claims

**CM-12 (D; `K-N`).** A canonicalization reference names a model-bound semantic
contract. Its applicability and equality strength are explicit. It cannot be
introduced as an explanation for unaccounted information loss.

**CM-13 (D; `K-R`).** A source or target recovery claim states:

- its recovery domain;
- the required opposite-domain closure;
- whether comparison uses state equality or semantic equivalence;
- the canonicalizer, if canonical recovery rather than exact recovery is
  claimed.

The correspondence model may carry evidence or assumptions associated with a
claim, but authoring a claim alone does not establish it.

## Definition-Time Validity

Validation is separated into three outcomes.

### Correspondence validity

**CM-14 (C; satisfies `K-C`).** A valid `K` has stable identity, two resolvable
exact contracts, internally consistent and complete present declarations, and
no forbidden boundary concepts. It may be valid while intentionally defining
no executable direction.

This preserves the kernel's zero-product case and permits correspondence facts
that make no directional definition. An authoring document may of course be
saved while incomplete, but it is not a validated `K` and cannot compile.

### Direction completeness

**CM-15 (D; `K-P`, `K-L`, `K-I`).** A declared direction is compilable only if
its references resolve, its domain is decidable by the supported semantic
vocabulary, its construction is deterministic and complete, destination
coverage is single-owned, source loss is accounted for, and valid output is
guaranteed for every admitted input. Runtime checks may implement domain
membership and defensively verify the guarantee, but they cannot turn an
unstated precondition into an ordinary in-domain failure.

Its domain is invariant under input semantic equivalence, and its construction
is semantically congruent: equivalent admitted inputs produce equivalent
outputs under the exact output contract. A rule cannot expose an input
distinction that its model declares semantically irrelevant unless the output
difference is likewise semantically irrelevant.

An incomplete direction does not become a partial function merely by narrowing
its domain after the fact. Its actual preconditions must be modeled. A present
direction that fails this obligation makes the authored revision invalid; it is
not treated as an absent direction in an otherwise validated `K`.

### Claim establishment

**CM-16 (D; `K-A`, `K-R`).** Recovery, totality, and losslessness are derived
capabilities. Validation reports each authored claim as established, refuted,
or unresolved under explicit assumptions. Unresolved is not equivalent to
false, but it cannot be advertised as established.

## Minimal Logical Vocabulary

The first authorable form must represent these logical roles without requiring
one generic operation node or opaque script:

- correspondence identity and revision;
- exact source and target contract binding;
- optional forward and reverse definitions;
- domain predicates;
- record participation and destination construction;
- identity construction;
- property/value construction;
- relationship construction;
- significant-order treatment where applicable;
- source and destination coverage;
- directional loss declarations;
- optional canonicalization and recovery claims.

This list defines semantic responsibilities, not final entity names. Concrete
variant entities, scope representation, expression vocabulary, and editing UX
remain implementation choices. The first vertical slice may support a strict
subset, but unsupported responsibilities must fail validation rather than fall
back to hidden converter code.

## Worked Semantic Sketch

Suppose `M_S` contains `Customer(Id, DisplayName)` and `M_T` contains
`Party(Id, Name)`. A forward definition may state:

```text
domain: every valid M_S workspace
for each Customer c:
  construct Party with Id from c.Id and Name from c.DisplayName
loss: none for the modeled source concepts in scope
```

This is correspondence truth only when the endpoint references, record
participation, identity, value construction, coverage, and loss statement are
modeled. A handwritten function implementing the same loop is not `K`.

The sketch says nothing about a reverse direction. Adding a separately authored
`Party -> Customer` definition still says nothing about recovery until the
recovery domain and law are established.

## Predecessor Validation

| Requirement | Predecessor basis | Validation result |
| --- | --- | --- |
| First-class authored `K` rather than converter callbacks | `K-C`, `K-P` | Preserved by the logical form and explicit separation of `Delta` from compiled products. |
| Zero, one, or two independent directions | `K-P` | Preserved by optional, independently validated `Delta_F` and `Delta_G`. |
| Explicit partial domains | `K-D` | Required by `CM-3`; incompleteness cannot masquerade as partiality. |
| Complete valid outputs | `K-W`, `K-L` | Required by destination coverage and direction completeness. |
| No silent loss or invention | `K-I` | Required by source accounting, explicit construction, and loss declarations. |
| Recovery separate from executability | `K-R`, `K-A` | Claims carry domains and are assessed independently. |
| No persistence or surface concerns | `K-B` | Contract and endpoint bindings are semantic only. |

The counterexample cases remain representable: a valid `K` with no complete
direction, a forward-only correspondence, two lossy nonrecovering directions,
and a partial direction with an explicit domain.

## Endpoint Feasibility

The logical roles can be represented by a Meta product model and validated
against neutral `GenericModel` contracts. The existing typed-model generation
and `InMemoryWorkspace` state are sufficient foundations. Nothing in this
layer requires Core to load a path, know a surface, mutate an input, or embed a
handwritten converter.

Whether the complete vocabulary is economical remains open. The implementation
layer therefore defines a bounded vertical slice and a stop/go gate before any
full implementation.
