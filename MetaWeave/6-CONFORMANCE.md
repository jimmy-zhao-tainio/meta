# MetaWeave Conformance

## Status

This is layer 6, the cross-cutting validation specification for the MetaWeave
ladder. It adds no correspondence semantics. It checks the concrete artifacts
introduced by [`2-CORRESPONDENCE-MODEL.md`](2-CORRESPONDENCE-MODEL.md),
[`3-COMPILATION.md`](3-COMPILATION.md),
[`4-EXECUTION.md`](4-EXECUTION.md), and
[`5-IMPLEMENTATION.md`](5-IMPLEMENTATION.md) against their immediate
predecessors and against [`1-KERNEL.md`](1-KERNEL.md) transitively.

## The Validated Path

Conformance follows one directional dependency:

```text
kernel obligations
  -> authored K1 records and denotation
  -> compiled DIR1 records and lowering law
  -> E1 states, transitions, and atomic result
  -> Meta entities, immutable CLR values, and services
```

Every arrow has both structural and semantic checks. A later successful example
cannot compensate for a missing predecessor construct.

## Conformance Levels

1. **Kernel-to-language:** `K1` can express required kernel cases without
   introducing forbidden semantics.
2. **Language-to-IR:** every supported authored construct has one exact lowering
   and `DIR1` preserves its denotation.
3. **IR-to-machine:** every IR variant has one machine transition and the atomic
   result is `[[DIR1]]`.
4. **Machine-to-implementation:** every machine responsibility has one explicit
   Meta/.NET owner and architecture boundaries prevent ambient behavior.

Passing a later level does not repair an earlier failure.

## Review Discipline

**CF-1 (D; `0-LADDER.md`).** Each derived statement cites its immediate
predecessor. A direct kernel citation may be added for visibility but cannot
replace that local citation. Each chosen statement names the predecessor
obligation it realizes. An open decision is absent from all downstream
acceptance criteria.

**CF-2 (D; kernel conservativity).** Every revision is searched for accidental
strengthening, including:

- an empty endpoint binding presented as correspondence truth;
- compiler support presented as semantic validity;
- an optional direction presented as mandatory;
- an unsupported language or contract feature presented as a smaller semantic
  domain;
- two directions presented as inverses;
- declared loss presented as safe recovery;
- finite examples presented as universal proof; and
- paths, surfaces, artifacts, or host callbacks entering `K1`, `DIR1`, or `E1`.

## Golden Witness Corpus

The customer correspondence displayed in every ladder document is the first
golden fixture. Test source and target contracts are exactly:

```text
M_S SalesCatalog
  Region(Id, Name)
  Customer(Id, DisplayName, Region -> Region required)

M_T PartyDirectory
  Territory(Id, Label)
  Party(Id, Name, Territory -> Territory required)

Id denotes modeled record identity. All displayed scalar properties are
required.
No entity has significant order.
```

The input and expected output are exactly:

```text
Input SalesCatalog
  Region(eu, Europe)
  Customer(c1, Ada, Region = eu)

Expected PartyDirectory
  Territory(eu, Europe)
  Party(c1, Ada, Territory = eu)
```

**CF-3 (D; `CM-1` through `CM-16`).** The authored fixture has:

- one correspondence and one immutable revision;
- exact `SalesCatalog` and `PartyDirectory` bindings;
- five associations: two entity, two property, one relationship;
- one forward direction and no reverse direction;
- `AllValid` domain;
- two `MapEach` rules;
- two identity assignments, two property copies, one relationship copy, and two
  insignificant-order assignments;
- seven preserved input-coverage entries;
- no loss declaration; and
- no claim.

Changing any count or endpoint is a fixture revision, not harmless test setup.

## Edge 1 -> 2: Kernel to `K1`

The structural checks are:

| Kernel fact | Required `K1` witness |
| --- | --- |
| first-class correspondence | at least one association record |
| exact endpoint contracts | two exact contract bindings |
| zero/one/two possible products | independently optional directions |
| explicit partial domain | a closed domain formula |
| complete valid output | exhaustive rules and assignments |
| explicit loss | complete input-fate ledger |
| recovery separate | optional claim records, initially unassessed |
| no physical semantics | no location or surface field in the grammar |

The semantic check evaluates the authored denotation directly over small finite
neutral workspaces. For the golden input:

```text
[[Delta_F]](Input) ≡_PartyDirectory Expected
```

This evaluator is a conformance oracle for the closed `K1` grammar, not the
production compiler or executor.

### Kernel-to-language counterexamples

