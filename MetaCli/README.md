# MetaCli

MetaCli is the modeled command-language foundation used by Meta command-line
tools. A MetaCli workspace defines an application, its command paths and
executable endpoints, and the options, positional arguments, value shapes,
arity, allowed values, and parameter groups accepted by those commands.

`MetaCliRuntime` loads that workspace, interprets command-line tokens against
the modeled grammar, selects the executable command, and dispatches its stable
identity to a bound handler. The same command model supplies usage and help,
so parsing, runtime dispatch, and command reference all describe one authored
surface.

Each Meta CLI owns its command workspace. For example, the `meta-docs` command
surface lives in [`MetaDocs/Cli/meta-docs.MetaCli`](../MetaDocs/Cli/meta-docs.MetaCli)
and the `meta-mesh` surface lives in
[`MetaMesh/Cli/meta-mesh.MetaCli`](../MetaMesh/Cli/meta-mesh.MetaCli). Product
code binds the modeled executable commands to domain services; MetaDocs imports
the same workspaces when it builds the public CLI reference.

## Author a command surface

The `meta-cli` executable creates and changes MetaCli workspaces. Its commands
add applications, command routes, executable commands, options, positionals,
parameter groups, value shapes, and allowed values as modeled data.

```text
meta-cli show --workspace <cli-workspace>
meta-cli help
```

`show` presents the authored command tree. The authoring commands maintain the
relationships and ordering that make the surface executable by
`MetaCliRuntime`.

The runtime boundary and modeling rules are documented in
[MetaCli Architecture](ARCHITECTURE.md). The generated command and model
reference is available at
[metametabi.com/docs.html](https://metametabi.com/docs.html).
