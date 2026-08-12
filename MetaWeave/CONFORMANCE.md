# MetaWeave Conformance

## Status

This is the cross-cutting validation specification for the MetaWeave ladder. It
does not add correspondence semantics. It turns obligations from
[`KERNEL.md`](KERNEL.md), [`CORRESPONDENCE-MODEL.md`](CORRESPONDENCE-MODEL.md),
[`COMPILATION.md`](COMPILATION.md), [`EXECUTION.md`](EXECUTION.md), and
[`IMPLEMENTATION.md`](IMPLEMENTATION.md) into review and executable gates.

## Conformance Levels

Conformance is assessed at four distinct levels.

1. **Specification coherence:** each layer preserves its predecessor and the
   kernel transitively.
2. **Authored-model conformance:** a concrete MetaWeave product model can
   express the accepted logical correspondence vocabulary without hidden
   semantics.
3. **Compiled-product conformance:** compilation derives products, domains,
   loss, capabilities, and evidence exactly as specified.
4. **Application conformance:** execution obeys the partial-function and
   directional laws over neutral workspace state.

Passing a later level does not compensate for failing an earlier one.

## Specification Review Method

**CF-1 (D; `LADDER.md`).** Every derived statement cites its immediate
predecessor. Every chosen statement identifies the obligation it realizes.
Every open statement is absent from downstream assumptions.

For each document revision, review records:

- predecessor revision reviewed;
- kernel trace keys affected;
- derived obligations added or removed;
- new design choices and rejected alternatives;
- open questions preserved;
- positive examples and counterexamples;
- endpoint feasibility;
- contradictions requiring predecessor revision.

**CF-2 (D; kernel conservativity).** Review explicitly searches for accidental
strengthening, including:

- treating optional directions as mandatory;
- treating a declared direction as total;
- treating two directions as inverses;
- treating canonicalization as permission for loss;
- treating example round trips as universal recovery;
- treating a product limitation as a narrower semantic domain;
- moving paths, surfaces, or artifacts into `K` or Core.

## Trace Matrix

This matrix is the minimum transitive coverage. Detailed tests may cite more
than one row.

| Kernel key | Correspondence | Compilation | Execution | Implementation |
| --- | --- | --- | --- | --- |
| `K-W` complete valid workspace | `CM-1`, `CM-4`, `CM-9` | `CP-1`, `CP-9` | `EX-2`, `EX-7` | `IM-1`, `IM-3`, `IM-8` |
| `K-C` authored first-class `K` | `CM-1`, `CM-14` | `CP-1` | Product identity in `EX-1` | `IM-4` |
| `K-P` zero/one/two products | `CM-11`, `CM-14`, `CM-15` | `CP-4`, `CP-5` | Absent products are not applicable | `IM-5` |
| `K-D` explicit partial domains | `CM-3` | `CP-8` | `EX-3` | `IM-5`, `IM-8` |
| `K-E` equality/equivalence | `CM-6`, `CM-8`, `CM-13` | `CP-11`, `CP-14` | `EX-10`, `EX-11` | Conformance comparers |
| `K-L` directional laws | `CM-4` through `CM-9` | `CP-7`, `CP-9`, `CP-12` | `EX-4` through `EX-11` | `IM-8`, `IM-9` |
| `K-N` canonicalization | `CM-12`, `CM-13` | `CP-11` | `EX-14` | Unsupported claims reject in v1 |
| `K-R` recovery | `CM-13`, `CM-16` | `CP-11`, `CP-14` | `EX-13`, `EX-14` | Separate recovery verifier |
| `K-I` explicit loss | `CM-5` through `CM-10` | `CP-9`, `CP-10` | `EX-8` | Source coverage and loss records |
| `K-A` derived capabilities | `CM-16` | `CP-11`, `CP-14` | `EX-13` | Compiler-owned evidence |
| `K-B` ownership boundary | `CM-2` | `CP-2`, `CP-12` | `EX-5` | `IM-1` |

An empty cell in a future revision requires either an explicit explanation that
the layer has no responsibility for that key or a new obligation.

## Authored Correspondence Fixtures

**CF-3 (D; correspondence validity).** The conformance corpus contains semantic
fixtures independent of serialization and surface. At minimum:

### Positive fixtures

- valid `K` with no directional definitions;
- valid forward-only `K`;
- valid reverse-only `K`;
- valid `K` with two independently authored directions;
- total identity-copy direction;
- partial direction using each supported domain predicate;
- explicit constant construction;
- explicit relationship construction;
- deliberately lossy direction with complete loss accounting;
- two executable directions with no recovery claim;
- exact recovery claim over a bounded closed primitive case.

### Negative fixtures

- wrong or unresolved source/target endpoint;
- incomplete present direction rejected for the whole correspondence revision,
  not reported as an absent direction;
- workspace path or surface concept embedded in semantic correspondence;
- destination record without identity construction;
- duplicate owners for one destination fact;
- uncovered required destination property or relationship;
- identity collision not excluded by domain;
- source entity or member omitted from coverage merely because no rule reads
  it;
- source fact silently omitted without loss declaration;
- implicit default or name-based relationship inference;
- direction using unsupported predicate or construction semantics;
- reverse definition assumed from forward definition;
- recovery claim without opposite-domain closure;
- canonicalization claim that discards modeled meaning.

Each negative fixture identifies the exact `CM-*` obligation and expected
stable diagnostic code.

## Compilation Conformance

**CF-4 (D; `COMPILATION.md`).** Compiler tests establish:

- exact contract matching and mismatch rejection;
- deterministic contract identity and compiled-product meaning;
- independent absent/rejected/compiled status per direction;
- zero, one, and two-product results;
- no executable product returned when any present direction is rejected;
- exact endpoint and scope resolution;
- deterministic domain lowering;
- every construction precondition and possible identity collision represented
  in domain membership rather than expected in-domain failure;
