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
- `meta-cli new-workspace` can seed ordinary CLI value shapes and a default
  help command directly as normal model rows.
- external demo proof exists at `Samples/Demos/MetaCliAuthoringDemo/demo-meta-cli.ps1`.
- `MetaCli.Core` contains a first model-backed runtime parser/dispatcher.

The immediate focus is the runtime and real CLI adoption of it.

## Accepted Model Direction

MetaCli is model-first.

The current model slice is the CLI invocation grammar: applications, command
routes, runnable commands, options, positionals, value shapes, parameter groups,
and shared application/command parameter scopes. It does not model parser
policy rows, process output contracts, output streams, or exit-code catalogs.
Those may become separate models later if they earn their own layer.

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

- Use `meta-cli` to author workspaces. Script substantial initial creation and
  migrations with direct `meta-cli` invocations.
- Do not hand-edit workspace XML except for deliberate forensic repair.
- Manual `meta-cli` edits are fine for tiny changes.
- Review and commit the resulting workspace diff; do not treat a bootstrap
  script as the long-term source of truth.
- Do not regenerate a CLI workspace from C# help/prose.
- Do not add a second command-surface language unless the model itself proves
  insufficient. The checked-in `.MetaCli` workspace is the authored artifact.

`meta-cli new-workspace` can seed ordinary CLI plumbing:

- value arities: `none`, `one`
- value shapes: `flag`, `text`, `path`, `token`, `bool`
- bool values: `true`, `false`
- optional application and default help command

Do not model operations as switches such as `--new-workspace`, and do not
reintroduce a root-command role.

ID naming convention:

- Application ids remain `app-<executable-name>`.
- Command and executable command ids remain route-derived.
- Parameter, option, token, positional, and group ids should include explicit
  route/name boundary words such as `command` and `parameter`. This prevents
  route/name collisions like `add-option --token-id` versus `add-option-token
  --id`.

If a mechanical id becomes ambiguous at the route/name boundary, use a clearer
semantic id instead. For example, avoid letting `add-option --token-id` collide
with `add-option-token --id`.

## Core Invariants

- Do not add compatibility aliases to keep old command surfaces alive.
- Do not model command kind, command group, command segment, or similar
  discriminator strings.
- Do not add `Ordinal`, `Position`, `Order`, or leading-zero sort hacks.
- Ordered CLI collections use relationships such as `PreviousToken`,
  `PreviousArgument`, and `PreviousMember`.
- Command route structure is modeled by `Command.ParentCommand`.
- Runnable commands are modeled by `ExecutableCommand`.
- Non-runnable grouping commands are plain `Command` rows without
  `ExecutableCommand` rows.
- No-argument default invocation is modeled by `ApplicationDefaultCommand`.
- `Parameter` is the shared contract identity for options and positionals.
- Application-wide parameters are scoped by `ApplicationParameter`.
- Command-specific parameters are scoped by `ExecutableCommandParameter`.
- `Option` and `PositionalArgument` are invocation forms over `Parameter`.
- `ParameterGroup` groups parameters, not only options, so surfaces such as
  `<Id> | --auto-id` do not require parser exceptions.
- `ValueShape` is required for parseable parameters.
- `ValueArity` and unordered `AllowedValue` sets are enough for this argument
  grammar slice.
- Do not add `ValueCodec` without concrete value semantics that cannot be
  modeled by arity, allowed values, or command-specific handling.
- Do not add parser-policy or parser-behavior entities until there is modeled
  syntax truth that cannot be represented by commands, parameters, tokens,
  arity, allowed values, or command-specific handling.
- Do not model output contracts or exit-code catalogs in this slice. They are
  not CLI argument grammar.

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

- `add-application-option` creates `Parameter`, `ApplicationParameter`,
  `Option`, and the first `OptionToken`.
- `add-option` creates `Parameter`, `ExecutableCommandParameter`, `Option`,
  and the first `OptionToken`.
- `add-option-token` only adds alias tokens to an already valid option.
- `add-positional` creates `Parameter`, `ExecutableCommandParameter`, and
  `PositionalArgument`.
- `add-parameter-group` creates `ParameterGroup` and its first
  `ParameterGroupMember`.
- `add-parameter-group-member` only adds additional members.
- `set-default-command` attaches an existing `ExecutableCommand` as the
  no-argument default.

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

- `meta-cli new-workspace <path> [--application <name>] [--standard-cli-shapes] [--default-help]`
- `meta-cli show [--workspace <path>]`

