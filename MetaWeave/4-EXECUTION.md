# MetaWeave Execution

## Status

This is draft layer 4 of the MetaWeave specification ladder. It derives from
the `DIR1` intermediate representation in
[`3-COMPILATION.md`](3-COMPILATION.md) and remains subordinate to
[`1-KERNEL.md`](1-KERNEL.md).

The input to this layer is one immutable `DIR1` program and one neutral input
workspace. The output is an atomic application result. This document defines a
concrete abstract machine, `E1`, whose transitions realize the already-defined
`[[DIR1]]` meaning. It does not load or publish workspaces and it does not
prescribe CLR data structures.

`E1` accepts no external semantic callback. A later extension language must
first add a typed semantic binding to the correspondence model, compiled
program identity, and machine input. It cannot be inserted behind this
interface.

## Layer Question

Compilation has resolved authored truth into constructors, writes, indexes,
and certificates. This layer adds the next abstraction:

> What machine state and transition rules apply that program without mutation,
> ambient semantics, or partial output?

## Application Interface

```text
Apply1(DIR1, InputWorkspace) ->
    Success(OutputWorkspace, ApplicationEvidence)
  | Failure(FailureCode, Diagnostics)
```

The returned workspace is complete neutral state under the exact output
contract retained by `DIR1`. `Apply1` has no output location and performs no
publication.

**EX-1 (D; `CP-1`, `CP-12`).** The supplied program is immutable, internally
well formed, and bound to exact input and output contract identities. A corrupt
or unsupported program fails before domain evaluation.

**EX-2 (D; `CP-1`, `CP-4`).** The input is complete valid state under the exact
input contract. Contract mismatch, invalid input, and valid input outside the
compiled domain are three distinct outcomes.

## Machine State

An `E1` state is:

```text
E1State = (
  Phase,
  Program,
  Input,
  Candidate,
  ConstructorCursor,
  RecordCursor,
  OutputIndex,
  PendingReferences,
  Evidence,
  Failure?
)
```

```text
Phase ::=
    Accept
  | TestDomain
  | ConstructRecords
  | ResolveRelationships
  | ApplyOrder
  | RecordLoss
  | ValidateCandidate
  | Succeeded
  | Failed
```

- `Candidate` is a fresh workspace builder for the exact output contract.
- `OutputIndex` maps `(OutputEntityId, OutputIdentity)` to one candidate record.
- `PendingReferences` holds relationship writes until every constructor has
  contributed its records.
- `Evidence` is private machine state until success.
- cursors refer only to resolved program components and neutral records.

No state component contains a path, surface, database connection, clock,
random source, process state, environment variable, or mutable global service.

**EX-3 (D; `CP-5`, `CP-7`).** Candidate, index, pending-reference, and evidence
state are fresh for each application. The input workspace and `DIR1` are never
used as mutable builders.

## Transition Rules

The machine starts in `Accept` with an empty candidate and empty private tables.
Every transition either advances to the named next phase or terminates in
`Failed`. No intermediate state is an application result.

### 1. Accept

```text
Accept -> TestDomain
  when Program is well formed,
       Input.M = Program.InputContract,
       and Input is valid under that exact contract.
```

Otherwise it terminates with `InvalidProgram`, `InputContractMismatch`, or
`InvalidInput`, respectively.

### 2. Test domain

```text
TestDomain -> ConstructRecords
  when every DomainTest in AllOf(...) is true.

TestDomain -> Failed(OutsideDomain)
  when any DomainTest is false.
```

Tests quantify over all records of their resolved input entity. Property
presence, scalar equality, and relationship presence use the exact input
contract. The empty conjunction is true.

```text
PropertyPresent      -> every record has the resolved property
PropertyEquals       -> every record has the property and its value equals the
                        compiled typed literal
RelationshipPresent  -> every record has the resolved relationship
```

These are the three `DomainProgram` test variants; the machine has no generic
predicate evaluator.

**EX-4 (D; `CP-4`).** This phase decides exactly the compiled domain. It cannot
repair input, ignore a failed test, or turn an unsupported operation into a
warning.

### 3. Construct records and scalar values

Constructors are visited in their deterministic semantic-identifier order.
For each constructor:

```text
EmptyConstructor:
  contribute no records.

MapConstructor:
  for each record of InputEntityId:
    CopyRecordIdentity -> output identity := input record identity
    allocate one record of OutputEntityId
    apply every PropertyWrite
    queue every RelationshipWrite
```

