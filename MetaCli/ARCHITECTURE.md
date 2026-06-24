# MetaCli Architecture Ledger

## Purpose

MetaCli is the modeled command-surface foundation for the Meta toolchain.

The durable source of truth is the `MetaCli` model workspace:

- `MetaCli/Workspace/model.xml`
- generated tooling under `MetaCli/Tooling`
- authored MetaCli workspaces created by `meta-cli`

This file records architecture decisions that should survive chat context loss.
It is not public product copy.

## Current Slice

The current implemented slice is model, authoring, and a first runtime core:

- `MetaCli` model exists.
- generated C# tooling exists.
- `meta-cli` can create and mutate MetaCli workspaces.
- external demo proof exists at `Samples/Demos/MetaCliAuthoringDemo/demo-meta-cli.ps1`.
- `MetaCli.Core` contains a first model-backed runtime parser/dispatcher.

The immediate focus is the runtime and real CLI adoption of it.

## Accepted Model Direction

MetaCli is model-first.

Each CLI surface gets its own authored MetaCli workspace. There is no
suite-level MetaCli workspace in the current architecture. Discovery, docs, or
catalog composition can be solved later by reading multiple CLI workspaces; do
not introduce a grand all-CLIs workspace as part of the runtime slice.

The model must move slowly. Every structural model change can require touching
many CLI workspaces, so model churn is expensive by design.

## CLI Workspace Policy

Each CLI has one checked-in authored MetaCli workspace. The workspace is the
source of truth for that CLI's command surface.

Standard workspace path:

`<CliProjectDirectory>/<executable-name>.MetaCli`

Examples:

- `MetaCli/Cli/meta-cli.MetaCli`
- `MetaDocs/Cli/meta-docs.MetaCli`
- `Meta/Cli/meta.MetaCli`
- `MetaSchema/Cli/meta-schema.MetaCli`

The executable name is lower-kebab text, without `.exe`. Use the real command
name the user types.

Authoring policy:

- Use `meta-cli` to author workspaces.
- Do not hand-edit workspace XML except for deliberate forensic repair.
- Script initial creation and substantial migrations.
- Manual `meta-cli` edits are fine for tiny changes.
- Review and commit the resulting workspace diff; do not treat a bootstrap
  script as the long-term source of truth.
- Do not regenerate a CLI workspace from C# help/prose or a parallel definition
  file.

Bootstrap script policy:

- Start from `MetaCli/Templates/bootstrap-cli-workspace.ps1`.
- Copy it next to the CLI project, set the CLI name, and add command authoring
  calls in the marked section.
- The script should assume `meta-cli.exe` is on `PATH` and stay easy to read as
  a sequence of ordinary authoring commands.
- Keep the script if it remains useful during migration; otherwise the
  persisted `.MetaCli` workspace is the durable artifact.

ID naming convention:

- Application: `app-<executable-name>`
- Command: `cmd-<route>`
- Executable command: `exec-<route>`
- Parameter: `param-<route>-<name>`
- Option: `option-<route>-<name>`
- Option token: `token-<route>-<name>`
- Positional argument: `pos-<route>-<name>`
- Parameter group: `group-<route>-<name>`

## Core Invariants

- Do not add compatibility aliases to keep old command surfaces alive.
- Do not model command kind, command group, command segment, or similar
  discriminator strings.
- Do not add `Ordinal`, `Position`, `Order`, or leading-zero sort hacks.
- Ordered CLI collections use relationships such as `PreviousToken`,
  `PreviousArgument`, `PreviousValue`, and `PreviousMember`.
- Command route structure is modeled by `Command.ParentCommand`.
- Runnable commands are modeled by `ExecutableCommand`.
- Non-runnable grouping commands are plain `Command` rows without
  `ExecutableCommand` rows.
- Root/default invocation is modeled by `ApplicationDefaultCommand`.
- `Parameter` is the shared contract identity for options and positionals.
- `Option` and `PositionalArgument` are invocation forms over `Parameter`.
- `ParameterGroup` groups parameters, not only options, so surfaces such as
  `<Id> | --auto-id` do not require parser exceptions.
- `ValueShape` is required for parseable parameters.
- `ValueArity` and `AllowedValue` are enough until concrete parser behavior
  proves otherwise.
- Do not add `ValueCodec` without a concrete parser behavior that cannot be
  modeled by arity, allowed values, or command-specific handling.
