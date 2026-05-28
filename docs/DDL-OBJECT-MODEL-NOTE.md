# DDL object model note

`meta` currently emits SQL by assembling strings directly inside generators. That works for small cases, but it is the wrong foundation for a growing family of SQL-producing tools.

The immediate goal is modest:

- stop hand-building generic SQL in `GenerationService`
- introduce a small relational DDL/data model in `Meta.Core`
- keep vendor-specific rendering in one place

This is not a full SQL AST. It should only model what the current generators need:

- tables
- columns
- primary keys
- unique constraints
- foreign keys
- indexes
- insert statements

Why this is worth doing now:

- generic SQL generation in `meta` becomes easier to reason about
- domain generators in `meta-bi` get a foundation to build on later
- SQL rendering rules stop leaking into unrelated services

What this first pass does not try to solve:

- check constraints
- computed columns
- storage/filegroup options
- vendor-independent SQL semantics
- diff/migration planning

Those can be added later if the model genuinely needs them.

The design rule is simple:

- generators build DDL objects
- renderers turn DDL objects into SQL text
- domain code should not spend its time on `StringBuilder.AppendLine(...)`