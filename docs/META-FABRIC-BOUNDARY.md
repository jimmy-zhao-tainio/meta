# MetaFabric Boundary [to be revised]

## Purpose

`MetaFabric` is the sanctioned foundational model for scoped binding over weave workspaces.

It exists because `MetaWeave` is intentionally flat:
- one referenced workspace per `ModelReference`
- one direct property equivalence per `PropertyBinding`

That is enough for direct correspondence such as:
- `BusinessHub.Name -> BusinessObject.Name`
- `BusinessLink.Name -> BusinessRelationship.Name`

It is not enough for child rows whose validity depends on an already resolved parent binding, especially when the child rows must follow one or more structural steps to reach that parent context.

Examples:
- a child key-part binding is only valid inside the parent hub/object binding already chosen
- a child relationship-end binding is only valid inside the parent link/relationship binding already chosen

## Research grounding

This model is informed by three real concept families.

### 1. Weaving models

EMF Views models a weaving root that contains contributing models and virtual links. That is a real precedent for treating cross-model links as explicit metadata rather than hidden tool logic.

### 2. Correspondence languages

Triple Graph Grammar literature models source, target, and correspondence together. The correspondence layer is explicit rather than implicit. That is the relevant lesson for this project: cross-model alignment may need its own first-class structure.

### 3. Relation dependencies

QVT Relations supports `when` and `where` dependencies between relations. That is a real precedent for saying one relation only makes sense in the context of another relation.

`MetaFabric` is not a copy of any of these. It is the minimal sanctioned model derived from the same first principles:
- explicit cross-model bindings remain in weave workspaces
- scoped consistency over those bindings belongs in a second foundational layer

## Scope

`MetaFabric` operates on weave workspaces only.

It does not bind domain workspaces directly.

That means:
- `MetaWeave` remains the sanctioned direct binding tool
- `MetaFabric` composes and constrains those sanctioned weaves

This keeps layering clean:
- domain models own domain meaning
- weave models own direct cross-model equivalence
- fabric owns grouped and scoped consistency across weave bindings

## Concrete XML example

The sanctioned sample workspaces show the minimal scoped-binding case.

Source model:

```xml
<Model name="SampleScopedSourceCatalog">
  <EntityList>
    <Entity name="Group">
      <PropertyList>
        <Property name="Name" />
      </PropertyList>
    </Entity>
    <Entity name="Item">
      <PropertyList>
        <Property name="Name" />
      </PropertyList>
      <RelationshipList>
        <Relationship entity="Group" />
      </RelationshipList>
    </Entity>
  </EntityList>
</Model>
```

Reference model:

```xml
<Model name="SampleScopedReferenceCatalog">
  <EntityList>
    <Entity name="Category">
      <PropertyList>
        <Property name="Name" />
      </PropertyList>
    </Entity>
    <Entity name="CategoryItem">
      <PropertyList>
        <Property name="Name" />
      </PropertyList>
      <RelationshipList>
        <Relationship entity="Category" />
      </RelationshipList>
    </Entity>
  </EntityList>
</Model>
```

With instance rows like:

```xml
<Item Id="item:alpha:common" GroupId="group:alpha">
  <Name>Common</Name>
</Item>
<Item Id="item:beta:common" GroupId="group:beta">
  <Name>Common</Name>
</Item>
```

```xml
<CategoryItem Id="categoryitem:alpha:common" CategoryId="category:alpha">
  <Name>Common</Name>
</CategoryItem>
<CategoryItem Id="categoryitem:beta:common" CategoryId="category:beta">
  <Name>Common</Name>
</CategoryItem>
```

The parent weave binds `Group.Name -> Category.Name`. The child weave binds `Item.Name -> CategoryItem.Name`. On its own, the child weave fails because `Common` is globally ambiguous. Fabric closes that gap by saying the child binding must be evaluated under the parent binding using `GroupId` on the source side and `CategoryId` on the target side.

