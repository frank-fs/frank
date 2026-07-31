# Frank.Rdf

**Date**: 2026-07-30
**Branch**: `rdf` (not yet created)
**Status**: Draft — awaiting review

## Context

`Frank.JsonHome` describes an API's entry points. The deferred [ALPS design](2026-07-28-frank-alps-protocol-design.md) describes an API's vocabulary and state transitions. Neither describes what a specific *resource instance* actually is in terms the wider web already understands — that a given `/games/{id}` is a `schema:Game`, that it `schema:sameAs` the Wikidata and DBpedia entries for tic-tac-toe. That's linked data's job: grounding a resource's data in shared, dereferenceable vocabulary rather than an app-private shape.

Today this is hand-rolled. `~/Code/tic-tac-toe/src/TicTacToe.Web/Discovery.fs` builds its schema.org JSON-LD representation as a raw F# string template (`gameJsonLd`), alongside a hand-rolled ALPS profile in the same file. There is no Frank package backing either.

There is a prior, untrusted attempt at this in `frank`'s `feature/v7.3.2` branch: full `Frank.LinkedData`, `Frank.Validation` (SHACL), `Frank.Provenance` (PROV-O), and `Frank.Discovery` (JSON Home + ALPS) packages, all built on `dotNetRdf.Core`/`dotNetRdf.Shacl` against a shared `Frank.Semantic` base. That base's centerpiece was a `vocabulary { }` CE that declared mappings from CLR types (`typeof<Game>`) to vocabulary terms, resolved by a build-time codegen step (`GeneratedLinkedDataResolver`, `.frank/semantic-mappings.lock.json`). This is judged to be why that effort stalled: codegen was the first thing built, so every wrong assumption about representation got baked into the generator, repeating across three attempts. See [[feedback_outside_in_before_codegen]].

This design treats that branch as reference only, not a starting point. It keeps the one part of it worth keeping — **dotNetRDF as the underlying RDF library** — and drops the reflection/codegen layer entirely. What replaces it is a directly hand-authored builder: the same "authored, not derived" house style `Frank.JsonHome` and the ALPS design already use for their own vocabulary (see *Why vocabulary is authored, not derived* in the ALPS design). The deliverable here isn't tooling that makes authoring JSON-LD easy — it's the target shape itself: what an LLM or a developer writes by hand, for now, so that a *future* codegen effort has something concrete to generate rather than something to guess at. That future work is explicitly out of scope here (see Non-goals) and stays deferred until this shape has proven out across at least two concrete samples.

### Naming: `Frank.Rdf`, not `Frank.LinkedData`

This design started out as `Frank.LinkedData`, and moved during review. The original name fit a JSON-LD-document-shaped model; once the model moved to flat RDF triples (see *Data model*), the package's own center of gravity shifted from "produces JSON-LD" to "builds RDF, of which JSON-LD is one projection" — and `Frank.Provenance`/`Frank.Validation` depending on it for that RDF foundation reinforces that. So the package, namespace, and file names all track `Rdf`, matching how every other Frank package's folder and root namespace already agree (`Frank.JsonHome` ⟷ `Frank.JsonHome`).

### Reference specifications