| Fixture | Expected result |
| --- | --- |
| exact contracts and associations, no directions | valid `K1`; later compilation has zero programs |
| no associations and no directions | invalid `K1`; endpoint pairing alone is vacuous |
| missing assignment for `Party.Name` | invalid `K1`; incomplete target coverage |
| input `Customer.DisplayName` absent from input coverage | invalid `K1`; unaccounted input distinction |
| constant `Party.Name = "Unknown"` plus explicit loss of `Customer.DisplayName` | valid lossy direction |
| relationship copy referring to no target entity rule | invalid `K1`; relationship construction is not closed |
| forward and independently authored reverse, no claim | valid `K1`; recovery remains unassessed |
| workspace path stored on a contract binding | invalid `K1`; boundary violation |
| contract identity differs from the contract supplied to validation | invalid result; no `ValidatedK1` value |
| valid syntax bound to a nontrivial equivalence under `K1-Validation-Core-1` | `Unsupported`; no `ValidatedK1` value and no invalidity claim |

**CF-4 (D; `CM-13`, `CM-14`).** `Invalid` and `Unsupported` validation results
both stop before compilation. `Invalid` must identify a refuted `K1` rule;
`Unsupported` must identify required features and make no invalidity claim.
Neither is passed to compilation to obtain a per-direction status.

## Edge 2 -> 3: `K1` to `DIR1`

For every compiler profile, tests enumerate all syntax variants it claims to
support and assert the lowering table from `3-COMPILATION.md`.

For the golden witness, structural equality requires:

```text
CompilationHeader language      = K1
DIR1Version                     = DIR1-1
ExecutionSemanticsProfileId     = E1-1
DomainProgram tests             = 0
MapConstructor count            = 2
EmptyConstructor count          = 0
CopyRecordIdentity count        = 2
CopyScalar count                = 2
CopyReference count             = 1
IgnoreIncidentalOrder count     = 2
InputFate row count             = 7
CompiledLoss row count          = 0
Target certificate owner count = 7
Association evidence row count  = 5
```

Each compiled semantic identifier must resolve to the corresponding authored
record and exact model endpoint.

### Conformance-only compiler profile

`K1-Scalar-Only` is a fixture profile, not a product profile. It has this exact
feature set:

```text
Outer correspondence support:
  all association variants and claim records may be resolved and retained

Supported directional constructs:
  AllValid
  EveryRecordPropertyPresent
  EveryRecordPropertyEquals
  MapEach
  ConstructEmpty
  CopyInputIdentity
  CopyProperty
  Constant
  Absent
  NoSignificantOutputOrder
  input coverage and declared loss

Unsupported directional constructs:
  EveryRecordRelationshipPresent
  CopyRelationship
  AbsentRelationship
  PreserveOrder
```

The golden forward direction therefore has exactly one missing required
feature, `CopyRelationship`. Its `K1` validity is unchanged.

**CF-5 (D; `CP-1` through `CP-15`).** Compilation conformance includes:

- only `ValidatedK1` is accepted by the compiler API;
- validation `Invalid`, validation `Unsupported`, and differently bound `K1`
  cannot reach direction assessment;
- absent, unsupported, and compiled are distinct;
- `K1-Scalar-Only` reports the golden forward direction `Unsupported` because
  of `CopyRelationship`, while the same `K1` remains valid;
- an unsupported direction does not discard a compiled opposite direction;
- compiler-profile limits do not appear as domain tests;
- every compiled direction carries `DIR1-1` and `E1-1` without relying
  on its surrounding compilation header;
- every supported authored variant has exactly one IR variant;
- every `PreserveOrder` lowers to `CopyRecordOrder` with the exact resolved
  input and output `OrderIdentity` values;
- the contract bindings retain the exact identity validity, equality, order,
  and revision used by `K1` validation;
- every target-coverage and input-fate certificate row is complete, including
  an `EmptyPopulationProof` for each `EmptyConstructor`;
- every authored claim has exactly one claim-assessment row;
- authored collection reordering declared insignificant does not change
  program semantic identity; and
- changing a `DIR1`/execution version, domain test, constructor, write, fate,
  loss row, or exact contract identity does change program semantic identity.

### Denotation preservation

For each finite fixture workspace in the authored domain:

```text
[[DIR1]](W) ≡_M_out [[Delta_X]](W)
```

For each valid fixture outside the authored domain, both denotations are
undefined and machine application later reports `OutsideDomain`.

The finite tests are exhaustive only for the explicitly finite fixture space.
They witness the closed lowering table; they do not prove claims about an
unbounded future language.

### Named-order retention fixture

A separate valid fixture has input entity `Item`, output entity `Entry`, input
order `Presentation`, and output order `Display`. A `MapEach` rule copies
identity, and order association `AO1` binds the two orders. The authored order
assignment is `PreserveOrder(AO1)`. Its only conforming lowering is:

```text
CopyRecordOrder(Presentation, Display)
```

For input records `a`, `b` with `Presentation = [b, a]`, the resulting
`Display` order must be `[b, a]`.

