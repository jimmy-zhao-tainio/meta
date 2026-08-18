# WeaveScript Surface

WeaveScript is the conversion-query language for Meta workspaces. It is a
deliberate distillation of `MetaTransformScript`: familiar T-SQL query syntax
is retained where it has deterministic workspace-conversion meaning, while SQL
Server deployment, storage, server-session, and external-access machinery is
removed.

`MetaWeaveScript.Sql` and `MetaWeaveScript.Execution` are implementation
package identities; WeaveScript is the language name used in prose. Its
semantic entities live inside the `MetaWeave` model rather than forming a
second sanctioned workspace.

The product boundary is one `MetaWeave` workspace and one `meta-weave` CLI.
A workspace contains the weave, its independent directions, their named source
workspace contracts, target contract, string parameters, source-domain
requirements, reusable direction relations, target-entity transformations,
and every embedded modeled query graph. Requirement, relation, and
transformation authoring reads recognizable T-SQL from standard input and
stores only the semantic query; the corresponding `emit-*` and `update-*`
commands inspect and replace it. An update is parsed before the workspace is
changed and removes the superseded semantic query graph. No raw script path is
part of the durable correspondence.

This document defines the complete intended language boundary, not an initial
implementation slice. Every currently sanctioned `MetaTransformScript` syntax
family is accounted for below.

MetaWeaveScript begins as a subtractive fork of `MetaTransformScript`. The two
implementations retain recognizable correspondence where that remains useful,
but are independently owned and may diverge as their domains require.
MetaWeaveScript does not require a shared source tree, runtime, or common-core
extraction.

The boundary is chosen syntax-first. A construct is retained when a concrete
workspace-conversion use case justifies it. Implementation cost, SQL Server
provenance, or lack of support in today's generic workspace API is not by
itself an exclusion argument. The required model entities are then obtained by
closing the retained syntax over its base, link, and ordered-item entities.

A use case is necessary but not sufficient. Every retained construct must also
be executable by MetaWeave without delegating to SQL Server: either it is one
of the execution primitives below or it has a defined lowering into those
primitives.

Legend:

- [x] retained in WeaveScript
- [ ] deliberately excluded from WeaveScript

## Conversion shape

A weave direction identifies one or more named source workspace contracts and
one target model contract. It contains named requirements, relations, and
transformations. Each requirement assigns one bare-`SELECT` whose returned
rows are violations. Each relation assigns a reusable direction-scoped name to
one bare-`SELECT`. Each transformation assigns one bare-`SELECT` document to
one target entity; those assignments are supplied by the surrounding weave
definition rather than encoded as `CREATE VIEW`.

Within a query:

- `sourceRole.Entity` denotes an entity population in a named source
  workspace;
- a direction-relation name denotes its source-derived rowset;
- an unqualified entity name denotes the unique matching population across all
  source workspaces;
- `@name` denotes a declared invocation string parameter;
- `alias.Id` denotes record identity;
- `alias.PropertyName` denotes a property value;
- `alias.RoleId` denotes a relationship target identity;
- a projected `Id` supplies target record identity;
- other projected names bind to target properties or relationship-id fields;
- `NULL` projected for a member means absence from the proposed record;
- normal Meta Core operations validate required members, relationships,
  identities, duplicates, and references as each record is instantiated.

The language is query-shaped, but it does not delegate execution to SQL Server.
Retained constructs therefore require WeaveScript-owned, deterministic
semantics.

Direction relations are composition, not a second language feature. They use
the same semantic query model as requirements and transformations, may depend
on other direction relations, and are evaluated once per direction execution.
They have no parameters of their own, cannot read the target workspace, and do
not introduce function declaration or invocation syntax.

All target-entity transformations in one direction read the same immutable set
of source workspace snapshots. A transformation cannot read a target population
or observe another transformation's result. Transformations instantiate the
valid target workspace in the topological order derived from the target model
DAG, with referenced entities before referencing entities. Each insertion is
a normal validated Meta Core operation. An entity population is atomic for
optional self-references, which are set through Meta Core after all of that
entity's rows exist. The state produced by the last scheduled transformation
is the completed target workspace.

