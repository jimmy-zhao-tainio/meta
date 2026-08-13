# MetaWeave Specification Ladder

## Status

This document defines the method and dependency order for deriving MetaWeave
from [`1-KERNEL.md`](1-KERNEL.md) to an implementable architecture. It is not an
additional semantic authority. The kernel remains the sole normative source of
MetaWeave facts.

The documents in the ladder are drafts until their predecessor validation and
conformance obligations are accepted. A later layer may choose a mechanism,
but it may not repair, weaken, or reinterpret an earlier semantic obligation.

Each layer must also contribute a real textual abstraction. Restating its
predecessor as a longer list of obligations is not a rung. A layer is complete
only when it names its input artifact, defines a new output artifact with
concrete syntax or state, and demonstrates the mapping between them on the same
witness.

## The Two Anchors

The ladder is steered from both ends.

The starting anchor is the kernel: first-class declarative correspondence over
complete, valid, neutral workspace state, with independently compiled partial
directions and explicit laws for equality, loss, recovery, and boundaries.

The feasibility anchor is a production implementation in the existing Meta
architecture:

- an authored correspondence is a Meta product document;
- exact source and target contracts are neutral model contracts;
- validation, compilation, and application operate over
  Meta.Operations `InMemoryWorkspace` neutral state;
- Core has no workspace-location or persistence responsibility;
- applications and CLIs acquire and publish workspaces through supported
  surfaces;
- the implementation is derived cleanly from the ladder.

The feasibility anchor constrains the route. It does not create kernel facts.
If the endpoint appears to require contradicting the kernel, the contradiction
must be resolved explicitly at the kernel before dependent documents change.

## Kernel Trace Keys

These keys are non-normative references to existing kernel sections. They make
transitive validation reviewable without duplicating the kernel.

| Key | Kernel obligation |
| --- | --- |
| `K-W` | A workspace is valid complete state `W = (M, I)` under one exact model contract. |
| `K-C` | `K` is authored, first-class declarative correspondence between exactly `M_S` and `M_T`. |
| `K-P` | A validated `K` may compile to zero, one, or two products; `F_K` and `G_K` are independent partial functions and not the authored weave. |
| `K-D` | Each directional domain is explicit; outside-domain application fails without successful output. |
| `K-E` | State equality and model-owned semantic equivalence are distinct and precisely bounded. |
| `K-L` | Successful directions preserve validity, determinism, semantic congruence, nonmutation, atomicity, and freedom from ambient semantics. |
| `K-N` | Canonicalization preserves meaning and stabilizes state; it cannot disguise loss. |
| `K-R` | Recovery is a separate claim with an explicit domain, opposite-domain closure, and an explicit equality strength. |
| `K-I` | Information loss is directional, explicit, and attributable to `K`; modeled information is never silently omitted or invented. |
| `K-A` | Capabilities are derived independently from `K` and the bound contracts, never assigned as unsupported labels. |
| `K-B` | MetaWeave owns modeled state correspondence, not acquisition, persistence, surfaces, artifacts, or orchestration. |

Changing the meaning of one of these keys requires reviewing every downstream
reference to it.

## Foundation Evidence

The repository foundation supplies feasibility evidence, not correspondence
semantics.

| Key | Repository fact | Consequence for the ladder |
| --- | --- | --- |
| `E-1` | `Meta.Operations` owns neutral workspace state, validation, and operations. | A MetaWeave executor need not invent another workspace representation. |
| `E-2` | Meta product models generate typed CLR views over the same semantic state. | An authored `K` can be a sanctioned Meta product document. |
| `E-3` | Workspace surfaces and `Meta.Integration` own loading and publication. | Core can receive and return neutral state while callers own I/O. |
| `E-4` | Product-model changes require deliberate review and generation through the normal model workflow. | The ladder must settle the logical model before proposing generated model or code changes. |
| `E-5` | Foundation projects already separate semantic operations from surfaces and application orchestration. | A clean Core/application boundary is realizable. |

## Layers

```text
KERNEL
  semantic laws
  |
  v
CORRESPONDENCE-MODEL
  K1 authored correspondence calculus
  |
  v
COMPILATION
  DIR1 immutable directional IR
  |
  v
EXECUTION
  E1 abstract machine
  |
  v
IMPLEMENTATION
  Meta entities, CLR values, and services

CONFORMANCE validates every edge and the complete path.
```