Authoring:

- `add-application`
- `add-command`
- `add-executable-command`
- `set-default-command`
- `add-value-arity`
- `add-value-shape`
- `add-allowed-value`
- `add-application-option`
- `add-option`
- `add-option-token`
- `add-positional`
- `add-parameter-group`
- `add-parameter-group-member`

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
- command token completeness
- value arity cardinality text, including no leading-zero numeric padding
- value-shape references
- parameter application/executable scopes
- parameter value-shape completeness
- option token-chain head completeness
- option-token chain integrity
- positional-argument chain integrity
- allowed-value uniqueness within value shape
- parameter-group member chain integrity
- required group member completeness
- boolean text fields

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
- adding parser-policy or parser-behavior rows without concrete modeled syntax
  truth
- reintroducing output or exit-code contracts into this argument-grammar model
- reintroducing `from-syntax` or a parallel command-surface language before the
  modeled workspace authoring surface itself proves insufficient

Pre-existing `CliAppDefinition` infrastructure remains legacy surface area in
other CLIs and MetaDocs import paths. It is not the MetaCli architecture.

## Runtime Direction

The runtime/parser consumes MetaCli workspaces through generated MetaCli
tooling.

The public runtime shape is a runner. Consumers provide the command-surface
workspace and command handlers, then call `Run(...)`. The domain model type
must be generated tooling that implements the shared workspace model contract:

```csharp
var runtime = new MetaCliRuntime<MyModel>("my-cli.MetaCli")
    .Bind("exec-show", (invocation, model) => Show(invocation, model))
    .Bind("exec-help", invocation => Help(invocation));

runtime.Run(args);
```

`Run(...)` returns nothing. It owns command-surface loading, argv parsing,
domain workspace path resolution, domain workspace loading, handler dispatch,
failure writing, and process exit-code assignment.

Workspace-backed handlers receive the modeled invocation plus the loaded domain
model instance. No-workspace handlers receive only the invocation. The default
domain workspace path is the current working directory; if a modeled parameter
named `workspace` is present and supplied, that value is used instead.

The first implemented shape is:

- use generated MetaCli tooling classes as the parser contract model
- expose `Run(...)` as the only public execution API: argv in, handler effects
  or written failure out
- return `MetaCliInvocation` with application, command, executable command,
  route, raw arguments, and bound parameters to handlers
- keep structured parse error codes as internal runtime machinery; consumers do
  not receive parse result objects
- preserve binding occurrence details so consumers can inspect the option token
  or positional argument that supplied a parameter value, and whether a value
  came from a modeled default
- keep parsing, binding, provider integrity proof, and presentation separable
- keep normal CLI output in CLI/presenter/handler code
- keep runtime failure output in the runtime because the user cannot act on a
  handler when parsing, command-surface loading, domain workspace loading, or
  handler lookup has failed

The first runtime slice supports command-tree dispatch, no-argument default
commands, application parameters, command parameters,
options, option-token chains, `--option=value`, the `--` parsing stop token,
positionals, required parameters, parameter groups, allowed values, and zero/one
value arity. It loads the command-surface workspace and, for workspace-backed
handlers, loads the domain model through `TModel.LoadFromXmlWorkspace(...)`.
It does not migrate any public CLI onto the runtime yet.

Runtime tests cover common parse failures: missing/ambiguous application,
unknown command, non-runnable command group, missing default command, unknown
option, duplicate option, missing option value, option after positional,
unexpected positional argument, parameter group miss/conflict, disallowed value,
command-surface load failure, missing handler failure, domain workspace load
failure, no-workspace handler dispatch, workspace-backed handler dispatch, and
current-directory workspace defaulting. `applicationId` is the current explicit
selection path when a command-surface workspace contains several applications.

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

Chosen first dispatch implementation: explicit runtime registry keyed by
`ExecutableCommand.Id`, executed by `Run(...)`.

If a modeled runnable command is invoked without a registered implementation,
`Run(...)` writes a runtime failure and sets a non-zero exit code:

`Command '<route>' is modeled but has no implementation.`

Handler wiring is intentionally runtime wiring, not model data. Do not add
implementation delegates or handler metadata to the model.

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
`Binding(...)`, `Required(...)`, `Optional(...)`, `Flag(...)`, and `Values(...)`.
Each binding keeps both the simple value list and modeled occurrences. This
lets a consumer stay simple or inspect source tokens/positionals when needed.

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