When source order is significant, record traversal uses that modeled order.
Otherwise traversal order is semantically irrelevant; implementations may use
a deterministic identity order for stable diagnostics.

Property writes evaluate as follows:

```text
CopyScalar       -> copy exact presence and scalar value
WriteConstant    -> write the compiled typed literal
WriteAbsent      -> leave the optional member absent
```

Allocation inserts the record into `OutputIndex`. A duplicate key is
`EvaluationDefect`: valid `K1`, its coverage certificate, and a valid admitted
input should make the collision impossible.

**EX-5 (D; `CP-5`, `CP-7`).** Record, identity, and scalar construction follows
only the constructors and writes in `DIR1`. Runtime name matching, reflection
inference, implicit merge, defaulting, filtering, grouping, and fan-out are not
machine transitions.

### 4. Resolve relationships

Each queued `CopyReference` contains:

```text
(OutputRecord,
 InputReferencedIdentity,
 OutputRelationshipId,
 ReferencedConstructorId)
```

For a present input relationship, the referenced constructor identifies its
exact output entity. Resolution looks up
`(ReferencedOutputEntityId, InputReferencedIdentity)` in `OutputIndex` and
writes that target. For an absent input relationship, `CopyReference` leaves
the output relationship absent and queues nothing. Compilation permits that
case only when the output relationship is optional; otherwise the domain must
guarantee input presence. `WriteAbsentReference` also queues nothing.

A missing target or required absent relationship is `EvaluationDefect`. It is
not repaired by observed identifiers or another constructor.

**EX-6 (D; `CP-5`).** Relationship resolution realizes the exact compiled
reference dependency. The two-phase record/link process changes no
correspondence meaning; it merely makes forward references evaluable.

### 5. Apply significant order

```text
CopyRecordOrder:
  order constructed records by the corresponding source record order.

IgnoreIncidentalOrder:
  write no significant order fact.
```

The latter does not erase modeled order because compilation permits it only for
an output entity whose contract declares order insignificant.

### 6. Record declared loss

For every compiled loss row, the machine emits:

```text
LossEvidence = (
  LossId,
  InputConceptId,
  AffectedInputRecordIds
)
```

An entity population or identity affects every record of that entity. A
property or relationship affects every record for which its presence, absence,
value, or target is a modeled distinction. Significant order affects the
ordered entity set. An empty affected set remains evidence that the declaration
was evaluated for this application.

**EX-7 (D; `CP-6`).** Every compiled loss row produces application evidence.
The executor cannot discover permission for loss that is absent from the
program, and evidence cannot alter the candidate.

`InputFate` rows marked preserved require no runtime action because their
program-component links were certified during compilation. `CompiledLoss` rows
are the exact rows visited here; neither table is reconstructed from output.

### 7. Validate candidate

The complete candidate is validated under the exact output contract, including:

- entity membership and identity uniqueness;
- property and relationship requiredness;
- relationship target integrity and cardinality;
- significant order; and
- every product-specific validity rule included in that contract.

```text
ValidateCandidate -> Succeeded
  when complete validation succeeds.

ValidateCandidate -> Failed(InvalidOutput)
  otherwise.
```

For a conformant `DIR1`, machine, and admitted valid input, `InvalidOutput` is a
conformance defect rather than an authored branch of the partial function.

`Accept` also verifies the integrity and contract identity of the
`CoverageCertificate`. Later phases consume its established ownership result;
they do not rerun authored coverage analysis.

**EX-8 (D; `CP-7`, `CP-8`).** Candidate validation gates the only successful
transition. No constructor-local fragment can bypass it.

## Terminal Results

`Succeeded` freezes the candidate as one neutral output workspace, canonicalizes
evidence ordering by semantic identifiers, and returns both.

`Failed` discards candidate, index, pending references, and private evidence.
Diagnostics may retain semantic identifiers and bounded input excerpts, but no
candidate workspace is returned.

```text
FailureCode ::=
    InvalidProgram
  | InputContractMismatch
  | InvalidInput
  | OutsideDomain
  | EvaluationDefect
  | InvalidOutput
  | Cancelled
```

Cancellation is application control, not correspondence semantics. It may move
any nonterminal state to `Failed(Cancelled)` and obeys the same atomicity rule.

