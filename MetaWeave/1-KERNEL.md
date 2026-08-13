# MetaWeave Kernel

## Status

This document is the normative semantic foundation of MetaWeave. It contains
only facts that every implementation and subordinate specification must
preserve. It does not prescribe mechanisms or policy.

## Workspace

A workspace is:

```text
W = (M, I)
```

`M` is a model contract and `I` is an instance graph. `Valid(W)` means that `M`
is internally valid and `I` conforms to it. Let `W_M` denote all valid
workspaces conforming to the exact contract `M`.

MetaWeave transforms complete workspace state. It does not transform a surface,
file, database, or other physical representation.

## Correspondence

A correspondence `K` is a first-class declarative correspondence model. It
expresses modeled correspondence between one source workspace contract `M_S`
and one target workspace contract `M_T`. A validated `K` may be compiled into
zero, one, or two executable directional transformations:

```text
F_K : D_F -> W_M_T    where D_F subset-of W_M_S
G_K : D_G -> W_M_S    where D_G subset-of W_M_T
```

`F_K` and `G_K` are compiled products of `K`, not the authored weave or
handwritten converter logic hidden behind the correspondence. They remain
independent partial functions: `F_K` is the forward direction and `G_K` is the
reverse direction. Either may be absent. The presence of both implies neither
recovery nor mutual inversion.

Each domain is explicit. Applying a direction outside its domain fails without
returning a successful workspace. Documentation, policy, or best-effort
behavior cannot enlarge a domain.

## Equality

For workspaces bound to `M`,

```text
W1 ≡_M W2
```

means extensional state equality: the same modeled records, identities, values,
relationships, and significant order. It ignores only runtime object identity
and incidental enumeration order that `M` declares insignificant.

```text
W1 ≈_M W2
```

means semantic equivalence under rules explicitly owned by `M`. It is reflexive,
symmetric, and transitive. State equality implies semantic equivalence:

```text
W1 ≡_M W2  =>  W1 ≈_M W2
```

If `M` declares no nontrivial equivalence, the two relations coincide.

## Directional Laws

For either direction, every successful application obeys:

```text
Valid(input) and input in domain  =>  Valid(output)
```

- **Determinism:** state-equal inputs under the same correspondence revision and
  explicitly bound semantics produce state-equal outputs.
- **Semantic congruence:** semantically equivalent inputs produce semantically
  equivalent outputs.
- **Nonmutation:** application does not change its input, whether it succeeds or
  fails.
- **Atomic result:** failure exposes no partial output as success.
- **No ambient semantics:** output does not depend on workspace location,
  persistence representation, time, randomness, process state, environment,
  network access, or an implicit registry. Any semantics beyond `K` and its
  bound model contracts are explicit inputs.

## Canonicalization

An optional canonicalizer `C_M : W_M -> W_M` preserves meaning and reaches a
stable state:

```text
C_M(W) ≈_M W
C_M(C_M(W)) ≡_M C_M(W)
```

If it claims one canonical state for every semantic equivalence class, it also
obeys:

```text
W1 ≈_M W2  =>  C_M(W1) ≡_M C_M(W2)
```

Changing or discarding modeled meaning is loss, not canonicalization.

## Recovery

Directional executability and recovery are separate facts. A source recovery
claim has an explicit domain `R_S`; a target recovery claim has `R_T`.

They require opposite-domain closure:

```text
R_S subset-of D_F    and    F_K(R_S) subset-of D_G
R_T subset-of D_G    and    G_K(R_T) subset-of D_F
```

Canonical source recovery requires:

```text
for every S in R_S: G_K(F_K(S)) ≡_M_S C_S(S)
```

Canonical target recovery requires:

```text
for every T in R_T: F_K(G_K(T)) ≡_M_T C_T(T)
```

A recovery claim may instead promise only semantic equivalence under `≈`. A
result outside the opposite domain refutes recovery; it never makes the law
vacuously inapplicable. Exact recovery is the special case in which the
canonicalizer is the identity.

## Loss

Information loss is directional, explicit, and attributable to `K`. No
application may silently omit, merge, invent, truncate, default, or normalize
modeled information. A reverse direction may still be useful when a forward
direction is lossy, but it does not thereby recover the original source.

## Capabilities

Capabilities are derived from the correspondence and model contracts; they are
not author-assigned labels. Derivation determines independently:

- whether each direction exists and whether its domain is total;
- whether each direction produces complete valid output;
- what information each direction loses;
- whether source recovery holds and on which domain;
- whether target recovery holds and on which domain.

Unestablished recovery is not reversibility.

## Boundary

MetaWeave owns semantic correspondence between modeled workspace states. It has
no knowledge of workspace acquisition, persistence, publication, surfaces,
files, directories, databases, source code, serialization formats, connection
strings, command lines, or physical artifacts.

Applications own orchestration. Workspace surfaces own persistence. A future
artifact adapter may faithfully read, write, or execute its artifact contract;
it may not decide domain-to-artifact semantics.
