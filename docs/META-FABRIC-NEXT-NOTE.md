# MetaFabric next note

## Current strength

The current `MetaFabric` model and service are strong enough for shared-parent scoped bindings.

That pattern looks like this:

- child weave binding is ambiguous on its own
- a parent weave binding already resolves source parent rows to target parent rows
- the child source rows carry a parent reference to the source parent rows
- the child target rows carry a parent reference to the target parent rows
- applying the parent mapping removes the ambiguity deterministically

This is the case already proven by the current sample fabric workspace:

- `MetaFabric.Workspaces/Fabric-Scoped-Group-CategoryItem`

And by the BI-side fabric sample:

- `meta-bi/Fabrics/Fabric-Scoped-MetaBusiness-MetaBusinessDataVault-LinkEndParticipant-Commerce`

## Current limitation

The current model is not strong enough for multi-hop or cross-parent scope.

The unresolved pattern looks like this:

- child source rows scope through source parent `A`
- child target rows scope through target parent `B`
- `A` and `B` are not themselves the direct endpoints of one already-resolved parent binding
- instead, the target-side path runs through another intermediate binding or relationship

This is the current Business/BDV key-part problem:

- source child: `BusinessHubKeyPart`
- source parent: `BusinessHub`
- target child: `BusinessKeyPart`
- target parent: `BusinessKey`

But the flat business anchor is:

- `BusinessHub.Name` -> `BusinessObject.Name`

The target child does not scope directly through `BusinessObject`. It scopes through `BusinessKey`, which itself belongs to `BusinessObject`.

So the needed path is not:

- parent binding + child binding

It is more like:

- parent binding
- plus an additional target-side structural step
- then child binding

## What this means

The current `BindingScopeRequirement` concept is intentionally narrow:

- one child binding
- one parent binding
- one source parent reference name
- one target parent reference name

That is enough for shared-parent scope.

It is not enough for:

- multi-hop target scope
- multi-hop source scope
- chained binding scope
- scope that depends on more than one already-resolved binding

## Next likely extension

The next fabric capability should probably not be another ad hoc property on `BindingScopeRequirement`.

The missing concept is a scoped path, not just a scoped parent.

In practical terms, that likely means some future concept in the family of:

- `BindingPathRequirement`
- `BindingScopePathStep`
- or another equivalent way to express:
  - from child source row, follow this source-side path
  - from child target row, follow this target-side path
  - compare those rows through already-resolved binding references

## Design rule

The important rule is unchanged:

- keep `MetaWeave` flat
- keep `MetaFabric` operating on weave workspaces only
- do not push this multi-hop scope problem down into domain CLIs unless it proves truly domain-specific

At the moment, the Business/BDV key-part seam is evidence that the next issue is foundational.
