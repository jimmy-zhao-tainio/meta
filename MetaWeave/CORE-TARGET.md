# MetaWeave Core Target

## Purpose

MetaWeave defines and applies semantic correspondences between modeled
workspace states. It is the directional correspondence and transformation
boundary between two model contracts; either direction may be absent.
Persistence, workspace surfaces, orchestration, and physical artifacts remain
outside it.

## Normative Foundation

[`1-KERNEL.md`](1-KERNEL.md) is the sole normative foundation. Every MetaWeave
specification and implementation must preserve its vocabulary, domains, laws,
loss rules, recovery rules, and ownership boundary.

The kernel states what must be true. Subordinate specifications state how a
particular layer makes it true.

## Document Dependency

The primary dependency direction is:

```text
KERNEL
  |
  v
CORRESPONDENCE-MODEL
  |
  v
COMPILATION
  |
  v
EXECUTION
  |
  v
IMPLEMENTATION

CONFORMANCE validates every edge and the complete path.
```

Later documents may refine mechanisms within their own layer. They may not
silently weaken, broaden, or reinterpret a kernel fact. If implementation work
reveals that a kernel fact must change, the kernel is revised explicitly before
dependent specifications are adjusted.

## Specification Set

The kernel is the sole normative semantic foundation. The ladder documents are
active design drafts: they are reviewed in dependency order and do not become
kernel facts merely by being more concrete. Remaining planned documents must
be designed independently rather than extracted mechanically from the former
monolithic target.

| Document | Status | Responsibility |
| --- | --- | --- |
| `0-LADDER.md` | Active | Derivation method, trace keys, layer order, and predecessor-validation protocol. |
| `1-KERNEL.md` | Active | Irreducible semantic facts and laws. |
| `2-CORRESPONDENCE-MODEL.md` | Draft | Concrete `K1` authored correspondence calculus, denotation, and definition-time validity. |
| `3-COMPILATION.md` | Draft | Concrete `DIR1` directional IR and denotation-preserving lowering from valid `K1`. |
| `4-EXECUTION.md` | Draft | Concrete `E1` abstract machine, transitions, atomic results, and runtime validation. |
| `5-IMPLEMENTATION.md` | Draft | One-to-one Meta/.NET realization of `K1`, `DIR1`, and `E1`, plus bounded value gates. |
| `EXTENSIONS.md` | Planned | Explicit externally supplied semantic functions and their trust boundary. |
| `PROVENANCE.md` | Planned | Logical derivation, trace, explanation, and retention. |
| `COMPOSITION.md` | Planned | Composition of independently valid correspondences. |
| `6-CONFORMANCE.md` | Draft | One golden witness through every rung, local preservation checks, counterexamples, and acceptance criteria. |

`EXTENSIONS.md` may refine the correspondence, compilation, and execution
layers without creating ambient semantics. `PROVENANCE.md` may observe
execution without changing its result. `COMPOSITION.md` must derive composed
domains, loss, and recovery from the kernel rather than inherit labels.
`5-IMPLEMENTATION.md` closes only choices required for a bounded greenfield
slice. `6-CONFORMANCE.md` tests each completed layer against its immediate
predecessor and the kernel transitively.

## Ownership

- MetaWeave owns semantic correspondence over neutral workspace state.
- Applications acquire workspaces, select operations, and publish results.
- Workspace surfaces own physical representation and persistence.
- Future artifact-model packages describe artifact semantics as models.
- Future artifact adapters read, write, or execute physical artifacts and
  contain no domain-to-artifact transformation logic.

No subordinate specification may move surface or artifact concerns into
MetaWeave to simplify its own implementation.

## Previous Target

The former monolithic `CORE-TARGET.md` combined the kernel with proposed
authoring, compilation, execution, extension, provenance, composition, and
scalability mechanisms. It remains available in Git history through commit
`9975c034cba1d10ee4f2ac5b8deb5f16599e209c`, but it is not active architecture
and must not be used as the outline for the planned specifications.