## Value and evaluation rules

The retained syntax operates on the values Meta workspaces expose:

- record identities are strings governed by the bound contract's identity
  equality and ordering;
- present property values and relationship target identities are strings;
- an absent optional member is `NULL`;
- boolean expressions use SQL-style three-valued logic;
- string equality and ordering use one versioned WeaveScript comparison
  profile, never ambient database collation;
- no property string is implicitly interpreted as a number, date, time, or SQL
  data type; the retained `TRY_CONVERT(int, value)` form performs an explicit
  conversion;
- counts and row numbers are language-owned
  integers and are serialized invariantly when projected into a target member;
- all accepted scalar and table functions have closed WeaveScript semantics.

Unordered source enumeration is never observable conversion truth. Ordering is
available only where a retained construct, such as `STRING_AGG ... WITHIN
GROUP` or `ROW_NUMBER() OVER (...)`, consumes it directly.

## Executable core

MetaWeave directly executes this closed relational core over an immutable
workspace snapshot:

- scan a bound source entity;
- construct literal `VALUES` rows;
- evaluate a non-recursive or recursive CTE;
- filter rows;
- project expressions;
- remove duplicate rows;
- perform inner, left, and cross joins;
- evaluate a lateral rowset, scalar subquery, or correlated `EXISTS` subquery
  for one input row;
- group rows by expressions and evaluate retained aggregates;
- append compatible rowsets with `UNION ALL`;
- expand `STRING_SPLIT`;
- evaluate `NULL`, boolean logic, `CASE`, retained scalar functions, and
  comparisons.

The executor implements these operators through binder-shaped loops over the
typed MetaWeave semantic model. Parsing ends when that model has been
populated. Invocation-scoped navigation indexes, scopes, rowsets, and aggregate
state support execution directly; there is no parser-AST interpreter,
standalone binder, persisted bound-script artifact, or SQL execution plan.

`COALESCE`, `NULLIF`, and `IIF` are retained surface syntax only because they
have deterministic lowerings into `CASE`. Unsupported syntax is rejected;
there is no SQL execution fallback.

## Documents, wrappers, and statements

- [x] One bare `SELECT` statement per named target-entity document
- [x] Optional terminating semicolon
- [x] SQL comments
- [ ] `CREATE VIEW`
- [ ] View column lists and view options
- [ ] `CREATE FUNCTION`, including inline table-valued and scalar functions
- [ ] Stored-procedure definitions and stored-procedure contracts
- [ ] `INSERT`
- [ ] `UPDATE`
- [ ] `DELETE`
- [ ] `TRUNCATE`
- [ ] `MERGE`
- [ ] `SET` auxiliary batches
- [ ] `GO` batch separators
- [ ] Mutation `OUTPUT` clauses

One query is sufficient to construct one complete target entity population.
Mutation statements would introduce arbitrary in-script state changes and are
not part of workspace conversion; target instantiation remains an external,
ordered sequence of Meta Core operations.

## Query structure

- [x] `SELECT`
- [x] `SELECT` without `FROM`
- [x] `DISTINCT`
- [ ] Aggregate-level `DISTINCT`
- [ ] `TOP`
- [ ] `TOP ... PERCENT`
- [ ] `TOP ... WITH TIES`
- [ ] Query-level `ORDER BY`
- [ ] `OFFSET`
- [ ] `FETCH`
- [x] Query parentheses
- [ ] `UNION`
- [x] `UNION ALL`
- [ ] `INTERSECT`
- [ ] `EXCEPT`

Ordering and row-limiting constructs use WeaveScript's deterministic value and
identity ordering. They do not inherit database collation or an optimizer's
unspecified row order.

## Common table expressions

- [x] `WITH`
- [x] Non-recursive common table expressions
- [x] Recursive common table expressions with an anchor and one `UNION ALL`
  recursive member
- [ ] CTE column lists
- [x] Multiple CTEs in dependency order