- `ExitCode` belongs to `Application`, with optional `ExecutableCommand`
  refinement.
- Output contracts are modeled with `Output`, `OutputFormat`, and
  `OutputStream`.

## Authoring Rule

Successful public mutating commands must never persist a workspace that fails
the modeled representation-integrity contract.

The integrity question is not whether the XML looks tidy. XML is one working
surface and may be mechanically jumbled. The first judge is relationship /
referential integrity in the modeled instance graph. The deeper judge is
isomorphism: the same represented structure must survive projection into
generated C#, SQL, and other sanctioned surfaces without semantic drift.

This means public command shapes create complete semantic aggregates when the
model requires multiple rows:

- `add-option` creates `Parameter`, `Option`, and the primary `OptionToken`.
- `add-option-token` only adds alias tokens to an already valid option.
- `add-positional` creates `Parameter` and `PositionalArgument`.
- `add-parameter-group` creates `ParameterGroup` and its first
  `ParameterGroupMember`.
- `add-parameter-group-member` only adds additional members.
- `set-default-command` creates the tokenless default `Command`, its
  `ExecutableCommand`, and the `ApplicationDefaultCommand`.

If a public mutation cannot preserve the modeled RI/isomorphism contract, the
command shape is wrong.

A standalone public `check` command is therefore not part of the MetaCli
surface. We do not need a SQL command to verify that foreign keys are foreign
keying, and we do not need a C# method to verify that object references are not
random memory addresses. Likewise, MetaCli should not expose a user command
whose job is to compensate for XML being permissive.

What we do need is provider proof: the modeling software must prove through
load, bind, convert, generate, and round-trip behavior that it preserves the
modeled RI/isomorphism contract.

## Current Public Command Surface

Workspace:

- `meta-cli --new-workspace <path>`
- `meta-cli show [--workspace <path>]`

Authoring:

- `add-application`
- `add-command`
- `add-executable-command`
- `set-default-command`
- `add-value-arity`
- `add-value-shape`
- `add-allowed-value`
- `add-option`
- `add-option-token`
- `add-positional`
- `add-parameter-group`
- `add-parameter-group-member`
- `add-duplicate-option-behavior`
- `add-unknown-token-behavior`
- `add-parser-policy`
- `add-output-format`
- `add-output-stream`
- `add-output`
- `add-exit-code`

All normal commands accept optional `--workspace <path>` and default to the
current working directory when omitted.

## Console Output Rule

MetaCli output should speak in terms of user actions and command surfaces, not
internal row mechanics.

- Successful mutating commands should say what changed in one concise line.
- `show` should summarize the authored command surface without dumping entity
  rows.
- Normal output should avoid table layouts unless the data is genuinely tabular
  and remains readable at ordinary console widths.
- Failure output should identify the failed command and point to the relevant
  help, without implying that the executable itself is broken.
- Entity names are acceptable when authoring the model requires them, but they
  should not be the default narrative voice of the CLI.

## Provider Integrity Contract

Internal provider integrity logic currently covers:

- duplicate row ids
- command parent references and parent cycles
- default command shape
- tokenless command restrictions
- value arity cardinality text, including no leading-zero numeric padding
- value-shape references
- parameter value-shape completeness
- option primary-token completeness
- option-token chain integrity
- positional-argument chain integrity
- allowed-value chain integrity
- parameter-group member chain integrity
- required group member completeness
- parser-policy cardinality and references
- boolean text fields
- behavior/output format/output stream name uniqueness
- output executable/format/stream references
- output name uniqueness within one executable command
- exit-code application/command scoping and uniqueness

## Rejected Paths

These are intentionally rejected unless reopened explicitly:

- reviving `CliAppDefinition` as the source of truth for MetaCli
- adding a projection/factory layer from `CliAppDefinition` into MetaCli
- adding a MetaDocs-specific projection layer inside MetaCli
- generating CLI-specific code inside the generic `meta` tooling generator
- restoring command aliases for backwards compatibility
- treating CLI help prose as the model
- modeling command categories with a vague `Kind` property
- ordering command parts through scalar ordinals or padded text
- persisting staged-invalid workspaces through public authoring commands

Pre-existing `CliAppDefinition` infrastructure remains legacy surface area in
other CLIs and MetaDocs import paths. It is not the MetaCli architecture.

## Runtime Direction

The runtime/parser consumes MetaCli model instances directly.