Lowering to a fieldless order operation, swapping the endpoints, or recovering
either identity from association evidence during execution fails this fixture.

## Edge 3 -> 4: `DIR1` to `E1`

**CF-6 (D; `EX-1` through `EX-16`).** Every `DIR1` variant has an execution
fixture that reaches its exact machine transition:

| `DIR1` construct | Required `E1` observation |
| --- | --- |
| IR and execution version fields | verified in `Accept` without a compilation header |
| domain test | evaluated only in `TestDomain` |
| map constructor | one output allocation per source record |
| empty constructor | no allocation for its target entity |
| scalar copy | exact presence/value copied |
| constant | exact typed literal written |
| absent scalar/reference | optional target member remains absent |
| relationship copy | queued during construction and resolved through the named constructor |
| order copy with two order identities | the named output order follows the named input order |
| input loss row | one immutable, deterministically ordered `LossEvidence` row |
| coverage certificate | no authoring reinterpretation at runtime |

### Directional law suite

- **Validity:** every valid admitted fixture reaches a valid exact-contract
  output. `EvaluationDefect` or `InvalidOutput` is a conformance failure.
- **Determinism:** state-equal inputs produce state-equal outputs while
  incidental input enumeration order is varied.
- **Semantic congruence:** when a supported fixture contract supplies an
  explicit nontrivial equivalence, equivalent admitted inputs produce
  equivalent outputs. Until the first profile supports such contracts, it
  returns `K1ValidationResult.Unsupported` without claiming invalidity or
  pretending to test them.
- **Nonmutation:** input and program snapshots remain state-equal after success,
  every failure code, and cancellation.
- **Atomicity:** no failure result contains candidate state.
- **Domain fidelity:** both sides of every supported predicate produce the
  expected `Succeeded` or `OutsideDomain` transition.
- **No ambient semantics:** architecture and behavioral tests vary process
  environment without changing semantic results.
- **Loss fidelity:** every compiled loss row appears in evidence, including an
  empty affected-record set; row and affected-identity order remain stable when
  execution traversal is varied.

### Identity-key regression

Under identity semantics revision `meta-identity/1`, use this valid input:

```text
Region(Id = EU, Name = Europe)
Customer(Id = c1, DisplayName = Ada, Region = eu)
```

The lowercase reference is valid because `MetaIdentity.Comparer` equates `eu`
and `EU`. Conforming execution must:

- insert `Territory(EU)` into `OutputIndex` using output identity equality;
- resolve lookup key `eu` to that same record;
- produce `Party(c1, Ada, Territory = EU)` without `EvaluationDefect`;
- treat a second insertion at `eu` as a duplicate of `EU`; and
- reject an identity failing output identity validation before index use.

Running the same fixture with an ordinal string index is a required negative
test and must fail conformance.

### Detached-program version regression

The golden `CompiledDirection` succeeds when detached from its
`CompiledCorrespondence` because it carries `DIR1-1` and `E1-1` itself.
Changing either field changes `ProgramIdentity`. An unknown `DIR1Version` or
execution-semantics profile returns `InvalidProgram` in `Accept`, before domain
evaluation. `K1LanguageVersion` remains compilation provenance and is not
required by `Apply`.

### Application-evidence regression

A lossy fixture maps `Person(Id, Name)` to `PublicPerson(Id, Label)`. Its
`MapEach` rule copies identity, writes constant `"Hidden"` to `Label`, and marks
input concept `Person.Name` lost through declaration `L1`. For input enumeration
`[Person(c2, Grace), Person(c1, Ada)]`, success returns exactly:

```text
ApplicationEvidence(
  Losses = [
    LossEvidence(
      Loss = L1,
      InputConcept = Person.Name,
      AffectedInputRecordIds = [c1, c2])])
```

Reversing enumeration or constructor scheduling produces the same immutable
value. An empty `Person` population still returns the `L1`/`Person.Name` row
with `AffectedInputRecordIds = []`; it does not omit the evaluated declaration.

### Golden machine trace

The conformance test records phase order exactly:

```text
Accept
TestDomain
ConstructRecords(C1/eu)
ConstructRecords(C2/c1)
ResolveRelationships(Party.c1.Territory -> Territory.eu)
ApplyOrder
RecordLoss(empty)
ValidateCandidate
Succeeded
```

The final workspace must be state-equal to the golden expected output. Trace
collection may be disabled in production, but disabling observation cannot
change phase behavior or result.

## Edge 4 -> 5: `E1` to Meta/.NET

**CF-7 (D; `IM-1` through `IM-9`).** Implementation mapping tests establish:

- the sanctioned Meta model can serialize every golden `K1` record and no
  generic payload field substitutes for a grammar variant;
- typed-model round-trip preserves the authored `K1` structure;
- contract binding freezes `GenericModelSnapshot` and significant-order values
  before computing `ContractIdentity`;