CTEs are part of the full surface. They provide named intermediate rowsets and
composition without adding procedural statements. A recursive member sees only
the rows produced by the preceding iteration. Evaluation stops when an
iteration produces no rows; a member that immediately reproduces its input is
rejected, and other non-terminating forms fail after 32,767 iterations. Mutual
recursion, recursive anchors, and multiple self-references are not retained.
The concrete MetaWeave use is walking an ordered modeled syntax graph before
aggregating its rendered tokens into target text.

## Rowset sources

- [x] Named source entities
- [x] Source aliases
- [x] CTE references
- [x] Derived tables
- [x] Inline `VALUES` tables
- [x] Parenthesized rowset expressions
- [ ] Generic registered table-valued functions
- [ ] `GENERATE_SERIES`
- [x] `STRING_SPLIT`
- [x] Aliases on retained table-valued functions
- [ ] Column-alias lists on retained table-valued functions
- [ ] Arbitrary schema-object or global table-valued functions
- [ ] `OPENJSON`
- [ ] `OPENROWSET`
- [ ] `OPENQUERY`
- [ ] Ad hoc data sources
- [ ] `CHANGETABLE`
- [ ] XML `nodes(...)` rowsets
- [ ] `TABLESAMPLE`

`STRING_SPLIT` is retained for the concrete migration case of decomposing a
legacy packed property into records. A generic table-function extension point
is not retained; a function name is never an escape hatch to ambient database
code.

## Joins and lateral sources

- [x] `INNER JOIN`
- [x] `LEFT OUTER JOIN`
- [ ] `RIGHT OUTER JOIN`
- [ ] `FULL OUTER JOIN`
- [x] `CROSS JOIN`
- [x] `CROSS APPLY`
- [x] `OUTER APPLY`
- [ ] Join-parenthesized table references

## Projection

- [x] Scalar projections
- [x] Projection aliases
- [ ] Assignment-form aliases
- [ ] `SELECT *`
- [ ] `SELECT alias.*`
- [ ] SQL Server single-quoted aliases

The bound target contract, rather than SQL metadata, determines whether an
output field is identity, a property, or a relationship.

## Predicates

- [x] `=`, `<>`, `<`, `<=`, `>`, and `>=`
- [ ] `!=` as an additional not-equal spelling
- [ ] `BETWEEN` and `NOT BETWEEN`
- [x] `IN (...)`
- [ ] `NOT IN (...)` as a dedicated spelling
- [ ] `IN (subquery)` and `NOT IN (subquery)`
- [x] `LIKE`
- [ ] `NOT LIKE` as a dedicated spelling
- [ ] `LIKE ... ESCAPE`
- [x] `IS NULL` and `IS NOT NULL`
- [ ] `IS DISTINCT FROM`
- [x] `EXISTS`
- [ ] `ALL` and `ANY` subquery comparisons
- [x] Boolean `AND`, `OR`, and `NOT`
- [x] Boolean parentheses
- [ ] `CONTAINS`
- [ ] `FREETEXT`
- [ ] ODBC-escape predicates

Full-text search is excluded. Tokenization, stemming, stoplists, language,
ranking, and index configuration make it an unsuitable basis for deciding
which records a conversion preserves.

## Grouping and aggregation

- [x] `GROUP BY`
- [ ] `HAVING`
- [ ] `GROUP BY ALL`
- [ ] `GROUPING SETS`
- [ ] `ROLLUP`
- [ ] `CUBE`
- [ ] Grand-total grouping
- [ ] Composite grouping specifications
- [x] Expression grouping specifications
- [ ] `AVG`
- [x] `COUNT`
- [ ] `COUNT_BIG`
- [ ] `SUM`
- [x] `MIN`
- [x] `MAX`
- [x] `STRING_AGG`, requiring `WITHIN GROUP`
- [ ] `STDEV`
- [ ] `STDEVP`
- [ ] `VAR`
- [ ] `VARP`
- [ ] `GROUPING`
- [ ] `GROUPING_ID`
- [ ] `CHECKSUM_AGG`
- [ ] `APPROX_COUNT_DISTINCT`
- [ ] Distributed-aggregation grouping specifications

Retained aggregates have structural conversion uses: counting records,
selecting a deterministic minimum or maximum, and collecting strings. Numeric
and statistical aggregates are excluded because WeaveScript has no typed
numeric property contract and does not own analytical processing.