### Layer 0: Kernel

Defines irreducible semantic facts and ownership boundaries. It contains no
authoring syntax, compilation plan, API, or adoption policy.

Its output is the set of semantic obligations identified by the kernel trace
keys. It is not expected to be executable.

### Layer 1: Correspondence model

Defines `K1`, a concrete abstract syntax for first-class associations,
directional domains and rules, coverage, loss, and claims. It gives that syntax
a denotation over neutral workspace state and carries one authored witness from
contracts to a mathematical partial direction. It does not define a serialized
Meta metamodel or compiler representation.

### Layer 2: Compilation

Defines `DIR1`, a concrete immutable intermediate representation with normalized
domain tests, typed constructors and writes, input-fate and loss tables, a
coverage certificate, and capability evidence. It defines a lowering relation
from each `K1` construct to `DIR1` and a denotation-preservation law. It does
not prescribe compiler passes, code generation, or plan serialization.

### Layer 3: Execution

Defines `E1`, an abstract machine with named state, phases, transition rules,
private candidate state, failure states, and one atomic success state. The
machine consumes `DIR1` directly and realizes its denotation operationally.
It does not acquire or publish workspaces.

### Layer 4: Implementation

Maps every `K1`, `DIR1`, and `E1` construct onto named Meta entities, immutable
CLR value variants, service signatures, project dependencies, and a bounded
delivery slice. No semantic construct may first appear here without a required
predecessor revision. The document defines decision gates without authorizing
model or code changes.

### Cross-cutting: Conformance

Carries the same golden correspondence through authored records, compiled IR,
machine states, and implementation values. It checks structural mapping and
semantic preservation at every edge, then adds counterexamples and reusable
law suites. Passing examples demonstrate bounded support; they do not prove
unbounded recovery or equivalence claims.

## Statement Classes

Every subordinate specification distinguishes three kinds of statement:

- **Derived (`D`)**: required by its predecessor or transitively by the kernel.
- **Chosen (`C`)**: one design choice among multiple kernel-compatible choices.
- **Open (`O`)**: deliberately unresolved and unavailable as an assumed fact.

A chosen statement must name the obligation it satisfies. An open statement
must not be depended upon by compilation, execution, capability reporting, or
implementation acceptance.

## Validation Protocol

Each layer is promoted only after six checks.

1. **Concrete increment:** the layer defines a named output artifact, its
   concrete syntax or state, and the input artifact it consumes. The output is
   more specific than the input without being an implementation accident.
2. **Predecessor closure:** every derived statement cites an obligation from
   the immediately preceding document, and every output construct traces to an
   input construct or an explicit design choice.
3. **Kernel conservativity:** no statement contradicts a kernel trace key or
   silently strengthens an optional kernel claim into a universal one.
4. **Boundary discipline:** the layer introduces only concepts appropriate to
   its abstraction level.
5. **Counterexample review:** partial directions, absent directions, lossy
   directions, and nonrecovering bidirectional cases remain representable.
6. **Endpoint realizability:** at least one implementation in the existing
   Meta architecture can satisfy the layer without moving persistence or
   artifact semantics into Core.

Validation is transitive but local. A layer is checked directly against its
predecessor; the trace keys then make its relationship to the kernel visible.
No later implementation test can compensate for a missing correspondence
invariant, and no CLI check can compensate for an under-specified compiler.

## Promotion Record

Review of a layer records:

```text
Layer:
Predecessor revision:
Input artifact:
New output artifact:
Concrete syntax or state introduced:
Witness mapping:
Derived obligations satisfied:
Choices introduced:
Open questions preserved:
Counterexamples considered:
Endpoint feasibility evidence:
Contradictions or required predecessor changes:
Decision: revise | accept for next layer
```

The record is design evidence, not runtime provenance.

## Scope Discipline

Historical weaving systems are relevant precedent for first-class
correspondence. They do not supply MetaWeave semantics by inheritance. In
particular, this ladder does not presume a primary consistency relation, graph
rewriting, incremental synchronization, in-place reconciliation, trace models,
or a standard-specific metamodel.

Extensions, provenance, composition, and adoption are later branches. They
may be developed once the direct kernel-to-execution path is coherent. They
cannot be used to make that path coherent retroactively.
