# Agent Context

## Current State

Date: 2026-08-04

Branch: `master`

The representation-neutral `Meta.Operations` core and its representation
implementations are adopted across the current foundation and BI consumers.
The detailed slice ledger is
`docs/META-OPERATIONS-MIGRATION.md`.

Current boundary:

- `InMemoryWorkspace` is model plus instance data.
- `IMetaWorkspaceSource` owns common reads.
- `InMemoryOperations` and `SqlOperations` implement the common operation
  language.
- `XmlWorkspaceReader` and `XmlWorkspaceWriter` own XML configuration,
  layout, shards, locking, stale-write detection, and publication.
- `Workspace`, `IWorkspaceService`, and `WorkspaceService` have been
  removed.
- The `meta` CLI, MetaWeave, MetaDocs generic imports, and downstream
  MetaSchema, MetaConvert, MetaSql, MetaDataVault, and MetaTransform consumers
  use the new boundaries.
- No product model changed in this migration.

## Verification

Closure verification on 2026-08-04 passed:

- `Metadata.Framework.sln` build: 0 warnings, 0 errors
- full foundation suite: 399/399
- all ten standard `meta-bi` CLI executables built from freshly packed local
  `Meta.Operations`, `Meta.Core`, and `Meta.Adapters` packages with 0 warnings
  and 0 errors
- repository searches found no remaining `Meta.Core.Domain.Workspace`,
  `IWorkspaceService`, or `WorkspaceService` code path; the generated
  workspace-configuration model still legitimately has a `Workspace` POCO

Post-deletion verification passed:

- MetaWeave: 21/21
- MetaDocs: 43/43
- semantic/XML workspace merge: 3/3
- external `meta` CLI behavior: 131/131
- `Metadata.Framework.sln`: zero warnings and errors
- all ten standard `meta-bi` CLI executables against freshly packed local
  foundation packages: zero warnings and errors
- MetaSql complete project: 107/107 in approximately 4m15s
- MetaDataVault complete project: 55/55
- MetaTransform conversion tests: 9/9

MetaConvert forced rebuilds pass with zero warnings. Its Business Data Vault
projection now omits fully absent optional implementation columns and rejects
half-specified name/type pairs explicitly.

## Remaining Work

- Keep artifact-directory generation ownership separate from semantic
  workspace operations.

## Constraints

- Do not reintroduce compatibility wrappers or aliases.
- Do not change product models as part of operation-core migration.
- XML, SQL, and C# are representations of the same modeled structure; XML
  layout is not semantic truth.
- Build and test serially.
