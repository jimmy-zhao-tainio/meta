# Meta foundation packages

Internal NuGet packages for the `meta` foundation. The package and public
namespace boundaries are deliberately aligned:

- `Meta.Operations`
- `Meta.Core`
- `Meta.Surfaces`
- `Meta.Surfaces.Xml`
- `Meta.Surfaces.CSharp`
- `Meta.Surfaces.Sql`
- `Meta.Integration`
- `MetaCli.Model`
- `MetaCli.Core`
- `MetaWeave.Model`
- `MetaWeave.Core`

`Meta.Operations` and `Meta.Core` are representation-neutral. `Meta.Surfaces`
is lightweight shared infrastructure; the XML, C#, and SQL implementations are
separate packages. `Meta.Integration` is the intentional composition package.

These packages are consumed by downstream repositories to keep the foundation
boundary explicit and prevent source-level drift back into Core.

The complete foundation set is packed by `pack-internal.cmd`:

- `Meta.Operations`
- `Meta.Core`
- `Meta.Surfaces`
- `Meta.Surfaces.Xml`
- `Meta.Surfaces.CSharp`
- `Meta.Surfaces.Sql`
- `Meta.Integration`
- `MetaCli.Model`
- `MetaCli.Core`
- `MetaWeave.Model`
- `MetaWeave.Core`

Package consumers supply the feed explicitly during restore. A sibling checkout is not a package source and does not change the dependency graph.

