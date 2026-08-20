# MetaMesh

MetaMesh models repeatable repository work. A mesh workspace names the
participating workspaces, establishes a repository root, groups work into
named operations, and records the executable steps that perform each
operation.

An operation step carries its executable, arguments, working directory,
expected exit code, and predecessor. Arguments can refer to the mesh root and
declared workspace locations, allowing a workflow to move with the repository
instead of depending on one machine's absolute paths.

`meta-mesh validate` resolves the operation and its workspace references before
execution. `meta-mesh run` follows the modeled step order, invokes each command,
reports progress, and stops with the failing step's evidence when a command
does not satisfy its declared result.

## Run a modeled workflow

The documentation build in this repository is a working mesh. It imports the
current CLI and model references from `meta` and `meta-bi`, merges the public
suite, validates it, and renders the site through one named operation.

```text
meta-mesh show --workspace MetaDocs/Docs/Documentation.MetaMesh
meta-mesh validate --workspace MetaDocs/Docs/Documentation.MetaMesh --operation regenerate-public-docs
meta-mesh run --workspace MetaDocs/Docs/Documentation.MetaMesh --operation regenerate-public-docs
```

The same model scales to larger construction paths. The checked-in
[AdventureWorks BI stack](https://github.com/jimmy-zhao-tainio/meta-bi/tree/main/Demos/AdventureWorksBiStackDemo)
uses MetaMesh to preserve the discovery, modeling, conversion, deployment,
pipeline, verification, and analytical operations that build the system.
