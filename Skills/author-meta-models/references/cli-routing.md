# CLI routing

Run `<cli> help` for the current command list and `<cli> help <command>` before
invocation. This map selects ownership; it is not a copied command reference.

## Foundation

| Need | Owning CLI |
| --- | --- |
| Inspect or operate generic workspace surfaces | `meta` |
| Author a modeled CLI workspace | `meta-cli` |
| Author, merge, validate, browse, or render documentation | `meta-docs` |
| Author or run a multi-workspace operation | `meta-mesh` |
| Author, inspect, emit, or execute correspondence | `meta-weave` |

## Schema, types, SQL, and transforms

| Need | Owning CLI |
| --- | --- |
| Discover an external schema | `meta-schema` |
| Author sanctioned data types | `meta-data-type` |
| Author or resolve type-conversion policy | `meta-data-type-conversion` |
| Extract, plan, deploy, or execute SQL artifacts | `meta-sql` |
| Import or emit modeled T-SQL transforms | `meta-transform-script` |
| Author reusable patterns and concrete pattern instances | `meta-transform-pattern` |
| Bind transforms to source and target schema contracts | `meta-transform-binding` |
| Discover, inspect, and promote Data Quality candidates | `meta-data-quality` |

## BI models and runtime

| Need | Owning CLI |
| --- | --- |
| Author Raw Data Vault | `meta-datavault-raw` |
| Author Business Data Vault | `meta-datavault-business` |
| Author dimensional warehouse | `meta-data-warehouse` |
| Author portable analytics | `meta-analytics` |
| Author or operate Tabular realization | `meta-tabular` |
| Author or operate multidimensional realization | `meta-multi-dimensional` |
| Author and execute one modeled pipeline | `meta-pipeline` |
| Create, inspect, plan, and execute cross-pipeline orchestration | `meta-orchestration` |
| Run sanctioned model-to-model conversions | `meta-convert` |

## Boundary reminders

- `meta-convert` hosts sanctioned conversion commands; it is not the authoring
  CLI for either endpoint model.
- `meta-weave` authors and executes correspondence workspaces; using a
  sanctioned conversion normally starts with `meta-convert`.
- `meta-pipeline` owns transform execution and task-level evidence.
  `meta-orchestration` owns dependencies, cross-pipeline planning, locks, and
  coordinated execution.
- `MetaSchemaAdapter` is a provider interface, not a competing discovery CLI.
  Provider integrations feed sanctioned MetaSchema discovery and binding
  workflows through the existing contracts.
