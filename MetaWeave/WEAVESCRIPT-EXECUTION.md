# WeaveScript Execution

WeaveScript execution consumes query roots embedded in the typed
`MetaWeaveModel` loaded from any sanctioned Meta representation. The parser
AST, if an input surface uses one internally, is not an execution contract.

## Invocation

One execution invocation receives:

- one named direction identifying its source and target model contracts and
  its source requirements and target-entity transformations;
- one immutable source workspace snapshot;
- one workspace supplying the target model contract;

Every transformation reads the same source snapshot. Transformations cannot
read the target workspace or another transformation's result. The target
model is a DAG under its relationships, so transformations execute in the
topological order derived from that model: referenced entities before
referencing entities. Authoring or input-list order does not override this
order. A self-relationship does not add an entity-order edge; one entity
transformation is an atomic bulk population for that purpose.

Execution never delegates expressions or rowsets to SQL Server.

`meta-weave execute` creates a new target workspace. `--target-workspace`
supplies the target model contract; its instance rows are not copied. Exactly
one of `--xml <path>`, `--csharp <path>`, or `--sql <path>` selects the new
workspace representation and location. `forward` is the default direction. A
failed direction creates no output workspace.

## Direction scaffold

`MetaWeave` is the durable four-entity scaffold around the semantic queries:

- `Weave` identifies the two model contracts;
- `Direction` identifies one independent source-to-target orientation and
  belongs to the weave;
- `DirectionRequirement` assigns one embedded `SelectStatement`, diagnostic
  code, and message to a direction; every returned row is a source-domain
  violation;
- `Transformation` assigns one embedded `SelectStatement` to one target entity
  and belongs to a direction.

The scaffold contains no execution-order property. Loading it resolves its
embedded query relationships directly. Execution validates the declared model
names against the supplied source and target workspaces, then derives
transformation order from the target model DAG.

All direction requirements execute against the immutable source snapshot
before the first target transformation. Zero returned rows satisfies a
requirement. Every returned row produces one diagnostic carrying the modeled
code and message plus its projected evidence. Requirement evaluation never
mutates the source or target and is not a final target-validation pass.

`meta-weave` is the single authoring, emission, and execution boundary. It
accepts WeaveScript through standard input while authoring, but no SQL file or
standalone script workspace is sanctioned.

## Semantic traversal

An invocation constructs indexes over the model's entity and relationship
collections. Execution then follows modeled base entities, subtype entities,
links, and ordered items directly. The principal loops are:

- execute a `QueryExpression`;
- execute a `QuerySpecification`;
- execute a `TableReference`;
- evaluate a `ScalarExpression`;
- evaluate a `BooleanExpression`.

These are loops over the semantic model, analogous to the traversal used by
MetaTransformScript binding. They are not visitors over parser nodes.

Name resolution occurs inside the invocation. Source entities, CTEs, aliases,
members, functions, aggregates, and correlated outer scopes are resolved once
through invocation-owned indexes and frames. No independently stored bound
script, binder project, compiler IR, or execution plan is part of the product
contract.

## Runtime values

Runtime values are `NULL`, string, or language-owned integer. Source record
identities, properties, and relationship target identities enter execution as
strings. Integer literals and `COUNT` produce integers. Projection serializes
integers invariantly; it never parses workspace strings as numbers.

Strings use ordinal, case-insensitive equality and deterministic ordinal,
case-insensitive ordering. Identities use `MetaIdentity` validation, equality,
and ordering. Boolean evaluation uses SQL three-valued logic; only `TRUE`
passes `WHERE` and join predicates.

## Rowsets and scopes

A source-entity scan exposes `Id`, declared properties, and relationship column
names. Missing optional members are `NULL`. A row frame maps exposed table
names or aliases to those columns. An unqualified member must resolve uniquely
in the nearest scope; a qualified member resolves through its alias. Correlated
subqueries and APPLY evaluate with an explicit outer frame.

CTEs are registered in declaration order, evaluated on first use, and cached
for the invocation. A CTE may reference only earlier CTEs. Forward references,
cycles, and duplicate names are errors.

Derived query columns use their explicit column-alias list when present and
otherwise retain the query's projected names. Inline `VALUES` rowsets require a
complete column-alias list for executable use. `STRING_SPLIT` exposes `value`
and, when its literal third argument is `1`, `ordinal`; segments retain input
order.

## Relational behavior

The executor directly implements the complete retained surface:

- source, CTE, derived-query, `VALUES`, and `STRING_SPLIT` rowsets;
- inner, left, and cross joins plus cross and outer APPLY;
- filtering, projection, `DISTINCT`, grouping, and `UNION ALL`;
- `COUNT`, `MIN`, `MAX`, and ordered `STRING_AGG`;
- scalar and `EXISTS` subqueries, including correlation;
- retained predicates, `CASE`, `COALESCE`, `NULLIF`, `IIF`, and the closed
  scalar-function catalog.

`IS_BLANK` is a language-owned scalar returning integer `1` for `NULL`, empty,
or Unicode-whitespace-only strings and `0` otherwise. It does not inherit a
database collation or SQL Server trimming profile.

An aggregate query without `GROUP BY` has one implicit group. `COUNT` counts
rows or non-NULL arguments; the other aggregates ignore NULL inputs. A scalar
subquery returns `NULL` for no row, its value for one row, and fails for more
than one row or more than one column. `STRING_AGG` consumes its required
`WITHIN GROUP` order directly.

## Target materialization

Each transformation must project one unique `Id` column and may project only
members declared by its assigned target entity. `NULL` omits a member from the
proposed record.

Execution begins with an empty instance of the supplied target model. Each
projected row becomes a normal Meta `Operation.InsertRecord`. Meta Core
applies those operations to the current valid in-memory target and validates
after every insertion. Required members, identity validity and uniqueness,
relationship targets, and all other workspace invariants are therefore
enforced by Meta Core at the transformation that instantiates the row.

Optional self-relationships are completed inside the same transformation:
Meta Core first inserts all rows without those optional links and then applies
normal `Operation.SetRelationship` operations. The transformation becomes the
next observable target workspace only after both phases succeed. This avoids a
separate record-level dependency order while preserving a valid workspace at
every Core operation boundary.

Each successful transformation produces the next valid target workspace. A
transformation failure ends the invocation before publication. After every
scheduled transformation succeeds, the resulting state is created on the
selected XML, C#, or SQL provider.
