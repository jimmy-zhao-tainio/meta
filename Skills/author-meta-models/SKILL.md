---
name: author-meta-models
description: Choose and use the owning Meta or Meta-BI model CLI to author sanctioned workspaces without editing generated representations directly.
---

# Author Meta models

Use this skill when creating or changing modeled instances through the public
CLIs. It does not authorize changing a sanctioned product model; model contract
changes require deliberate architectural review.

## Choose the owner

Read [references/cli-routing.md](references/cli-routing.md) to select the CLI.
Do not use the generic `meta` mutation commands merely because the target is a
Meta workspace. Product CLIs preserve identities, relationships, ordering, and
authoring policy that raw row insertion may bypass.

## Discover the current command surface

```powershell
<cli> help
<cli> help <command>
```

The runtime help is the exact installed surface. MetaDocs is the combined
explanatory reference. Do not infer option names from another CLI or freeze a
copied command catalogue into automation.

## Author through one surface

1. Use the CLI's `create` command or the first command's output-surface options
   to create one XML, C#, or SQL workspace.
2. Continue mutations through `--workspace <path>` or from the workspace
   directory where supported.
3. Supply stable modeled identities deliberately. Do not derive identities from
   display names unless that is the documented product contract.
4. Use predecessor/relationship options for modeled order; do not invent
   padded scalar ordinals.
5. Inspect through the owning CLI's `show` or `inspect` command when available,
   then use `meta status`, `list`, `query`, and `view` for generic evidence.

For scripts or projections supplied through standard input, preserve the
command's stdin contract instead of creating incidental `.sql` files solely to
pass text between commands.

## Reuse executable examples

Before assembling a long authoring sequence, locate the closest `meta-bi/Demos`
MetaMesh workspace. Its ordered operations are the maintained command witness.
Adapt the modeled operation or invoke the owning CLI; do not create an
untracked shell transcript as a second implementation of the same demo.

## Verify

Run command-specific inspection, load the resulting workspace through `meta`,
and exercise the downstream consumer or sanctioned conversion. Model
authorship is not proven merely because files were emitted.
