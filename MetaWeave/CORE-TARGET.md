# MetaWeave Core Target

## Purpose

MetaWeave defines and applies semantic correspondences between modeled
workspace states. It is the bidirectional transformation boundary between two
model contracts; persistence, workspace surfaces, orchestration, and physical
artifacts remain outside it.

## Normative Foundation

[`KERNEL.md`](KERNEL.md) is the sole normative foundation. Every MetaWeave
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
```

Later documents may refine mechanisms within their own layer. They may not
silently weaken, broaden, or reinterpret a kernel fact. If implementation work
reveals that a kernel fact must change, the kernel is revised explicitly before
dependent specifications are adjusted.

## Specification Set

Only the kernel and this map are active today. The remaining documents are
planned and must be designed independently rather than extracted mechanically
from the former monolithic target.

| Document | Status | Responsibility |
| --- | --- | --- |
| `KERNEL.md` | Active | Irreducible semantic facts and laws. |
| `CORRESPONDENCE-MODEL.md` | Planned | Authorable semantic vocabulary and definition-time validity. |
| `COMPILATION.md` | Planned | Conversion of a valid correspondence into an immutable execution plan. |
| `EXECUTION.md` | Planned | Directional application, atomic results, and runtime validation. |
| `EXTENSIONS.md` | Planned | Explicit externally supplied semantic functions and their trust boundary. |
| `PROVENANCE.md` | Planned | Logical derivation, trace, explanation, and retention. |
| `COMPOSITION.md` | Planned | Composition of independently valid correspondences. |
| `CONFORMANCE.md` | Planned | Executable laws, fixtures, counterexamples, and acceptance criteria. |

`EXTENSIONS.md` may refine the correspondence, compilation, and execution
layers without creating ambient semantics. `PROVENANCE.md` may observe
execution without changing its result. `COMPOSITION.md` must derive composed
domains, loss, and recovery from the kernel rather than inherit labels.
`CONFORMANCE.md` tests each completed layer against the kernel.

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