The sanctioned unscoped sample `MetaFabric.Workspaces\Fabric-Suggest-Scoped-Group-CategoryItem` is intentionally missing that scope requirement so `meta-fabric suggest` can propose it deterministically.

## Current sanctioned concepts

### `WeaveReference`

References one weave workspace.

It carries:
- `Alias`
- `WorkspacePath`
- optional `Description`

A fabric implementation should load the referenced workspace and verify that its model is `MetaWeave`.

### `BindingReference`

References one `PropertyBinding` inside one referenced weave workspace.

It carries:
- `Name`
- `BindingName`
- optional `Description`
- relationship to `WeaveReference`

This makes weave bindings first-class inside the fabric without duplicating the actual binding definition.

### `BindingScopeRequirement`

Declares that one binding is only valid within the context of another binding.

It carries:
- relationship `Binding` -> the child binding being constrained
- relationship `ParentBinding` -> the already resolved parent binding
- optional `Description`

Semantics:
- when evaluating a row pair under `Binding`, the source row must resolve to a source parent row through its source-side scope path
- the target row must resolve to a target parent row through its target-side scope path
- those parent rows must themselves resolve through `ParentBinding`

A binding may have multiple scope requirements. They are conjunctive.

### `BindingScopePathStep`

Carries one ordered step in a scope path.

It carries:
- `Side`
- `Ordinal`
- `ReferenceName`
- relationship `BindingScopeRequirement`

Semantics:
- `Side` is `Source` or `Target`
- `Ordinal` orders the path
- `ReferenceName` is the relationship usage name or reference column name to follow at that step

This keeps the model general enough for both:
- shared-parent scope such as `GroupId` / `CategoryId`
- multi-hop scope such as `BusinessKeyId.BusinessObjectId`

## What this model is deliberately not doing yet

Not yet in scope:
- grouped execution plans
- transitive materialization semantics
- domain-specific shortcuts
- alternative scope kinds beyond path-based parent-scoped requirements
- cyclic dependency handling beyond the general rule that cycles should be invalid

Those may come later if real use cases require them.

See also: `docs/META-FABRIC-NEXT-NOTE.md`.

## Current rule set

1. A fabric workspace references weave workspaces only.
2. A fabric binding reference points to an existing weave `PropertyBinding` by name.
3. A scope requirement constrains one binding by another binding.
4. Scope paths are expressed by ordered `BindingScopePathStep` rows.
5. Multiple scope requirements on the same binding are all required.
6. Fabric should reject cyclic scope graphs.

## Why this is the current minimum

This is the smallest model that closes the scoped binding gap without pushing that complexity into domain CLIs.

It keeps the foundation honest:
- no Data Vault-specific hacks in `meta-datavault`
- no pretending flat weave is enough when it is not
- no premature generalization beyond the scoped binding problem already visible in sanctioned models

Current sanctioned samples prove both shapes:
- shared-parent scope:
  - `MetaFabric.Workspaces/Fabric-Scoped-Group-CategoryItem`
- multi-hop path scope:
  - `meta-bi/Fabrics/Fabric-Scoped-MetaBusiness-MetaBusinessDataVault-HubKeyPart-KeyPart-Commerce`

## Sources

- EMF Views User Guide: <https://www.atlanmod.org/emfviews/manual/user.html>
- Eclipse QVT Declarative overview: <https://help.eclipse.org/latest/topic/org.eclipse.qvtd.doc/help/OverviewandGettingStarted.html>
- Eclipse QVT Declarative documentation PDF: <https://download.eclipse.org/qvtd/doc/0.14.0/qvtd.pdf>
- Triple Graph Grammar discussion of correspondence language: <https://link.springer.com/article/10.1007/s10270-024-01238-1>
- Algebraic semantics for QVT Relations (`when` / `where` dependencies): <https://repositorio.uam.es/bitstreams/9457c950-13cd-463c-a898-b466d58ef93f/download>