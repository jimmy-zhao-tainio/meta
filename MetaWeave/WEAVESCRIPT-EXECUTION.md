# WeaveScript Execution

WeaveScript execution consumes query roots embedded in the typed
`MetaWeaveModel` loaded from any sanctioned Meta representation. The parser
AST, if an input surface uses one internally, is not an execution contract.

## Invocation

One execution invocation receives:

- one named direction identifying its source workspace contracts, target
  model contract, requirements, named relations, transformations, and string
  parameters;
- one immutable snapshot for every named source workspace;
- one workspace supplying the target model contract;
- one value for every declared string parameter.

Every transformation reads the same set of source snapshots. Transformations
cannot read the target workspace or another transformation's result. The
target model is a DAG under its relationships, so transformations execute in
the topological order derived from that model: referenced entities before
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

`MetaWeave` is the durable seven-entity scaffold around the semantic queries:

- `Weave` identifies the correspondence;
- `Direction` identifies one independent many-source-to-one-target mapping and
  belongs to the weave;
- `DirectionSourceWorkspace` gives one source role its required model
  contract;
- `DirectionStringParameter` declares one named string input;
- `DirectionRequirement` assigns one embedded `SelectStatement`, diagnostic
  code, and message to a direction; every returned row is a source-domain
  violation;
- `DirectionRelation` gives one embedded `SelectStatement` a direction-scoped
  name so its rowset can be reused by requirements, transformations, and other
  relations;
- `Transformation` assigns one embedded `SelectStatement` to one target entity
  and belongs to a direction.

The scaffold contains no execution-order property. Loading it resolves its
embedded query relationships directly. Execution validates every named source
and the target against their declared model contracts, checks the declared
parameter values, then derives transformation order from the target model DAG.

All direction requirements execute against the immutable source snapshots
before the first target transformation. Zero returned rows satisfies a
requirement. Every returned row produces one diagnostic carrying the modeled
code and message plus its projected evidence. Requirement evaluation never
mutates the source or target and is not a final target-validation pass.

Named relations are also read-only queries over those source snapshots and
declared parameters. They cannot read the target. A relation is evaluated on
first reference and its rowset is cached for the rest of the direction, so a
requirement and several transformations observe the same value. Relations may
reference other relations; dependency cycles are execution errors. Every
relation must produce named, case-insensitively unique columns.

Requirements retain precedence over construction work. Relations referenced
by a requirement are evaluated as needed; if any requirement returns a
violation, execution stops there. Once requirements pass, all remaining
relations are evaluated before target construction, ensuring invalid unused
definitions are reported without allowing them to obscure source-domain
diagnostics.

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

Name resolution occurs inside the invocation. Source roles, source entities,
named relations, CTEs, aliases, members, parameters, functions, aggregates,
and correlated outer scopes are resolved through invocation-owned indexes and
frames. A local CTE shadows a direction relation; a direction relation shadows
an unqualified source-entity name. Qualified `sourceRole.Entity` references
remain explicit. No independently stored bound script, binder project,
compiler IR, or execution plan is part of the product contract.

## Runtime values

Runtime values are `NULL`, string, or language-owned integer. Source record
identities, properties, and relationship target identities enter execution as
strings. Declared parameters enter as strings. Integer literals and `COUNT`
produce integers. Projection serializes integers invariantly; it never parses
workspace strings as numbers.

Strings use ordinal, case-insensitive equality and deterministic ordinal,
case-insensitive ordering. Identities use `MetaIdentity` validation, equality,
and ordering. Boolean evaluation uses SQL three-valued logic; only `TRUE`
passes `WHERE` and join predicates.

## Rowsets and scopes

A source-entity scan exposes `Id`, declared properties, and relationship column
names. `role.Entity` selects an entity from a named source workspace; an
unqualified entity name is accepted when it occurs in exactly one source.
Missing optional members are `NULL`. A row frame maps exposed table names or
aliases to those columns. An unqualified member must resolve uniquely in the
nearest scope; a qualified member resolves through its alias. Correlated
subqueries and APPLY evaluate with an explicit outer frame.

CTEs are local to one query. They are registered in declaration order,
evaluated on first use, and cached for that query session. A non-recursive CTE
may reference only earlier CTEs. A recursive CTE has an anchor followed by one
`UNION ALL` member whose single self-reference exposes only the preceding
iteration. Its rows are accumulated until an iteration is empty. Forward and
mutual references, recursive anchors, multiple self-references, and duplicate
names are errors.

Derived query columns use their explicit column-alias list when present and
otherwise retain the query's projected names. Inline `VALUES` rowsets require a
complete column-alias list for executable use. `STRING_SPLIT` exposes `value`
and, when its literal third argument is `1`, `ordinal`; segments retain input
order.

## Relational behavior

The executor directly implements the complete retained surface:

- source, non-recursive and recursive CTE, derived-query, `VALUES`, and
  `STRING_SPLIT` rowsets;
- inner, left, and cross joins plus cross and outer APPLY;
- filtering, projection, `DISTINCT`, grouping, and `UNION ALL`;
- `COUNT`, `MIN`, `MAX`, ordered `STRING_AGG`, and partitioned `ROW_NUMBER`;
- scalar and `EXISTS` subqueries, including correlation;
- retained predicates, `CASE`, `COALESCE`, `NULLIF`, `IIF`, and the closed
  scalar-function catalog.

`LEN` is a language-owned scalar returning the string length after excluding
trailing U+0020 spaces; `NULL` propagates. `IS_BLANK` returns integer `1` for
`NULL`, empty, or Unicode-whitespace-only strings and `0` otherwise. Neither
function inherits a database collation or SQL Server trimming profile.

The retained `TRY_CONVERT(int, value)` form returns a language integer for a
convertible string or integer and `NULL` otherwise. `ROW_NUMBER` requires an
`OVER` clause with `ORDER BY`, accepts optional `PARTITION BY`, and produces a
one-based language integer. No other SQL data type or window function is part
of the executable surface.

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

A caller may request immutable outputs for every named relation on the
successful application result, keyed case-insensitively by relation name.
Capture is opt-in so ordinary execution does not materialize result evidence
it will not use. Consumers such as conversion reports can request that evidence
without repeating the weave's selection or naming decisions in host code.