- `ContractIdentity` changes when identity-semantics revision changes, and the
  first profile binds `MetaIdentity.TryValidate` plus
  `MetaIdentity.Comparer` as both equality and order;
- invalid authoring and unsupported validation proof obligations produce
  distinct `K1ValidationResult.Invalid` and
  `K1ValidationResult.Unsupported` results;
- `K1-Validation-Core-1` is passed explicitly and reports its missing proof
  features without changing correspondence validity;
- every detached `CompiledDirection` carries `DIR1Version` and
  `ExecutionSemanticsProfile` fields included in
  `ProgramIdentity`;
- every compiled abstract-record variant has one immutable CLR type;
- exhaustive pattern matching covers every domain test, constructor, identity
  write, property write, relationship write, order write, and input-fate
  variant;
- `CopyRecordOrderWrite` stores exact input and output `OrderIdentity` values;
- `OutputRecordIndex` uses output identity equality for insertion, duplicate
  detection, and lookup, while copied identities use output identity
  validation;
- successful results expose immutable `ApplicationEvidence` and `LossEvidence`
  values in the deterministic order required by `E1-1`;
- `ExecutionContext` is application-local and never retained by a compiled
  program;
- Core project references exclude `Meta.Integration`, surfaces, database and
  CLI assemblies;
- Core source dependency checks exclude filesystem, network, clock, random,
  process, and environment access;
- only the application layer loads and publishes workspaces; and
- stable diagnostic codes preserve phase and semantic identifiers.

Dependency tests are preferred when project boundaries can make forbidden
behavior impossible.

## Recovery Conformance

Recovery remains separate from the direct first slice. When stage 7 begins, a
source claim must establish:

```text
R_S subset-of D_F
F_K(R_S) subset-of D_G
for every S in R_S:
  G_K(F_K(S)) equal-to C_S(S) under the claimed comparison
```

Target claims use the symmetric obligations. A forward result outside the
opposite domain is a counterexample, never a skipped case. State equality and
semantic equivalence use different comparers.

**CF-8 (D; `CP-10`, `EX-14`).** Before a recovery verifier exists, every authored
recovery claim is reported `Unresolved`. Finite fixture enumeration is called
exhaustive only when the claimed domain is itself finite and fully enumerated;
otherwise it is empirical evidence.

## First Forward Slice Acceptance

The stage-4 slice is accepted only when:

- the golden `K1` is represented by the sanctioned Meta model;
- all kernel-to-language positive and negative fixtures relevant to
  `K1-Validation-Core-1` pass;
- `Valid`, `Invalid`, and validation `Unsupported` remain distinct through the
  implementation API;
- the compiler produces the exact golden `DIR1` counts and identities;
- detached directions carry and enforce `DIR1-1` and `E1-1`;
- `K1-Scalar-Only` demonstrates validity distinct from compiler support;
- the executor produces the exact golden state and passes every applicable
  directional law;
- the named-order and case-insensitive identity-key regressions pass;
- application and loss evidence have the exact immutable shapes and ordering
  required by `E1-1`;
- relationship construction is included;
- unsupported language or contract features fail before execution;
- no reverse or recovery capability is claimed; and
- documentation states that this proves a bounded forward architecture, not
  product value or complete MetaWeave coverage.

The later stage-5 value gate additionally requires a named real product pair,
direct-converter comparison, full framework-cost accounting, and a recorded
continuation decision.

## Local Promotion Record

Every rung review records:

```text
Document and revision:
Immediate predecessor and revision:
New abstraction introduced:
Concrete syntax or state added:
Predecessor constructs consumed:
Local structural checks:
Local semantic checks:
Counterexamples preserved:
Choices introduced:
Open decisions preserved downstream:
Boundary review:
Required predecessor correction:
Decision: revise | accept for next rung
```

## Current Draft Assessment

| Edge | Concrete contribution now available | Review still required |
| --- | --- | --- |
| Kernel -> `K1` | association and direction grammar, three-way validation result, denotation, golden authored witness | Confirm the bounded grammar is sufficient to justify a first value experiment. |
| `K1` -> `DIR1` | normalized tests, typed constructors/writes, retained order identities and executable versions, fate/loss tables, certificate, lowering law | Recheck the complete closed lowering table after any language revision. |
| `DIR1` -> `E1` | explicit state, identity-key semantics, phases, transitions, deterministic evidence, terminal result, golden trace | Review failure taxonomy and resource/cancellation boundary. |
| `E1` -> implementation | named Meta entities, identity semantics, versioned CLR values, evidence records, services, profile, project boundary | Review model size and total cost before authorizing model changes. |

These rows identify review work; they do not self-certify promotion. No edge
depends on a primary consistency relation, graph rewriting, incremental
synchronization, in-place reconciliation, trace model, or hidden converter.