## Windows and analytic functions

- [x] `OVER (...)` for `ROW_NUMBER`
- [ ] Named `WINDOW` clauses
- [x] `PARTITION BY` for `ROW_NUMBER`
- [x] Window `ORDER BY` for `ROW_NUMBER`
- [ ] `ROWS` frames
- [ ] `RANGE` frames
- [ ] Numeric frame delimiters
- [x] `ROW_NUMBER`
- [ ] `RANK`
- [ ] `DENSE_RANK`
- [ ] `NTILE`
- [ ] `LEAD`
- [ ] `LAG`
- [ ] `FIRST_VALUE`
- [ ] `LAST_VALUE`
- [ ] `PERCENT_RANK`
- [ ] `CUME_DIST`
- [ ] `PERCENTILE_CONT`
- [ ] `PERCENTILE_DISC`

## Scalar expressions

- [x] Column references
- [x] Declared string parameter references such as `@databaseName`
- [x] Parenthesized expressions
- [x] Scalar subqueries
- [ ] Unary `+` and `-`
- [ ] Binary arithmetic `+`, `-`, `*`, `/`, and `%`
- [x] Searched `CASE`
- [ ] Simple `CASE`
- [x] `COALESCE`
- [x] `NULLIF`
- [x] `IIF`
- [ ] `CHOOSE`
- [x] `LEFT` and `RIGHT` through the generic function-call shape
- [x] WeaveScript-owned scalar function calls
- [ ] Arbitrary or server-resolved function calls
- [ ] Function-call targets for XML methods
- [ ] `WITH ARRAY WRAPPER`
- [ ] `CAST`
- [ ] `TRY_CAST`
- [ ] `CONVERT`
- [x] `TRY_CONVERT(int, value)`
- [ ] `PARSE`
- [ ] `TRY_PARSE`
- [ ] `COLLATE`
- [ ] `AT TIME ZONE`
- [ ] `EXTRACT`
- [ ] `CURRENT_TIMESTAMP`
- [ ] `NEXT VALUE FOR`
- [ ] Global variables such as `@@SPID`
- [ ] ODBC scalar-function escapes

The complete scalar-function catalog is `CONCAT`, `LOWER`, `UPPER`, `TRIM`,
`LTRIM`, `RTRIM`, `LEN`, `REPLACE`, `SUBSTRING`, `LEFT`, `RIGHT`, and
`IS_BLANK`.
Names, arity, indexing, null propagation, and string behavior are
WeaveScript-owned and versioned. `LEN` returns the string length after
excluding trailing U+0020 spaces and propagates `NULL`. `IS_BLANK` returns
integer `1` for `NULL`, empty, or Unicode-whitespace-only strings and `0`
otherwise; it captures conversion contracts that distinguish blank from
merely space-trimmed text.
Unlisted names fail validation; they are not passed through to a database
engine. WeaveScript does not include function declarations or server-resolved
functions. `LEFT` and `RIGHT` use the generic function-call shape rather than
MetaTransformScript's dedicated entities.

A scalar subquery projects exactly one column. No row produces `NULL`, one row
produces that row's value, and more than one row is an evaluation error. A
scalar subquery may be correlated to the row being evaluated.

## Literals

- [x] Integer literals
- [ ] Numeric/decimal literals
- [ ] Real/scientific literals
- [ ] Signed numeric literal forms
- [x] String literals
- [ ] National-string `N'...'` spelling
- [ ] Binary literals
- [x] `NULL`
- [ ] `MAX` as a data-type parameter

String and integer literal spelling is preserved through parse and emit.

## Pivoting

- [ ] `PIVOT`
- [ ] `UNPIVOT`

## XML, full text, and server facilities

- [ ] `WITH XMLNAMESPACES`
- [ ] Default XML namespaces
- [ ] XML `.query(path)`, `.exist(path)`, and `.value(path)` methods
- [ ] XML `nodes(...)`
- [ ] `CONTAINSTABLE`
- [ ] `FREETEXTTABLE`
- [ ] SQL Server table hints
- [ ] SQL Server query hints
- [ ] Join hints
- [ ] Sequence access
- [ ] Session or server globals

