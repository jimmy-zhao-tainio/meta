# Meta foundation packages

Development NuGet packages for the `meta` foundation. The package and public
namespace boundaries are deliberately aligned:

- `Meta.Operations`
- `Meta.TypedModels`
- `Meta.Core`
- `Meta.Surfaces`
- `Meta.Surfaces.Xml`
- `Meta.Surfaces.CSharp`
- `Meta.Surfaces.Sql`
- `Meta.Integration`
- `MetaCli.Model`
- `MetaCli.Core`
- `MetaWeave.Model`
- `MetaWeaveScript.Execution`
- `MetaWeaveScript.Sql`
- `MetaWeave.Core`

`Meta.Operations`, `Meta.TypedModels`, and `Meta.Core` are representation-neutral. `Meta.Surfaces`
is independent shared descriptor and publication infrastructure. XML and C#
depend on Operations and common Surfaces; XML also uses TypedModels to map
typed CLR contracts to semantic workspace state. SQL depends on Operations
and owns its complete DDL model. `Meta.Integration` is the intentional
composition package.

These packages are consumed by downstream repositories to keep the foundation
boundary explicit and prevent source-level drift back into Core.

The complete foundation set is packed by `pack-internal.cmd`. The script name
and the current `-internal` version suffix identify the unreleased development
channel; they do not imply that the source or package contracts are private.

- `Meta.Operations`
- `Meta.TypedModels`
- `Meta.Core`
- `Meta.Surfaces`
- `Meta.Surfaces.Xml`
- `Meta.Surfaces.CSharp`
- `Meta.Surfaces.Sql`
- `Meta.Integration`
- `MetaCli.Model`
- `MetaCli.Core`
- `MetaWeave.Model`
- `MetaWeaveScript.Execution`
- `MetaWeaveScript.Sql`
- `MetaWeave.Core`

Package consumers supply the feed explicitly during restore. A sibling checkout is not a package source and does not change the dependency graph.