- complete target coverage and single ownership;
- explicit identity collision handling;
- undeclared-loss rejection;
- established/refuted/unresolved capability evidence;
- no recovery derivation from product count;
- no path, surface, workspace instance, or ambient registry in compiled
  products.

Reordering semantically unordered authored records must not change compiled
meaning. Changing any semantic rule must change the correspondence revision or
compiled semantic identity.

## Execution Law Suite

**CF-5 (D; kernel directional laws and `EXECUTION.md`).** Every supported
direction runs against reusable law tests.

### Validity

```text
valid input and input in domain and success => valid exact-contract output
```

Tests include required values, identity uniqueness, referential integrity,
relationship requiredness, and every significant ordering form supported by
the slice.

Every valid input admitted to the domain must succeed under a conformant
product and executor. `EvaluationFailure` or `InvalidOutput` on such an input is
a conformance failure, not an accepted negative result.

### Determinism

State-equal inputs, equal compiled products, and equal explicit semantic inputs
produce state-equal outputs. Tests vary incidental input record enumeration and
dictionary order.

### Semantic congruence

Where a fixture model declares a nontrivial semantic equivalence, equivalent
inputs produce equivalent outputs. The comparer comes from the fixture model's
explicit semantic contract, not a test-only omission rule.

Compiler fixtures also include a construction that observes a
semantically-irrelevant input distinction and attempts to expose it as a
meaningful output distinction; that direction must not compile as congruent.

### Nonmutation

The complete input state is snapshotted before success, each failure class, and
cancellation. It remains state-equal afterward.

### Atomic result

Collision, missing relationship target, invalid candidate, unsupported
semantic input, and cancellation expose no successful partial workspace.

### No ambient semantics

Execution results are invariant under changes to current directory, culture,
time zone, environment variables, thread scheduling, and unrelated registry or
filesystem state. Tests need not mutate global state when architectural
dependency checks already make observation impossible.

### Domain fidelity

Inputs on both sides of every supported domain predicate are tested. A valid
outside-domain input produces `OutsideDomain`, not `InvalidInput`, warning, or
best-effort success.

Semantically equivalent inputs are tested for equal domain membership. A
predicate that distinguishes only model-declared irrelevant representation is
not conformant.

### Loss fidelity

Every triggered declared loss appears in application evidence. An equivalent
fixture without the declaration fails compilation; execution never discovers
permission for undeclared loss.

## Recovery Conformance

**CF-6 (D; kernel recovery laws).** Recovery tests are separate from direction
tests and name their evidence strength.

For a source claim, tests establish or sample:

```text
R_S subset-of D_F
F_K(R_S) subset-of D_G
G_K(F_K(S)) equal-to C_S(S) under the claimed comparison
```

Target claims use the symmetric obligations. A forward result outside the
opposite domain is a counterexample, not a skipped test. State equality and
semantic equivalence use different assertions.

Finite fixture enumeration is reported as exhaustive only when the fixture
space itself is finite and fully enumerated. Otherwise it is empirical
evidence, not proof of a universal claim.

## Boundary Conformance

**CF-7 (D; kernel `K-B`, `IM-1`).** Architecture checks enforce:

- correspondence records contain no workspace or artifact locations;
- compiled products contain no surface or publication types;
- Core execution has no filesystem, database, network, clock, random, process,
  environment, or CLI dependency;
- applications perform acquisition and publication outside Core;
- artifact adapters, if later introduced, cannot supply correspondence
  semantics.

Dependency tests are preferable to conventions where project boundaries can
make a forbidden dependency impossible.

## First-Slice Acceptance

**CF-8 (C; `IMPLEMENTATION.md`).** The first implementation slice is accepted
only when:

- its logical `K` is expressible without opaque scripts, paths, inference, or
  generic payload nodes;
- all relevant positive and negative authored fixtures pass;
- the compiler passes `CF-4` for the supported subset;
- the executor passes every applicable law in `CF-5`;
- unsupported constructs fail before execution;
- documentation and diagnostics state the bounded supported subset precisely;
- the stage-5 value measurement is complete.

Passing this gate establishes the bounded slice, not the complete MetaWeave
vision. Expansion requires one real need, one correspondence-model refinement,
one compilation obligation, one execution behavior, and positive and negative
conformance cases.

## Ladder Validation Summary

At the current draft revision:

| Edge | Coherence result | Remaining work |
| --- | --- | --- |
| Kernel -> correspondence | No contradiction found; zero/one/two products, partiality, loss, recovery separation, and boundaries remain representable. | Review whether the minimal logical vocabulary omits any modeled distinction required by real endpoint contracts. |
| Correspondence -> compilation | Every direction responsibility has a validation or derivation obligation; unsupported semantics reject rather than hide code. | Select the first closed predicate and construction representation. |
| Compilation -> execution | Product identity, domain, construction, loss, capability, and diagnostics have runtime preservation rules. | Define concrete result and diagnostic types. |
| Execution -> implementation | All runtime inputs and phases map to neutral Meta boundaries without surface leakage. | Select the first real source/target pair and review the proposed Meta model. |

No edge claims a consistency relation, mutual inversion, incremental
synchronization, in-place reconciliation, or graph-rewrite semantics.

## Review Record Template

```text
Document and revision:
Immediate predecessor and revision:
Kernel keys reviewed:
Derived statements checked:
Chosen statements justified:
Open statements accidentally assumed:
Positive fixtures:
Counterexamples:
Boundary review:
Endpoint realization sketch:
Required predecessor correction:
Decision: revise | accept for next layer
```
