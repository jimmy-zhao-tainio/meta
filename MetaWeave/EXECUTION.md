# MetaWeave Execution

## Status

This is draft layer 3 of the MetaWeave specification ladder. It is subordinate
to [`KERNEL.md`](KERNEL.md) and derives from
[`COMPILATION.md`](COMPILATION.md).

This document defines application of one compiled directional product to one
neutral workspace. It does not define workspace loading, publication, command
behavior, plan serialization, or physical artifacts.

## Layer Question

Compilation produces immutable directional products whose observable meanings
are `F_K` and `G_K`. This layer answers:

> What must happen when one product is applied so that its partial-function
> semantics and every directional kernel law remain true?

## Application Contract

For a compiled forward product `P_F`:

```text
Apply(P_F, S) -> Success(T, evidence) | Failure(diagnostics)
```

For a compiled reverse product `P_G`:

```text
Apply(P_G, T) -> Success(S, evidence) | Failure(diagnostics)
```

The returned workspace is complete neutral state under the exact output
contract. `Apply` does not publish it. The evidence describes this application;
it does not alter the semantic result.

If future extension implementations are permitted, their exact bindings become
additional explicit application inputs. Until `EXTENSIONS.md` defines that
contract, a compiled product depending on an external semantic function is not
executable.

## Input Acceptance

**EX-1 (D; `CP-1`, `CP-12`).** The product is internally valid, immutable, and
bound to exact input and output contract identities. A corrupt, unsupported, or
contract-incompatible product fails before evaluation.

**EX-2 (D; kernel `K-W`, `K-D`).** The input is a complete valid workspace under
the product's exact input contract. Invalid input and contract mismatch are
failures distinct from being outside the domain.

**EX-3 (D; `CP-8`).** Domain membership is decided before semantic success. An
input outside the compiled domain returns a domain failure and no successful
workspace. A caller option, warning policy, or output destination cannot change
that decision.

## Evaluation Boundary

**EX-4 (D; `CP-9`, kernel `K-L`).** Evaluation constructs a fresh candidate
workspace. It never mutates the input workspace or exposes a partially
constructed candidate as successful state.

An implementation may use internal mutable builders for efficiency only when
they are unobservable, isolated from the input, and discarded completely on
failure.

**EX-5 (D; `CP-12`, kernel `K-L`).** Evaluation depends only on:

- the compiled product;
- the accepted input workspace;
- explicit semantic inputs admitted by a subordinate specification.

It cannot observe workspace location, storage representation, time, randomness,
process state, environment variables, network state, a mutable global registry,
or incidental collection order.

**EX-6 (D; `CP-7`, `CP-9`).** Record, identity, value, relationship, and order
construction follows the compiled ownership and dependency semantics exactly.
No runtime name matching, reflection inference, implicit merge, repair, or
defaulting may fill a missing plan decision.

## Candidate Validation

**EX-7 (D; kernel `K-L`).** A candidate becomes a successful output only after
validation against the exact destination contract and all compiled
data-dependent obligations.

Validation includes at least:

- entity membership and identity uniqueness;
- required value and relationship presence;
- relationship target integrity and cardinality;
- modeled significant order;
- compiled collision, coverage, and domain-postcondition checks.

Product-specific validity beyond structural conformance is part of the exact
model contract when that validity affects `W_M`. It cannot be skipped merely
because the generic structure is well formed.

For a conformant product and an input admitted to its domain, candidate
validation succeeds. `InvalidOutput` is a defensive indication of a defective
product, semantic binding, or executor; it is not a modeled branch of the
partial function.

**EX-8 (D; kernel `K-I`).** Execution records every triggered declared loss and
fails on behavior that would create undeclared loss or invention. A declared
loss remains visible application evidence; it is not downgraded to a debug log.

## Result Atomicity

**EX-9 (D; kernel `K-L`).** Success returns exactly one complete valid output
workspace and application evidence. Failure returns no workspace that a caller
may treat as the directional result.

Diagnostics may contain bounded excerpts or semantic references needed to
explain failure. They do not contain a recoverable partial result masquerading
as output.

Cancellation is a failure and follows the same atomicity rule.

## Determinism and Congruence

**EX-10 (D; kernel `K-L`).** Applying the same compiled product and equal
explicit semantic inputs to state-equal input workspaces produces state-equal
successful outputs or the same semantic failure classification.