**EX-9 (D; `CP-8`).** Success returns exactly one complete output equal to the
`DIR1` denotation. Failure returns no directional output.

## Machine Laws

### Denotation realization

The machine realizes the `[[DIR1]]` denotation defined by the compilation
layer:

```text
[[DIR1]](W) = O
  exactly when Apply1(DIR1, W) = Success(O, evidence).
```

For every valid `W` admitted by the domain, a conformant program and machine
reach `Succeeded`. Operational cancellation is outside this mathematical
evaluation.

### Determinism

**EX-10 (D; `CP-5`, `CP-8`, `CP-12`).** State-equal admitted inputs and the same
`DIR1` produce state-equal outputs. Incidental traversal order cannot change
the candidate's modeled state.

Stable failure codes and diagnostic ordering are an implementation choice for
operability; they are not presented as a kernel-derived transformation law.

### Semantic congruence

**EX-11 (D; `CP-4`, `CP-8`).** Semantically equivalent admitted inputs produce
semantically equivalent outputs under the exact output contract. Domain tests
cannot separate equivalent valid input states.

### Nonmutation and atomicity

**EX-12 (D; `CP-5`, `CP-8`).** Input and program remain unchanged across every
transition. Only the frozen state from `Succeeded` is observable as output.

### No ambient semantics

**EX-13 (D; `CP-2`).** The transition relation depends only on `DIR1`, the input
workspace, and exact contract semantics already named by the program. Changing
working directory, storage representation, culture, time zone, environment,
filesystem, database, network, thread schedule, or unrelated registry state
cannot change a semantic result.

### Capabilities and recovery

**EX-14 (D; `CP-10`, `CP-11`).** Execution reports compiled capabilities and
loss evidence but never promotes them. Ordinary application invokes one
direction only. A successful pair of applications is not a recovery proof.

## Concurrency and Resource Policy

**EX-15 (C; realizes `CP-12`).** Immutable programs are concurrently reusable;
all `E1State` values are application-local. A cache is permitted only when its
key contains every semantic input and cache presence cannot affect output or
failure classification.

**EX-16 (C; preserves `CP-8` partial-function meaning).** Resource limits and
cancellation belong to the application invocation. Exhaustion produces no
semantic output and does not shrink the authored domain or change capability
evidence.

## Customer Witness Execution

Apply the compiled customer program to:

```text
SalesCatalog input
  Region(Id = eu, Name = Europe)
  Customer(Id = c1, DisplayName = Ada, Region = eu)
```

The machine trace is:

```text
Accept
  exact SalesCatalog contract; input valid

TestDomain
  AllOf() = true

ConstructRecords
  C1/eu -> Territory(Id = eu, Label = Europe)
  C2/c1 -> Party(Id = c1, Name = Ada)
  queue (Party c1, target identity eu, Party.Territory, C1)

ResolveRelationships
  index (Territory, eu) -> Territory eu
  Party c1.Territory = Territory eu

ApplyOrder
  no significant output order

RecordLoss
  loss table empty

ValidateCandidate
  PartyDirectory valid

Succeeded
```

The atomic result is:

```text
PartyDirectory output
  Territory(Id = eu, Label = Europe)
  Party(Id = c1, Name = Ada, Territory = eu)
```

Every output fact can be traced to one displayed `DIR1` write, and every machine
transition consumes a structure introduced by the immediately preceding layer.

## Predecessor Validation

| `DIR1` abstraction | `E1` realization | Local validation |
| --- | --- | --- |
| Exact contract identities | `Accept` checks | Evaluation cannot begin on a lookalike contract. |
| `AllOf(DomainTest...)` | `TestDomain` | The same tests decide machine admission. |
| Constructors and scalar writes | `ConstructRecords` | One transition rule exists for each IR variant. |
| Reference writes | `ResolveRelationships` | Referenced constructor identity is used directly. |
| Order writes | `ApplyOrder` | Significant order has an explicit transition. |
| Fate and loss tables | `RecordLoss` | Every compiled loss row yields evidence. |
| Coverage certificate | Candidate validation assumption and defensive checks | The executor consumes the certificate result rather than recreating authoring logic. |
| Denotation-preservation law | `Succeeded` output | `Apply1` realizes the `[[DIR1]]` already used by `CP-8`. |

The customer trace is the local witness: it executes the exact IR shown in
`3-COMPILATION.md`, reaches the expected exact-contract state, and introduces no
new correspondence decision at runtime.
