# MetaWeave Compilation

## Status

This is draft layer 2 of the MetaWeave specification ladder. It is subordinate
to [`KERNEL.md`](KERNEL.md) and derives from
[`CORRESPONDENCE-MODEL.md`](CORRESPONDENCE-MODEL.md).

This document defines the semantic contract of compilation. It does not select
a compiler algorithm, serialized plan format, generated-code strategy, or
runtime data structure.

## Layer Question

The correspondence layer defines what authored `K` means and when a directional
definition is complete. This layer answers:

> How does a validated declarative `K` become zero, one, or two independently
> executable directional products without hiding converter logic behind it?

## Compilation Function

Compilation has the abstract form:

```text
Compile(K, M_S, M_T) -> CompilationResult
```

The supplied contracts must be the exact contracts bound by `K`. They are
semantic inputs, not workspaces to transform. Compilation reads no source or
target instance graph.

A successful result has the abstract form:

```text
P_K = (K-revision, M_S-identity, M_T-identity,
       P_F?, P_G?, capabilities, evidence)
```

`P_F` and `P_G` are optional immutable compiled directional products. Their
observable meanings are `F_K` and `G_K`. `P_K` is a packaging form for the
products and their derivation; it is not a third transformation.

## Preconditions

**CP-1 (D; `CM-1`, `CM-14`).** Compilation requires a definition-valid `K` and
the exact source and target contracts bound by that revision. Contract mismatch
is a compilation failure, not a compatible-looking fallback.

**CP-2 (D; `CM-2`).** Compilation has no workspace location, surface,
representation, environment, registry, or artifact input. Human-readable names
may appear in diagnostics but cannot change resolution.

**CP-3 (C; satisfies `CM-1`).** A production implementation uses deterministic
semantic contract identities. The identity scheme covers every model fact that
can affect validation, equality, domain, or output meaning. The concrete digest
algorithm remains an implementation choice.

## Independent Direction Assessment

**CP-4 (D; `CM-11`, `CM-15`).** The compiler assesses `Delta_F` and `Delta_G`
independently. For each orientation it reports one of:

- **Absent:** no authored directional definition exists;
- **Rejected:** a definition exists but is invalid or incomplete;
- **Compiled:** a complete definition produced an executable product.

An absent direction is not an error in `K`. A rejected direction prevents a
successful compilation result for the correspondence revision and carries
stable diagnostics. No executable products are returned from that invalid
revision. A compiler must not silently omit a malformed authored direction and
report it as absent.

**CP-5 (D; `CM-11`, `CM-16`).** Compilation never manufactures one direction
from the other. Shared authored facts may lower into both products, but each
product has its own domain, construction, loss, and capability derivation.

Consequently a successful `P_K` contains exactly zero, one, or two products.

## Compilation Obligations

For each present direction, compilation performs the following semantic work.
The order is explanatory; it does not prescribe compiler passes.

### Resolve exact semantic references

**CP-6 (D; `CM-1`, `CM-15`).** Every entity, identity, property, relationship,
order, predicate, and canonicalization reference resolves against the correct
bound contract and directional scope. Resolution by naming convention or
runtime reflection is prohibited.

### Establish scope and ownership

**CP-7 (D; `CM-4`, `CM-9`).** Every rule input is bound explicitly, and every
constructed destination fact has exactly one owner in a given execution path.
The compiler rejects out-of-scope reads, ambiguous destination ownership, and
implicit dependence on traversal or container order.

### Derive the domain

**CP-8 (D; `CM-3`).** The compiled product contains an executable decision for
membership in the authored domain. The decision implements the modeled
predicate exactly. It does not enlarge the domain through recovery, repair,
warning policy, or best effort.

The decision incorporates every data-dependent precondition needed for
identity uniqueness, complete construction, and valid output. Those conditions
cannot be deferred as expected failures after an input has been admitted.

Compilation establishes that supported domain predicates are invariant under
the input contract's semantic equivalence. If this cannot be established, the
direction is rejected or the domain definition is revised explicitly.

If the supported predicate vocabulary cannot decide an authored domain, the
direction is rejected rather than compiled with a hidden callback.

### Close output construction

**CP-9 (D; `CM-4` through `CM-9`).** The compiler establishes that every
reachable execution path constructs a complete candidate output under the
exact destination contract. Identity production is deterministic, required
facts are covered, relationships refer to deterministically constructed
targets, possible collisions are excluded by the explicit domain, and
uncovered facts reject the direction.

The compiler also establishes semantic congruence for the supported
construction vocabulary. A direction whose rules can distinguish equivalent
inputs in a way that produces nonequivalent outputs is rejected.

