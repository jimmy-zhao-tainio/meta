# MetaFabric next note

## Current strength

The current `MetaFabric` model and service are strong enough for both:

- shared-parent scoped bindings
- multi-hop path-to-parent scoped bindings

That is now proven by:

- `MetaFabric.Workspaces/Fabric-Scoped-Group-CategoryItem`
- `meta-bi/Fabrics/Fabric-Scoped-MetaBusiness-MetaBusinessDataVault-LinkEndParticipant-Commerce`
- `meta-bi/Fabrics/Fabric-Scoped-MetaBusiness-MetaBusinessDataVault-HubKeyPart-KeyPart-Commerce`

The last case matters because the child target rows scope through `BusinessKeyId.BusinessObjectId`, so fabric now handles a multi-hop path instead of only a one-step shared parent.

## Current limitation

The next gap is no longer path depth. The next gap is scope that depends on more than one already-resolved binding or on path conditions that are not reducible to one parent binding.

The unresolved pattern looks like this:

- child binding `C` is ambiguous
- resolving `C` requires more than one parent binding context
- or resolving `C` requires structural predicates beyond a simple source path / target path equality under one parent binding

Examples of future pressure:

- one child binding must be valid only when two different parent bindings agree
- one child binding must be scoped by a parent binding and an ordinal or role constraint at the same time
- one child binding depends on several sibling bindings, not just one parent binding

## What this means

The current `BindingScopeRequirement` concept is intentionally focused:

- one child binding
- one parent binding
- one source-side path
- one target-side path

That is enough for:

- one-hop shared-parent scope
- multi-hop path-to-parent scope

It is not yet enough for:

- multi-parent scope
- conjunctive scope across different parent bindings
- richer structural predicates beyond path traversal

## Next likely extension

If those cases become real across more than one domain, the next fabric capability will likely need some concept in the family of:

- `BindingScopeGroup`
- `BindingPredicate`
- `BindingScopeCondition`

The likely shape is:

- keep path steps for navigation
- add explicit grouped or conditional scope semantics on top
- continue to operate only over referenced weave bindings, not domain models directly

## Design rule

The important rule is unchanged:

- keep `MetaWeave` flat
- keep `MetaFabric` operating on weave workspaces only
- do not push foundational scoped-binding complexity down into domain CLIs unless it proves truly domain-specific

At the moment, the next issue is no longer “can fabric do multi-hop?” It can. The next issue is whether future sanctioned model families need multi-parent or conditional scope.