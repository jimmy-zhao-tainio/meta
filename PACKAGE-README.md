# Meta foundation packages

Internal NuGet packages for the `meta` foundation:

- `Meta.Operations`
- `Meta.Core`
- `Meta.Surfaces`
- `Meta.Integration`
- `MetaCli.Model`
- `MetaCli.Core`
- `MetaWeave.Model`
- `MetaWeave.Core`

These packages are consumed by downstream repositories to keep the foundation boundary explicit and prevent source-level drift back into core.

The complete foundation set is packed by `pack-internal.cmd`:

- `Meta.Operations`
- `Meta.Core`
- `Meta.Surfaces`
- `Meta.Integration`
- `MetaCli.Model`
- `MetaCli.Core`
- `MetaWeave.Model`
- `MetaWeave.Core`

Package consumers supply the feed explicitly during restore. A sibling checkout is not a package source and does not change the dependency graph.