Static establishment may be combined with mandatory runtime checks where a
property depends on input data. Such a check either participates in the domain
decision or defensively detects a nonconformant product. It is not permission to
define an ordinary failure for an otherwise admitted input or to expose partial
output.

### Derive loss and capabilities

**CP-10 (D; `CM-10`, `CM-16`).** The compiler derives directional loss from
source accounting and construction behavior. It rejects undeclared loss and
does not turn a declaration into a claim of safety, validity, or recovery.

**CP-11 (D; `CM-16`).** The compiler derives, independently for each direction:

- existence;
- the explicit domain and whether it is total;
- complete-output status;
- directional loss;
- dependencies on unresolved assumptions.

It derives source and target recovery only when the authored claim, both
products, opposite-domain closure, canonicalization, and required comparison
law are established. Two compiled products alone never yield a reversibility
capability.

### Produce immutable products

**CP-12 (D; kernel `K-L`).** A compiled directional product is immutable and
fully determined by the `K` revision, exact contract identities, and explicit
semantic inputs accepted by this layer. Recompiling equal inputs produces
extensionally equal product meaning and capability evidence.

The compiled representation may be interpreted, lowered to code, or encoded as
an internal plan. Those strategies are equivalent only if they preserve the
same observable domain, result, loss, failure, and evidence contracts.

## Minimum Product Contract

Without prescribing representation, every `P_F` or `P_G` exposes or carries:

- the correspondence revision from which it was derived;
- its exact input and output contract identities;
- its directional orientation;
- its domain membership semantics;
- complete deterministic output-construction semantics;
- runtime checks required for data-dependent obligations;
- directional loss classification;
- derived capability evidence;
- stable semantic identities sufficient for diagnostics and conformance.

It contains no workspace, mutable execution state, surface descriptor,
location, output destination, CLI option, or implementation delegate that was
not explicitly represented by an allowed semantic contract.

## Diagnostics and Evidence

**CP-13 (C; supports `CM-14` through `CM-16`).** Compilation returns structured
diagnostics with stable codes, severity, direction, correspondence element,
model endpoint, and explanatory detail. Physical authoring locations may be
attached by an application, but are not semantic identities.

**CP-14 (D; `CM-16`).** Capability evidence distinguishes:

- **Established:** derivable from supported modeled semantics and checked
  obligations;
- **Refuted:** a contradiction or counterexample is known;
- **Unresolved:** the available semantics do not establish or refute the
  claim.

Examples and runtime samples may add empirical confidence. They cannot turn an
unresolved universal recovery claim into an established one.

## Failure Boundary

Compilation failure returns no executable product for the correspondence
revision. Diagnostics may describe multiple independent defects and may report
which otherwise independent direction they affect, but an implementation must
not publish a plan that relies on execution to guess missing semantics.

A complete `K` with an intentionally absent direction compiles successfully
without that product. A `K` with no directional definitions may therefore
compile successfully to capability evidence and zero products.

## Predecessor Validation

| Requirement | Correspondence basis | Validation result |
| --- | --- | --- |
| Authored truth remains distinct from executable products | Logical `K` and `Delta` form | `Compile` consumes `K`; `P_F/P_G` are derived immutable products. |
| Zero, one, or two directions | `CM-11`, `CM-14`, `CM-15` | Absent/rejected/compiled are explicit and assessed independently. |
| Explicit domains | `CM-3` | Each product carries exact domain membership semantics. |
| Complete construction | `CM-4` through `CM-9` | Scope, ownership, identity, relationship, order, and coverage checks close output construction. |
| Explicit loss | `CM-10` | Loss is derived and undeclared loss rejects compilation. |
| Claims are not labels | `CM-12`, `CM-13`, `CM-16` | Capabilities carry established/refuted/unresolved evidence. |
| No hidden converter | `CM-2`, `CM-15` | Unsupported semantics reject compilation; opaque callbacks are not a fallback. |

The predecessor's counterexamples survive compilation: zero products, one
product, two independent lossy products, partial domains, and unresolved
recovery are all distinct results.

## Endpoint Feasibility

The product contract can be represented by immutable .NET objects in
`MetaWeave.Core`, using neutral model contracts for endpoint resolution. A
first implementation can interpret an in-memory plan; generated code and plan
serialization are unnecessary for semantic completeness.

This layer requires no change to workspace surfaces.

## Open Questions

- **CP-O1:** Whether compiled products become durable, serializable artifacts.
- **CP-O2:** Which closed predicate and construction vocabulary is sufficient
  for the first authorable MetaWeave model.
- **CP-O3:** Which proof obligations can be established statically and which
  require deterministic runtime checks.
- **CP-O4:** Whether externally supplied semantic functions are admitted in the
  first implementation. Until specified by `EXTENSIONS.md`, they are
  unsupported rather than ambient callbacks.
