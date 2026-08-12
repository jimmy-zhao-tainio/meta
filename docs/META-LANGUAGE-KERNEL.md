# Meta Language Kernel

## Status

This is an architecture hypothesis for review.
It describes the smallest language-theory frame that fits the current system.
It does not approve a new model, API layer, runtime, or product change.

## The narrow claim

`meta` is a small kernel for defining finite typed attributed graph languages.

A Meta model declares:

- entity types
- text properties on those entity types
- single-valued relationships between entity types
- whether each property or relationship is required
- implicit, stable identity for every entity instance

A Meta instance supplies:

- a finite set of records for each declared entity type
- text values for declared properties
- references for declared relationships

The instance is therefore a finite graph typed by the model:

- records are nodes
- entity declarations are node types
- properties are text attributes
- relationships are typed directed edges
- IDs give nodes stable names

This is close to the typed graph language described by Corradini, Konig, and
Nolte, where a type graph defines a language of graphs that conform to it. Meta
adds required or optional, single-valued members and stable identity to that
basic shape. Typed attributed graph theory supplies the corresponding account
of values attached to graph elements.

Primary references:

- [Specifying Graph Languages with Type Graphs](https://arxiv.org/abs/1704.05263)
- [Fundamental Theory for Typed Attributed Graphs and Graph Transformation](https://dblp.org/rec/journals/fuin/EhrigEPT06)

## A small formal account

For a product `P`, let its structural signature be:

```text
Sigma(P) = (E, A, R, required)
```

where:

- `E` is a finite set of entity types
- `A` is a finite set of text-valued properties, each owned by one entity type
- `R` is a finite set of directed, single-valued relationship types
- `required` marks properties and relationships as total or optional

A workspace instance `I` structurally conforms to `Sigma(P)` when:

- every record belongs to one declared entity type
- every record has an ID unique within that entity type
- every value belongs to a declared property of that entity type
- every relationship points to a record of its declared target entity type
- every required property and relationship is present

Product code often imposes further rules which cannot be expressed by the
current kernel. Let those rules be `C(P)`.

The product language is then:

```text
L(P) = { I | I conforms to Sigma(P) and C(P)(I) }
```

This distinction matters. `model.xml` defines the structural language. It does
not currently define every product invariant or the meaning of a valid
instance. Product validation and product services supply additional
well-formedness and semantics.

## The fixed levels

The architecture only needs four levels.

### 1. Kernel

The fixed Meta grammar defines what can appear in `model.xml`.

The current kernel is deliberately small. It is implemented by the generic
model classes, the model XML codec, integrity rules, and generators. It is not
itself an ordinary product workspace.

### 2. Product language

A product `model.xml` defines one structural graph language.

Examples include MetaCli, MetaMesh, MetaSchema, MetaTransformScript, and
MetaAnalytics.

### 3. Product document

A workspace instance is one finite structure in that product language.

Examples include:

- one command surface in MetaCli
- one operation graph in MetaMesh
- one database schema description in MetaSchema
- one SQL syntax graph in MetaTransformScript
- one analytical model in MetaAnalytics

### 4. Meaning

Product tooling assigns meaning to the document by interpreting, transforming,
rendering, analysing, or executing it.

There is no need for an infinite chain of MetaMetaMeta levels. Some product
documents denote another language or schema, but that is their product
semantics:

- a MetaCli document denotes a command language
- a MetaSchema document denotes a database schema
- a MetaTransformScript document denotes SQL syntax

Each remains an instance of its own Meta product language.

## Workspace is packaging

A workspace is not a fifth semantic level.

It packages:

- one product model
- one product instance
- representation settings such as paths, sharding, encoding, and canonical
  ordering

The model and instance carry product structure. Workspace layout controls how a
representation is stored. Shard placement, XML formatting, and file names are
not domain meaning unless a product explicitly models them.

## Representation

XML, SQL storage, and C# object graphs can represent the same abstract instance.
They are not three different product models.

For each supported surface `s`, define:

```text
decode_s : Representation_s -> L(P)
encode_s : L(P) -> Representation_s
```

The minimum round-trip law is:

```text
decode_s(encode_s(I)) = I
```

where equality preserves:

- entity type
- stable ID
- absent versus present text values
- relationship targets and roles
- required or optional membership
- explicitly modeled order

The reverse direction normally includes canonicalization:

```text
encode_s(decode_s(x)) = canonical_s(x)
```

This permits deterministic formatting, row order, and file sharding without
making those choices semantic.

The word `isomorphic` should be reserved for this representation boundary and
only for the supported subset on which both directions obey the laws. It should
not be used as a synonym for successful generation.

This is related to partial isomorphisms and bidirectional transformations, but
Meta should not claim a lens or a bidirectional transformation unless the
corresponding laws are actually implemented and tested.

Primary references:

- [Invertible Syntax Descriptions](https://www.informatik.uni-marburg.de/~rendel/unparse/)
- [Combinators for Bidirectional Tree Transformations](https://www.cis.upenn.edu/~bcpierce/papers/newlenses-full.pdf)

## Six different operations

Several current discussions use `generation` for operations with different
contracts. They should remain separate.

### Parsing and printing

```text
parse : ConcreteSyntax(P) -> L(P)
print : L(P) -> ConcreteSyntax(P)
```

The product document denotes syntax in an external language.

Example:

- SQL text parsed into MetaTransformScript and printed back as canonical SQL

Required proof:

```text
parse(print(I)) = I
print(parse(x)) = canonical(x)
```

for the declared supported syntax subset and the product's chosen equivalence.
Preserving meaning may be sufficient where the printer deliberately
canonicalizes lexical form.

MetaCli has a related but different shape. A MetaCli document supplies the
grammar, and parsing command-line tokens produces an invocation under that
grammar:

```text
parse_G : Tokens -> Invocation(G)
```

### Representation conversion

```text
L(P) <-> Representation_s
```

The abstract product instance stays the same.

Examples:

- Meta workspace to XML and back
- Meta workspace to relational SQL storage and back
- Meta workspace to a C# object graph and back

Required proof: the representation round-trip laws.

### Model transformation

```text
F : L(P) -> L(Q)
```

The source and target have different product languages.

Examples:

- MetaAnalytics to MetaTabular
- MetaAnalytics to MetaMultiDimensional
- MetaDataWarehouse to MetaSql

Required proof: target conformance and the explicit source meaning preserved by
the transformation. This is not isomorphism unless a real inverse exists.

### Elaboration or analysis

```text
B : L(P) x Environment -> L(Q)
```

The output records facts established by examining the source in an environment.

Example:

- MetaTransformScript plus one or more MetaSchema documents produces a
  MetaTransformBinding document

Required proof: every recorded fact follows from the source and environment,
and rejected input reports the unsupported or invalid boundary.

### Rendering or compilation

```text
G : L(P) -> Artifact
```

Examples:

- a Meta model rendered as generated C# tooling
- MetaDocs rendered as HTML
- MetaSql rendered as deployable SQL

Required proof: a stated relation between source meaning and artifact meaning.
Deterministic bytes are useful, but determinism alone is not semantic
correctness.

### Interpretation or execution

```text
E : L(P) x Environment -> Effects
```

Examples:

- MetaCli parses command tokens and dispatches a bound handler
- MetaMesh validates and executes operation steps
- MetaPipeline executes modeled tasks

Required proof: execution follows the modeled structure, ordering, inputs, and
failure contract.

## How representative products fit

### MetaCli

The MetaCli model defines the abstract structure of command grammars. A MetaCli
workspace instance denotes one command language. Command-line tokens are its
concrete syntax. `MetaCliRuntime` parses that syntax and interprets the result by
dispatching the modeled executable command to an externally bound handler.

### MetaMesh

The MetaMesh model defines workspaces, operations, and predecessor-linked
operation steps. A MetaMesh instance is an executable declarative document. The
runtime resolves its environment and interprets the ordered steps as process
effects.

### MetaSchema

A MetaSchema instance is a typed graph that denotes a database schema.
Extraction observes an external database and constructs that graph. Deployment
or downstream conversion interprets the graph in another system.

### MetaTransformScript

MetaTransformScript is the clearest conventional language case. SQL text is
parsed into an abstract syntax graph. The graph preserves identity, sharing,
polymorphic syntax families, and modeled sequence. SQL emission maps the graph
back to canonical SQL for the bounded supported language.

The whole workspace is not an initial term algebra because it can contain
shared identities and graph references. Initial algebra semantics may still be
useful inside tree-shaped expression fragments; it is not the generic Meta
kernel.

### MetaTransformBinding

Binding is elaboration. It resolves names and checks uses and writes against
schema environments. Its output is a new model instance containing established
facts. The binding document is not another representation of the transform
script.

### MetaAnalytics

A MetaAnalytics instance declares target-independent analytical intent.
Converters lower that document into MetaTabular or MetaMultiDimensional
instances. These are typed model transformations with explicit target policy,
not representation round trips.

## What the kernel does not claim

The current kernel is not:

- a general-purpose programming language
- a general constraint language
- a theorem prover
- an algebraic data type system
- a graph rewrite language
- a complete implementation of MOF, QVT, or another modeling standard
- a guarantee that every product model is executable
- a guarantee that every generated target is invertible

MOF is useful as a comparison because it separates metamodel definition,
interchange, constraints, transformations, and model-to-text generation. Meta
occupies a deliberately smaller area. It should not acquire the rest of MOF by
accident.

Primary specifications:

- [Meta Object Facility 2.5.1](https://www.omg.org/spec/MOF/2.5.1/PDF)
- [Query/View/Transformation 1.3](https://www.omg.org/spec/QVT/1.3/PDF)

## Current implementation gaps exposed by this frame

The theory is useful only if it reveals where the implementation disagrees.

### Required-reference acyclicity may be an implementation leak

Generic validation rejects cycles in the graph of required relationship types.
A typed attributed graph language does not require this restriction. C# object
graphs and relational schemas can both represent cycles.

The restriction should either be accepted and documented as part of the Meta
kernel or removed from structural conformance. It should not remain an
unexamined consequence of construction or insert ordering.

### Generic SQL is not yet a general representation surface

The generic SQL generator topologically orders entity types using every
relationship. It therefore rejects a model with a self-reference even when the
relationship is optional and the instance is a valid finite chain.

This can be reproduced with the current MetaMesh model:

```text
meta generate sql --workspace MetaMesh/Workspace --out <temporary-directory>

Cannot generate data script because relationship cycle includes
'OperationStep'.
```

`OperationStep.PreviousStep` is valid modeled order. A representation provider
must represent it rather than reject the product model.

The SQL importer also reconstructs relationships as required and rejects null
relationship columns. It therefore cannot currently round-trip optional
relationships.

### Scalar types are not part of the proven kernel

`GenericProperty.DataType` exists, but current generated C# properties are
strings and generic SQL properties are `NVARCHAR(MAX)`. SQL import reconstructs
them as `string`. Current checked-in product models do not use `dataType` in
`model.xml`.

The honest v1 kernel therefore has text properties. Scalar typing should not be
claimed until it has one modeled meaning and natural, law-tested
representations in each sanctioned surface.

### Structural conformance is not full product validity

The generic kernel proves names, required members, identity, and referential
integrity. Many products impose stronger rules in services and validators.

Documentation should distinguish:

- structural conformance to `model.xml`
- product validity under additional product rules
- semantic correctness of a transformation or interpreter

### Existing round-trip evidence is bounded

The generic XML-SQL-XML proof covers a bounded relational shape. The
MetaTransformScript corpus proves a different boundary: supported SQL syntax
can be parsed, emitted, parsed again, and emitted canonically.

These are valuable proofs. They do not establish one global isomorphism theorem
for every Meta product and every surface.

## Streamlining direction

No new universal framework is required to use this account.

Use these terms consistently:

- `kernel` for the fixed Meta structural grammar
- `product model` or `structural signature` for `model.xml`
- `instance` or `product document` for a conforming workspace graph
- `workspace` for the package and representation configuration
- `codec` for a lawful representation pair
- `transformation` for model-to-model work
- `elaboration` or `analysis` for derived evidence
- `renderer` or `compiler` for model-to-artifact work
- `interpreter` or `runtime` for behavior and effects

Apply these proof obligations:

1. A loader must construct a structurally conforming graph or fail.
2. A parser and printer must state their supported syntax and canonicalization
   laws.
3. A codec must prove its round-trip laws over a named supported subset.
4. A transformation must produce a valid target document and state what source
   meaning it preserves.
5. An analyser must record only facts established from declared inputs.
6. An interpreter must follow modeled ordering, parameters, and failure
   semantics.

Avoid these immediate moves:

- do not add a generic semantics model
- do not add a generic transformation model
- do not add a multilevel metamodel hierarchy
- do not rename every existing API before the vocabulary is reviewed
- do not call all output `generation`
- do not call all successful round trips `isomorphism`

The first implementation work suggested by this account is smaller:

1. Define and test the exact generic XML, SQL, and C# representation subsets.
2. Repair or narrow the generic SQL representation claim.
3. Decide whether `DataType` is removed from the text-only kernel or made real.
4. Classify existing product operations under the six contracts above.
5. Tighten public prose only after those contracts match the implementation.
