# MetaCli Architecture

## Purpose

MetaCli is the model-first command-surface foundation for the Meta toolchain.
It describes applications, command routes, executable commands, parameters,
options, positionals, values, and the relationships that make a command
surface parseable and documentable.

Each CLI has its own authored MetaCli workspace. There is no suite-level
MetaCli workspace in the current architecture.

## Source Of Truth

The authored workspace is the source of truth for one CLI's command surface.
The workspace is maintained with `meta-cli`; it is not reconstructed from
handwritten parser code or help text. Typed C# sources may be used by an
application, but they are a workspace surface and implementation artifact, not
a second command grammar.

Do not hand-edit workspace instance data for ordinary authoring. Use the
MetaCli authoring commands and review the resulting workspace change.

## Runtime Boundary

`MetaCliRuntime<TModel>` owns command-surface loading, argument parsing, help
and usage conventions, workspace selection, handler lookup, failure writing,
and process exit-code assignment. A CLI program should bind executable command
ids to small handlers and call `runtime.Run(args)`.

Handlers receive the modeled invocation and the loaded domain workspace when
their command needs one. They map invocation values into domain requests, call
domain services, and present structured service results. They must not parse
generic argument arrays or reload the primary workspace behind the runtime.

The runtime uses the workspace descriptor and surface API to open the selected
domain workspace. When a workspace option is omitted, workspace-oriented
commands use the current working directory.

## Modeling Rules

- Model command routes with command relationships, not discriminator strings.
- Model runnable commands with `ExecutableCommand`.
- Model parameter scopes explicitly for application and command parameters.
- Use relationships for ordered collections; do not add ordinal or padded-text
  ordering properties.
- Do not add compatibility aliases, vague `Kind` properties, or implementation
  delegates to the MetaCli model.
- Keep parser behavior in the runtime when it is a convention of the grammar;
  add model structure only when the syntax has a real semantic distinction.
- Public authoring commands create complete semantic aggregates and do not
  persist a command surface that fails its modeled integrity rules.

## CLI Boundary

The CLI project owns version wiring, runtime construction, command bindings,
and presentation. Domain services own domain behavior and return structured
results. MetaDocs imports authored MetaCli workspaces directly; MetaCli does
not grow documentation-specific projection or prose behavior.

## Verification

Build the runtime and its CLI before tests that invoke the executable. Run
serially when projects share local foundation outputs:

```powershell
dotnet build MetaCli\Cli\MetaCli.Cli.csproj --nologo -m:1 -nr:false
dotnet test MetaCli\Tests\MetaCli.Tests.csproj --nologo -m:1 -nr:false
```

The authored workspace, current source, and tests are authoritative. Historical
audits and old command examples are not current runtime guidance.