Diagnostic presentation order may be canonicalized independently, but
nondeterministic discovery order cannot affect whether execution succeeds.

**EX-11 (D; kernel `K-E`, `K-L`).** Semantically equivalent accepted inputs
produce semantically equivalent outputs. This obligation is checked under the
equivalence rules owned by the exact input and output model contracts. An
implementation cannot invent an equivalence by ignoring state.

## Failure Taxonomy

**EX-12 (C; supports atomic diagnostics).** Application distinguishes at least:

| Failure | Meaning |
| --- | --- |
| `InvalidProduct` | The supplied compiled product cannot be trusted or interpreted. |
| `InputContractMismatch` | The input is bound to a different model contract. |
| `InvalidInput` | The input does not belong to the valid workspace space of the bound contract. |
| `OutsideDomain` | The input is valid but not in the product's explicit partial domain. |
| `MissingSemanticInput` | A required explicit semantic binding is absent or incompatible. |
| `EvaluationFailure` | The executor or an explicit semantic binding failed to realize a product for an admitted input; this is not a normal result of a conformant product. |
| `InvalidOutput` | The complete candidate failed its exact target contract or compiled postconditions, revealing a conformance defect. |
| `Cancelled` | The caller cancelled before atomic success. |

Stable diagnostic codes refine these classes. Exception types, result unions,
and localization are implementation choices.

## Capabilities at Runtime

**EX-13 (D; `CP-11`, `CP-14`).** Execution does not promote capabilities.
Successful examples do not establish totality, losslessness, recovery, or
mutual inversion beyond the evidence already attached to the compiled
correspondence.

Runtime counterexamples may refute a claim or reveal a compiler defect. They do
not silently change the semantics of the product being executed.

**EX-14 (D; kernel `K-R`).** Round-trip verification is a separate operation
over explicit recovery domains. Ordinary forward or reverse application does
not automatically invoke the opposite direction or report recovery.

## Observation and Explanation

**EX-15 (C; preserves kernel `K-L`).** Execution evidence may identify the
correspondence revision, product, input and output contract identities,
triggered rules, and declared loss. Observation must be deterministic with
respect to semantic inputs and must not influence output construction or
success.

Full logical provenance, retention, streaming, replay, and explanation queries
remain deferred to `PROVENANCE.md`. The absence of that specification does not
permit untraceable hidden semantics; it limits only the detail and retention of
observation.

## Concurrency and Reuse

**EX-16 (C; follows `CP-12`).** Compiled products are concurrently reusable.
All per-application state is isolated. Caches are permitted only when their keys
include every semantic input and their presence cannot change observable
results or failure classifications.

## Predecessor Validation

| Requirement | Compilation basis | Validation result |
| --- | --- | --- |
| Exact product and contract identity | `CP-1`, `CP-3`, `CP-12` | Product and input are checked before evaluation. |
| Explicit domain | `CP-8` | Domain membership precedes evaluation and cannot be enlarged by policy. |
| Closed deterministic construction | `CP-7`, `CP-9`, `CP-12` | Runtime follows compiled ownership and rejects collisions or missing obligations. |
| Complete valid output | `CP-9` | Candidate validation gates atomic success. |
| Derived loss and capabilities | `CP-10`, `CP-11`, `CP-14` | Execution reports loss but does not manufacture stronger claims. |
| No hidden semantic input | `CP-2`, `CP-12` | Only product, workspace, and explicitly specified semantic bindings are observable. |
| Structured failure | `CP-13` | Failure classes preserve direction and semantic references without partial output. |

The compilation counterexamples remain distinct at runtime: absent products
cannot be applied, valid outside-domain inputs fail cleanly, lossy directions
may succeed with explicit loss evidence, and two successful directions still
do not establish recovery.

## Endpoint Feasibility

The contract maps directly to a pure Core service accepting immutable compiled
products and neutral `InMemoryWorkspace` state. A caller can load and publish
through any supported surface without the executor knowing which surface was
used.

Atomicity can be achieved with an isolated candidate builder followed by the
neutral validation boundary.

## Open Questions

- **EX-O1:** The exact structured result and diagnostic types.
- **EX-O2:** Whether application evidence is returned inline or through a
  separate observer in the first implementation.
- **EX-O3:** Resource limits and deterministic failure for inputs too large to
  evaluate within configured explicit limits.
- **EX-O4:** The recovery-verification API after the direct application contract
  is implemented and proven.