The first implemented shape is:

- use generated MetaCli tooling classes as the parser contract model
- bind command handlers through an explicit registry keyed by
  `ExecutableCommand.Id`
- pass handlers `MetaCliInvocation`, not raw `string[] args`
- keep parsing, binding, provider integrity proof, and presentation separable
- keep CLI output in CLI/presenter code, not service code

The first runtime slice supports command-tree dispatch, default commands,
options, primary/alias option tokens, `--option=value` when enabled by
`ParserPolicy`, positionals, required parameters, parameter groups,
allowed values, and zero/one value arity. It does not migrate any public CLI
onto the runtime yet.

Integrity evidence belongs to the modeled operation, load/bind/generate
pipeline, or round-trip proof, not to a standalone public `check` command.

## Handler Binding Options

The model must not know about implementation delegates. Handler association is
runtime wiring over model instances, not model data.

Reasonable options:

- Explicit registry keyed by `ExecutableCommand.Id`.
  - Shape: `Bind("exec-add-property", handler)`.
  - Pros: simple, transparent, easy to test, no reflection magic.
  - Cons: string ids can drift; the current runtime treats an unbound runnable
    command as a runtime failure.
- Explicit registry keyed by loaded `ExecutableCommand` object references.
  - Shape: resolve the model first, then bind a row reference to a handler.
  - Pros: uses generated tooling objects naturally once the workspace is loaded.
  - Cons: more awkward to author; still needs stable lookup by id/name before
    the row reference exists.
- Attribute/reflection discovery.
  - Shape: methods annotated with command ids.
  - Pros: concise at the handler method.
  - Cons: hidden wiring, weaker compile-time review, easy to turn into another
    magic convention pile.
- Generated handler stubs from a CLI workspace.
  - Pros: can provide strong constants and coverage scaffolding.
  - Cons: too early, risks creating CLI-specific generation in the wrong layer.

Chosen first implementation: explicit runtime registry keyed by
`ExecutableCommand.Id`.

If a modeled runnable command is invoked without a registered implementation,
the runtime returns a failure result:

`Command '<route>' has no registered implementation (executable command: <id>).`

This is intentionally runtime wiring, not model data. Do not add implementation
delegates or handler metadata to the model.

## Invocation Object Options

Handlers should not receive raw `string[] args`.

Reasonable options:

- A small generic invocation object.
  - Shape: command row, application row, raw tokens, bound parameters, and
    services such as presenter/cancellation.
  - Pros: minimal, model-native, works before per-command typed objects exist.
  - Cons: handlers still ask for values by modeled parameter id/name.
- Per-command typed request objects.
  - Shape: `AddPropertyRequest { Workspace, Entity, Property }`.
  - Pros: excellent handler ergonomics.
  - Cons: needs generation or hand-written mapping per command; too much for the
    first runtime slice.
- Hybrid generic invocation plus typed value accessors.
  - Shape: `invocation.Required("workspace")`,
    `invocation.Optional("format")`, `invocation.Flag("verbose")`.
  - Pros: small enough to implement now; avoids raw argv; keeps command model as
    source of truth.
  - Cons: still string-addressed until later typed wrappers exist.

Chosen first implementation: `MetaCliInvocation` with command/application rows,
raw argument text, bound modeled parameters, and accessors such as
`Required(...)`, `Optional(...)`, `Flag(...)`, and `Values(...)`.

Move to per-command typed request objects only after parser/runtime behavior
proves stable.

## Migration Direction

After `meta-cli` uses the runtime for itself, migrate the Meta-side CLIs before
Meta-BI.

Order:

- Meta-side CLIs first.
- `meta` itself last among Meta-side CLIs.
- Then begin selecting Meta-BI CLIs.

Do not start by migrating Meta-BI. The foundation CLI runtime should absorb the
pain first.

## Open Questions

- How should MetaDocs import change once MetaCli workspaces replace
  `CliAppDefinition` factories? MetaDocs should import MetaCli workspaces
  directly. It is MetaDocs' responsibility to understand MetaCli as a source
  model; MetaCli should not grow a MetaDocs-specific projection layer.

## Operating Notes

- Keep changes small and proven by generated artifacts.
- Run build/test serially with MSBuild node reuse disabled when broad solution
  output is involved.
- Use the external demo as a behavioral proof after changing public command
  shapes.
- When a chat thread contains an architecture decision, move the decision here
  before relying on it.
