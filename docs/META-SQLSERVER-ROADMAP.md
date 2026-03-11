# MetaSqlServer roadmap

`meta-sqlserver` is the planned SQL Server deployment surface for the `meta` family.

It is not meant to replace domain generators. Domain tools such as `meta-datavault` or future `meta-mart` should keep owning domain semantics and SQL generation. `meta-sqlserver` should own deployment of SQL Server artifacts and, later, SQL Server-specific migration behavior.

## Intended role

- deploy generated SQL Server DDL to a target database
- provide one SQL Server deployment story across the `meta` family
- keep SQL Server deployment concerns out of domain CLIs
- later own diff-based migration planning and execution for existing databases with data

## What it should not do

- become the source of truth for domain semantics
- replace sanctioned workspaces
- force SSDT or Visual Studio into the workflow
- hide destructive schema changes behind weak defaults

## Phase 1: deploy generated SQL [implemented baseline]

Goal:
- take an ordered SQL artifact set and apply it to SQL Server deterministically

Scope:
- connection handling
- deterministic script ordering
- fail-fast execution
- clean console output
- optional deployment journal table later

Likely command surface:
- `meta-sqlserver deploy --scripts <path> --connection-string <value>`
- or later under `meta deploy sqlserver ...`

## Phase 2: introduce a shared deployment artifact

Goal:
- stop treating deployment as a loose folder of scripts

Artifact should carry:
- ordered scripts
- manifest
- generator identity
- generated-at metadata
- optional target compatibility metadata

This keeps deployment reproducible and inspectable.

## Phase 3: diff-based schema migration

Goal:
- support existing databases with data, not just fresh deploys

This is the hard part.

The deployer must eventually distinguish:
- additive safe change
- additive risky change
- destructive change
- rename/refactor requiring explicit mapping
- data-preserving transformation requiring migration steps

Important constraint:
- `meta-sqlserver` should never guess through destructive changes just because SQL Server accepts DDL. If the migration path is ambiguous, it must fail and require explicit metadata.

## Phase 4: shared DDL model in foundation

Goal:
- stop hand-assembling SQL through raw string concatenation in generators

Status:
- started in `Meta.Core` with a minimal DDL object model and SQL Server renderer

Next likely additions:
- indexes
- check constraints
- default constraints
- schema-level objects where justified

## Phase 5: journal and drift control

Goal:
- make repeated deploys safe and inspectable

Possible features:
- deployment journal
- applied artifact manifest tracking
- hash/fingerprint comparison
- drift detection against target database

## Phase 6: advanced SQL Server realization

Future SQL Server-specific concerns can live here without polluting domain generators:
- transactional deployment strategies
- online/offline choices
- pre/post deployment hooks
- optional DACPAC or SSDT compatibility output if needed for old-hat teams

These are secondary to first-class deployment, not the primary architecture.

## Current decision

Do not start with SSDT.

Primary path:
- sanctioned workspace -> domain SQL generation -> `meta-sqlserver` deploy

Secondary future path:
- sanctioned workspace -> product-specific projection such as `MetaSSDT`

That keeps deployment semantics in the `meta` stack instead of outsourcing them to Visual Studio conventions.