Using XML to reshape application data is not by itself a Meta-to-Meta
conversion use case. No required correspondence currently needs to interpret
an opaque XML-valued property, so WeaveScript does not acquire an XML/XQuery
subsystem speculatively. XML and JSON rowsets, full-text search, date/time
expressions, sampling, sequences, collation overrides, hints, and
session/server globals remain excluded: none currently has both a required
workspace-conversion use and coherent Meta value semantics.

## Names and qualification

- [x] Unquoted identifiers
- [x] Bracket-quoted identifiers
- [ ] Double-quoted identifiers
- [x] One-part source entity names
- [x] Two-part source-workspace/entity names
- [x] Two-part alias/member references
- [ ] Backtick-quoted identifiers
- [ ] Three-part database/schema/object names
- [ ] Four-part server/database/schema/object names
- [ ] Cross-database names

The surrounding direction binds named source workspace contracts and one target
workspace contract. The first part of `sourceRole.Entity` is a workspace role,
not a SQL schema. Database and server qualification has no WeaveScript meaning.

## Data types

- [x] The `int` SQL data-type reference used by retained `TRY_CONVERT`
- [ ] Parameterized SQL data types
- [ ] Other integer and logical types: `bigint`, `smallint`, `tinyint`, and `bit`
- [ ] `decimal`, `numeric`, `money`, `smallmoney`, `float`, and `real`
- [ ] `date`, `time`, `datetime`, `datetime2`, and `datetimeoffset`
- [ ] `char`, `varchar`, `nchar`, and `nvarchar`
- [ ] `binary` and `varbinary`
- [ ] `uniqueidentifier`
- [ ] `sql_variant`
- [ ] `xml`
- [ ] `geography`, `geometry`, and `hierarchyid`
- [ ] SQL Server `timestamp` and `rowversion` aliases

Workspace properties are values governed by the source and target model
contracts. The single `int` reference exists to express explicit ordinal
conversion; WeaveScript does not reproduce SQL Server's declaration and
conversion type system.

## Extraction rule

The syntax checklist above is the design artifact. The WeaveScript model is
derived from it by closing the retained syntax families over their structural
base entities, links, and ordered-item entities. An entity is retained when it
is needed to represent any checked syntax. It is excluded only when all syntax
that requires it is unchecked.

This is not the same as retaining everything reachable from
MetaTransformScript's `ParseQueryExpression`: parser reachability is an
implementation fact, while the checklist records why the syntax belongs.

Applying the current checklist to the 346-entity MetaTransformScript model
produces the materialized closure in the C# workspace
`Workspace/MetaWeave.meta.cs`: 134 retained WeaveScript entity types plus the
seven MetaWeave scaffold entities, with 212 excluded entity types. A structural
check finds no retained relationship
whose target entity is excluded.

Several retained entity types must also lose fields that belong only to
excluded syntax:

- `BinaryQueryExpression.All`
- `BinaryQueryExpression.BinaryQueryExpressionType`
- `StringLiteral.IsLargeObject`
- `ExpressionGroupingSpecification.DistributedAggregation`
- `FunctionCall.UniqueRowFilter`
- `FunctionCall.WithArrayWrapper`
- `GroupByClause.All`
- `GroupByClause.GroupByOption`
- `IdentifierOrValueExpression.Value`
- `InPredicate.NotDefined`
- `IntegerLiteral.LiteralType`
- `LikePredicate.NotDefined`
- `LikePredicate.OdbcEscape`
- `Literal.LiteralType`
- `MultiPartIdentifier.Count`
- `NullLiteral.LiteralType`
- `QualifiedJoin.JoinHint`
- `StringLiteral.IsNational`
- `StringLiteral.LiteralType`
- `TableReferenceWithAlias.ForPath`

Enumerated properties shared by retained and excluded syntax are narrowed by
WeaveScript validation rather than copied as open-ended strings.

The lexer, parser shapes, model names, ordering representation, and
parse/emit/parse test cases should remain recognizably derived from
MetaTransformScript. Excluded syntax must fail with a specific WeaveScript
diagnostic rather than being accepted as opaque SQL.