| Spec | Version | Media type |
|---|---|---|
| JSON-LD 1.1 | [W3C Recommendation](https://www.w3.org/TR/json-ld11/) | `application/ld+json` |
| JSON-LD 1.1 Processing Algorithms and API | [W3C Recommendation](https://www.w3.org/TR/json-ld11-api/) | — |
| RDF 1.1 Concepts and Abstract Syntax | [W3C Recommendation](https://www.w3.org/TR/rdf11-concepts/) | — |
| dotNetRDF | [3.5.x](https://dotnetrdf.org/) | — (library, not a wire spec) |

## Goals

1. Provide an `rdf { }` CE for hand-authoring RDF triples — one or more resources, cross-linked as needed — grounded in existing vocabularies (schema.org, FOAF, Dublin Core — e.g. `schema:sameAs` as a plain linking predicate, not an OWL reasoning construct).
2. Serialize that description to JSON-LD in **expanded form**.
3. Replace tic-tac-toe's hand-rolled `gameJsonLd` string with this package, proving it against a real consumer.
4. Establish a hand-authored target shape that later work (a second protocol sample, then eventually codegen) can build toward.

## Non-goals

- **Compact-form JSON-LD.** A prior failure pattern. Expanded form only, for now — see *Serialization*.
- **Multi-format content negotiation** (Turtle, RDF/XML). dotNetRDF's writers make this cheap to add later; nothing today needs it, so it isn't built.
- **SHACL, PROV-JSON, ALPS integration.** Separate sub-projects, in that order, each getting its own design.
- **An OWL reasoner / inference engine.** Not built because nothing here needs inference — not because OWL is unwelcome. `rdf { }` is vocabulary-agnostic: OWL terms (`owl:sameAs`, `rdfs:subClassOf`, ...) are just triples like any other, same as the `schema:sameAs` this design already uses. If a consumer wants to author OWL axioms through this CE, or plug a reasoner in over the resulting graph later, nothing here stops that.
- **Reflection-driven type→vocabulary mapping or build-time codegen** (the old `vocabulary { typeof<T> ... }` / resolver / lock-file pattern). Rejected per [[feedback_outside_in_before_codegen]].
- **Any dependency on `Frank` core.** Nothing here needs `HandlerDefinition`, `ApiSurface`, or the `resource` CE — see *Package shape*.

## Package shape

`src/Frank.Rdf/`, targeting `net8.0;net9.0;net10.0` (matching Frank core). **No `ProjectReference` to `Frank`** — this package builds and serializes a document; it has no opinion on how a handler returns the resulting string, so it doesn't need `HandlerDefinition` or the `resource` CE. One NuGet dependency:

```
PackageReference dotNetRdf.Core 3.5.1
```

```
RdfTypes.fs   Node, Literal, Value, Doc — all [<RequireQualifiedAccess>] where they're a DU
Rdf.fs        the `rdf { }` CE, Doc.toGraph, Doc.toJsonLd, Node.blank
```

Each `.fs` gets a matching `.fsi`, per `CLAUDE.md`. No middleware, no resolver, no generated code.

## The design

### Data model

Modeled directly on RDF's own triple shape, not JSON-LD's document shape — a graph is a flat set of `(subject, predicate, object)` statements about however many resources it needs, with no notion of one being "primary" or others being "embedded."

```fsharp
[<RequireQualifiedAccess>]
type Node =
    | Iri of string      // absolute IRI or a "prefix:local" CURIE
    | Blank of string     // minted by `Node.blank ()`; never authored literally

[<RequireQualifiedAccess>]
type Literal =
    | String of string
    | Int of int
    | Bool of bool
    | DateTime of System.DateTimeOffset

[<RequireQualifiedAccess>]
type Value =
    | Node of Node       // a reference: another resource, by IRI or blank node
    | Literal of Literal

type Doc =
    { Prefixes: (string * string) list
      Statements: (Node * string * Value) list }   // subject, predicate CURIE, object
```

`Node` is reused for both subject position and reference-valued objects, matching RDF's actual constraint that only IRIs and blank nodes can be subjects (literals can't); `Value` exists only to let a triple's object also be a literal, which nothing in subject position permits. `Node.blank : unit -> Node` mints a fresh `Blank` label for anonymous intermediate nodes (a PROV entity with no natural IRI, say) — nothing in this tic-tac-toe example needs one, since `#players` already has a stable fragment IRI.

Every DU here is `[<RequireQualifiedAccess>]`. Bare `Iri`, `String`, `Node` (the case) would either collide with each other (`Value.Node` the case vs. `Node` the type; `Literal.DateTime` vs. `System.DateTime`) or just be too generic to read unambiguously out of context. Qualification fixes that at the compiler level — `Node.Iri`, `Literal.String`, `Value.Node` are always unambiguous — without inventing an artificial prefix scheme (the earlier `RdfNode`/`VNode`/`LString` naming this replaced) that stutters everywhere and still didn't disambiguate cases from each other, only types.

That would make hand-authoring `Value.Literal(Literal.String "...")` for every field, though, which is the opposite of what this CE is for. The CE closes that gap — see *Authoring*.

Prefixes are declared inside the CE via the `ldContext` operation and resolved against dotNetRDF's `INamespaceMapper` when building the graph — CURIEs are just strings at the F# level; there is no compile-time checking of them (unlike ALPS's `rt`, which references descriptor *values*). That asymmetry is deliberate: ALPS vocabulary is a closed, authored set the compiler can check; RDF vocabulary is open by design — grounding external terms like `schema:Game` can't be validated except against the vocabulary's own definition, which Frank doesn't own.

The `ldContext` operation is named that, not `context` — Frank handlers universally bind `ctx: HttpContext`, and a bare `context` custom operation next to that convention would read ambiguously. `ldContext` names the JSON-LD concept it's standing in for (`@context`) even though, per the expanded-form decision above, nothing ever serializes a JSON `@context` object — the declared mappings only resolve CURIEs while building the graph.

`typ "schema:Game"` is sugar for `property` with the predicate hardcoded to the absolute `rdf:type` IRI (`http://www.w3.org/1999/02/22-rdf-syntax-ns#type`) — not resolved through `ldContext`. It's a universal RDF constant, not app vocabulary, so requiring an `ldContext "rdf" "..."` declaration just to use the CE's single most common operation would be pure ceremony. Calling `typ` more than once on the same subject asserts multiple `rdf:type` triples, which is exactly how RDF expresses multiple types — nothing special has to be built for that; it falls out of `typ` being ordinary repeated statement emission.

### Authoring

`property`, `typ`, and `about`'s subject parameter are overloaded to accept plain values directly — `string`, `int`, `bool`, `System.DateTimeOffset`, and `Node` — and wrap them into `Value.Literal`/`Value.Node` internally. This is where the CE earns its keep: the qualified constructors from *Data model* exist for precision in the underlying model, but an author only reaches for them explicitly when actually building a standalone `Node` reference — a `sameAs` target, or a node whose IRI gets reused across more than one `about` block. Everything else reads as plain F# values.

```fsharp
let gameLinkedData (gameUri: string) =
    let players = Node.Iri(gameUri + "#players")
    rdf {
        ldContext "schema" "https://schema.org/"

        about (Node.Iri gameUri) {
            typ "schema:Game"
            property "schema:name" "Tic-tac-toe"
            property "schema:description" "A two-player m,n,k (3,3,3) game..."
            property "schema:numberOfPlayers" players
            property "schema:sameAs" (Node.Iri "http://www.wikidata.org/entity/Q210339")
            property "schema:sameAs" (Node.Iri "http://dbpedia.org/resource/Tic-tac-toe")
        }

        about players {
            typ "schema:QuantitativeValue"
            property "schema:value" 2
        }
    }
```

`about subject { ... }` is sugar over repeated `triple subject predicate value` statements — like Turtle's `;` shorthand for not repeating the subject — not containment. `players` is declared and referenced by IRI wherever it's needed; its `about` block could equally have come first, or lived in a different `rdf { }` call merged in later. Nothing here assumes a single root resource, which is what `Frank.Provenance`'s multi-subject PROV graphs (activities, entities, agents, cross-linked) will need next. Raw `triple` is also available directly, without an `about` block, for one-off statements.

### `about` is a genuine nested CE, not string sugar

The brace syntax above isn't decorative — `about subject { ... }` really is a second computation expression, distinct from `rdf { }` itself, entered by calling a function that returns a builder instance:

```fsharp
type AboutBuilder(subject: Node) =
    member _.Yield(_) : (string * Value) list = []
    member _.Zero() : (string * Value) list = []
    member _.Delay(f) = f ()
    member _.Combine(a, b) : (string * Value) list = a @ b

    [<CustomOperation("typ")>]
    member _.Typ(stmts, curie: string) = stmts @ [ RdfType, Value.Node(Node.Iri curie) ]

    [<CustomOperation("property")>]
    member _.Property(stmts, predicate: string, value: string) =
        stmts @ [ predicate, Value.Literal(Literal.String value) ]
    // + overloads for int / bool / DateTimeOffset / Node

    member _.Run(stmts) : (Node * string * Value) list =
        stmts |> List.map (fun (p, v) -> subject, p, v)

let about subject = AboutBuilder(subject)
```

The outer `RdfBuilder` then has to absorb an `about` block's result — and bare `triple` calls — as ordinary statements with no `yield!` ceremony, which is why it needs `Combine`/`Delay`/`Yield`/`Zero` at all. This is a different CE shape from Frank core's own `ResourceBuilder`: `resource { }` only ever chains custom operations on itself (`Yield` seeds one `ResourceSpec`, and `get`/`post`/`rel` each take and return that same spec — no sub-blocks, so no `Combine` needed). `rdf { }` has to compose a *second* builder's output as if it were its own statement, which is exactly what `Combine`/`Delay` are for — the standard F# mechanism for nesting one CE inside another, the same shape `seq { for x in xs -> nested { ... } }`-style composition relies on. Worth building correctly since it's the same shape `Frank.Provenance`'s `activity { }`/`entity { }` blocks will need later, grouped under a `prov { }` outer CE the same way `about` groups under `rdf { }`.

### Serialization

`Doc.toGraph : Doc -> Graph` populates a `VDS.RDF.Graph`: registers declared prefixes on the namespace map, resolves each `Node.Iri` (absolute or CURIE) and mints one real blank node per distinct `Node.Blank` label via `graph.CreateBlankNode()`, then asserts one triple per statement. Flat fold, no recursion — nesting was the old model's problem, not this one's.

`Doc.toJsonLd : Doc -> string` runs dotNetRDF's `JsonLdWriter` over that graph and returns **expanded form**: an array with one node-object per distinct subject, no `@context`, every predicate and type fully expanded to its absolute IRI. This is a real, visible change from today's hand-rolled compact output in `Discovery.fs`; it trades human readability for not repeating the compact-form failure pattern, and expanded form is valid input to any conformant JSON-LD processor regardless. Revisiting compaction is future work, not blocked on anything here, but not attempted now.

### Integration point

`Discovery.fs`'s `gameJsonLd` string template is replaced by a call to `gameLinkedData` above, piped through `Doc.toJsonLd`. The handler sets `Content-Type: application/ld+json` itself, same as it does today — this package has no opinion on response plumbing.

## Error handling and edge cases

| Situation | Behaviour |
|---|---|
| `property`/`typ`/predicate CURIE uses an undeclared prefix | Throws at `toGraph` time with the unresolved CURIE named in the message. Fail fast rather than emit a garbage IRI. |
| Same prefix declared twice with different URIs | Throws — ambiguous mapping. |
| `Node.blank ()` handle reused across `about` blocks | Same underlying blank node — this is how you assert more statements about a node you don't have an IRI for, not an error. |
| `about` with no statements inside | Omitted — no triples asserted, so no trace of the subject appears in the graph. |
| A `Node.Iri` pointing at a non-existent/unchecked URI | Not validated — Frank has no way to know if it resolves, same as `sameAs` in the current hand-rolled version. |

## Implementation order

1. **`RdfTypes.fs`** — the model, plus unit tests for construction (no serialization yet).
2. **`Rdf.fs`: `AboutBuilder` + `RdfBuilder` + `Doc.toGraph`** — `AboutBuilder` first in isolation (`typ`/`property` overloads accumulating into a subject-bound statement list), then `RdfBuilder`'s `Combine`/`Delay`/`Yield`/`Zero` composing multiple `about` blocks and bare `triple` calls together, then prefix resolution and blank node minting in `toGraph`. Unit-tested by inspecting the resulting `Graph`'s triples directly (subject/predicate/object), not through JSON-LD.
3. **`Doc.toJsonLd`** — wire up `JsonLdWriter`, expanded-form output.
4. **tic-tac-toe integration** — replace `gameJsonLd`.

Each stage independently verifiable, matching how `Frank.JsonHome` was staged.

## Testing

New project `test/Frank.Rdf.Tests`.

- **Unit, no serialization**: CE construction — prefix declarations, overload resolution (literal and `Node` forms of `property`), multi-valued properties, blank node minting. Separately: `AboutBuilder` in isolation (statement list for one subject), and `RdfBuilder` composition specifically — two consecutive `about` blocks, an `about` block followed by a bare `triple`, and an empty `about` — to verify `Combine`/`Delay`/`Zero` merge correctly rather than dropping or duplicating statements. This is the part most likely to have a subtle bug, since it's the one piece of real CE machinery in the package rather than plain data transformation.
- **Graph-level**: `toGraph` output asserted by triple count/shape for a representative document (including the two-subject `QuantitativeValue` case), not by string comparison.
- **Round-trip check** (the JSON-LD equivalent of JsonHome's golden-document test): serialize with `Doc.toJsonLd`, then parse the result back into a graph with dotNetRDF's own JSON-LD reader, and assert the two graphs are isomorphic. This is the strongest available check that the expanded output means what the input graph meant — stronger than diffing against a hand-written expected string, which is exactly the kind of brittleness that made the compact-form attempt fragile.
- **tic-tac-toe regression**: the existing schema.org fields (`@type`, `name`, `description`, `numberOfPlayers`, `sameAs` to Wikidata/DBpedia) are all present in the new expanded output, via the round-trip graph rather than a literal string comparison against the old compact form.

## Future work (separate)

- **`Frank.Provenance`** and **`Frank.Validation`** (SHACL) both take a `ProjectReference` to this package and build their graphs through `Doc`/`Graph`/`Doc.toGraph` rather than re-wiring dotNetRDF from scratch — that's this package's foundation role. Neither reuses the `rdf { }` CE's vocabulary directly, though: PROV wants `activity`/`entity`/`agent`/`used`/`wasGeneratedBy`, SHACL wants `shape`/`targetClass`/`minCount`/`pattern` — different enough shapes (a SHACL shape isn't "a resource with properties," it's a constraint description) that each gets its own purpose-built CE producing a `Doc`, not `about`/`property` pressed into service for a vocabulary they weren't designed for.
- **`Frank.Provenance`** — PROV-JSON, motivated by the tic-tac-toe leaderboard query. Next sub-project.
- **`Frank.Validation`** (SHACL) — deferred behind Provenance.
- **ALPS-as-Discovery** — deferred furthest; joins the existing [ALPS/protocol design](2026-07-28-frank-alps-protocol-design.md).
- **A second protocol sample** with meaningfully different role dynamics, to pressure-test this shape before generalizing it.
- **Codegen** — only after the shape above has proven out across two or more real examples. Not started until then. See [[feedback_outside_in_before_codegen]].
- **Compact-form JSON-LD** — revisit once expanded form has been in real use and the earlier failure mode is understood well enough to name.
