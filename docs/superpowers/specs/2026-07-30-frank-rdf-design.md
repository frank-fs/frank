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
RdfTypes.fs   Node, Literal, Value, Doc, Description — all [<RequireQualifiedAccess>] where they're a DU
Rdf.fs        the `rdf { }` CE, Doc.toGraph, Doc.toJsonLd, Doc.merge, Node.blank
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

type Description =
    { Subject: Node
      Statements: (string * Value) list }   // predicate CURIE, object -- subject implied
```

`Node` is reused for both subject position and reference-valued objects, matching RDF's actual constraint that only IRIs and blank nodes can be subjects (literals can't); `Value` exists only to let a triple's object also be a literal, which nothing in subject position permits. `Node.blank : unit -> Node` mints a fresh `Blank` label for anonymous intermediate nodes (a PROV entity with no natural IRI, say) — nothing in this tic-tac-toe example needs one, since `#players` already has a stable fragment IRI.

The label is a fresh `System.Guid`, not a per-`Doc` counter (`"b0"`, `"b1"`, ...). A counter would make two *independently built* `Doc`s collide the moment both happen to mint their first blank node — silently unifying two unrelated anonymous entities into one node wherever those docs are later merged (see *Merging documents*). GUIDs make that structurally impossible, for the same implementation cost as a counter.

Every DU here is `[<RequireQualifiedAccess>]`. Bare `Iri`, `String`, `Node` (the case) would either collide with each other (`Value.Node` the case vs. `Node` the type; `Literal.DateTime` vs. `System.DateTime`) or just be too generic to read unambiguously out of context. Qualification fixes that at the compiler level — `Node.Iri`, `Literal.String`, `Value.Node` are always unambiguous — without inventing an artificial prefix scheme (the earlier `RdfNode`/`VNode`/`LString` naming this replaced) that stutters everywhere and still didn't disambiguate cases from each other, only types.

That would make hand-authoring `Value.Literal(Literal.String "...")` for every field, though, which is the opposite of what this CE is for. The CE closes that gap — see *Authoring*.

Prefixes are declared inside the CE via the `prefix` operation and resolved against dotNetRDF's `INamespaceMapper` when building the graph — CURIEs are just strings at the F# level; there is no compile-time checking of them (unlike ALPS's `rt`, which references descriptor *values*). That asymmetry is deliberate: ALPS vocabulary is a closed, authored set the compiler can check; RDF vocabulary is open by design — grounding external terms like `schema:Game` can't be validated except against the vocabulary's own definition, which Frank doesn't own.

The operation is named `prefix`, not `context` or `ldContext` (an earlier draft's name for it, changed during review) — `prefix` is Turtle's and SPARQL's own term for exactly this concept (`@prefix`/`PREFIX`), consistent with the rest of the CE's Turtle-flavored vocabulary (`describe`, `about`, `triple`), where `ldContext` was the one JSON-LD-specific name left over from when the package was still called `Frank.LinkedData`. It also sidesteps `context` colliding with `ctx: HttpContext`, which every Frank handler binds — a real, verified collision, unlike the RDF-vocabulary-collision concerns raised earlier in this design that turned out to be unfounded (see `Node`/`Literal`/`Value` above). Routing prefix declarations through JSON-LD's `@context` was never accurate anyway: nothing here ever serializes a JSON `@context` object, since output is expanded-form only — the declared mappings only resolve CURIEs while building the graph.

`typ "schema:Game"` is sugar for `property` with the predicate hardcoded to the absolute `rdf:type` IRI (`http://www.w3.org/1999/02/22-rdf-syntax-ns#type`) — not resolved through `prefix`. It's a universal RDF constant, not app vocabulary, so requiring a `prefix "rdf" "..."` declaration just to use the CE's single most common operation would be pure ceremony. Calling `typ` more than once on the same subject asserts multiple `rdf:type` triples, which is exactly how RDF expresses multiple types — nothing special has to be built for that; it falls out of `typ` being ordinary repeated statement emission.

### Authoring, and how `describe`/`about` mirror `handler`/`get`

Frank core already has a two-CE composition pattern, used twice (`handler { }` feeding `resource { }`'s `get`/`post`, and `resource { }` feeding `webHost { }`'s `resource` operation): the inner CE is fully self-contained — `Yield` seeds one accumulator, every custom operation threads that same accumulator, `Run` validates and returns a plain value — and the outer CE's custom operation just takes that **already-evaluated value** as an ordinary parameter. `get (handler { handle myHandler })` is `get` receiving a plain `HandlerDefinition`; nothing implicit is threaded between the two builders. No `Combine`, no `Delay`, needed by either side.

`describe`/`about` follow that exact shape instead of the `Combine`/`Delay` machinery an earlier draft of this design used (documented in git history — self-corrected during review; kept here because the same mistake would have recurred in `Frank.Provenance`, which needs the identical shape for `activity { }`/`entity { }` blocks grouped under a `prov { }` outer CE):

```fsharp
[<Sealed>]
type DescribeBuilder(subject: Node) =
    member _.Yield(_) : Description = { Subject = subject; Statements = [] }
    member _.Run(d: Description) = d

    [<CustomOperation("typ")>]
    member _.Typ(d, curie: string) =
        { d with Statements = d.Statements @ [ RdfTypeIri, Value.Node(Node.Iri curie) ] }

    [<CustomOperation("property")>]
    member _.Property(d, predicate: string, value: string) =
        { d with Statements = d.Statements @ [ predicate, Value.Literal(Literal.String value) ] }
    // + overloads for int / bool / DateTimeOffset / Node

let describe subject = DescribeBuilder(subject)
```

`describe (Node.Iri gameUri) { typ "schema:Game"; property ... }` runs to completion as an ordinary sub-expression, exactly like `handler { handle ... }` does — and produces a plain `Description`. `rdf { }`'s `about` custom operation takes that value directly, the same way `get` takes a `HandlerDefinition`:

```fsharp
[<CustomOperation("about")>]
member _.About(doc: Doc, d: Description) : Doc =
    { doc with
        Statements = doc.Statements @ (d.Statements |> List.map (fun (p, v) -> d.Subject, p, v)) }
```

`property`, `typ`, and `about`'s value parameter are overloaded to accept plain values directly — `string`, `int`, `bool`, `System.DateTimeOffset`, and `Node` — wrapping into `Value.Literal`/`Value.Node` internally. This is where the CE earns its keep: the qualified constructors from *Data model* exist for precision in the underlying model, but an author only reaches for them explicitly when building a standalone `Node` reference — a `sameAs` target, or a node whose IRI gets reused across more than one `describe` block. Everything else reads as plain F# values.

```fsharp
let gameLinkedData (gameUri: string) =
    let players = Node.Iri(gameUri + "#players")
    rdf {
        prefix "schema" "https://schema.org/"

        about (describe (Node.Iri gameUri) {
            typ "schema:Game"
            property "schema:name" "Tic-tac-toe"
            property "schema:description" "A two-player m,n,k (3,3,3) game..."
            property "schema:numberOfPlayers" players
            property "schema:sameAs" (Node.Iri "http://www.wikidata.org/entity/Q210339")
            property "schema:sameAs" (Node.Iri "http://dbpedia.org/resource/Tic-tac-toe")
        })

        about (describe players {
            typ "schema:QuantitativeValue"
            property "schema:value" 2
        })
    }
```

`about (describe subject { ... })` is sugar over repeated `triple subject predicate value` statements — like Turtle's `;` shorthand for not repeating the subject — not containment. `players` is declared and referenced by IRI wherever it's needed; its `describe` block could equally have come first, or lived in a different `rdf { }` call merged in later. Nothing here assumes a single root resource, which is what `Frank.Provenance`'s multi-subject PROV graphs (activities, entities, agents, cross-linked) will need next. Raw `triple subject predicate value` is also available directly on `rdf { }`, without a `describe`/`about` pair, for one-off statements — mirroring how `resource { }` also has plain operations (`name`, `docs`) alongside `get`/`post`.

`rdf { }` itself stays exactly as simple as `ResourceBuilder`: `Yield` seeds `Doc.Empty` (empty prefixes and statements), and `prefix`/`about`/`triple` each take and return that same `Doc` — no `Combine`, no `Delay`, here either.

### Serialization

`Doc.toGraph : Doc -> Graph` populates a `VDS.RDF.Graph`: registers declared prefixes on the namespace map, resolves each `Node.Iri` (absolute or CURIE) and mints one real blank node per distinct `Node.Blank` label via `graph.CreateBlankNode()`, then asserts one triple per statement. Flat fold, no recursion — nesting was the old model's problem, not this one's.

`Doc.toJsonLd : Doc -> string` runs dotNetRDF's `JsonLdWriter` over that graph and returns **expanded form**: an array with one node-object per distinct subject, no `@context`, every predicate and type fully expanded to its absolute IRI. This is a real, visible change from today's hand-rolled compact output in `Discovery.fs`; it trades human readability for not repeating the compact-form failure pattern, and expanded form is valid input to any conformant JSON-LD processor regardless. Revisiting compaction is future work, not blocked on anything here, but not attempted now.

### Merging documents

`Doc.merge : Doc -> Doc -> Doc` combines two independently-built documents — concatenate `Prefixes`, concatenate `Statements`, nothing more:

```fsharp
module Doc =
    let merge (a: Doc) (b: Doc) : Doc =
        { Prefixes = a.Prefixes @ b.Prefixes
          Statements = a.Statements @ b.Statements }
```

That it's this simple isn't an oversight — the two things that look like they'd need special-case handling are already covered elsewhere. A prefix declared with two different URIs across the two docs is caught by the existing conflict check in *Data model*, which runs at `toGraph` time over the merged `Prefixes` list regardless of provenance — merge needs no validation of its own. An identical statement asserted by both docs is harmless: `VDS.RDF.Graph.Assert` has set semantics, so a duplicate tuple in `Doc.Statements` collapses to one triple in the built graph, not two. The one thing that *does* need a real decision is blank node identity across documents built independently of each other, which is why `Node.blank` mints a GUID rather than a per-`Doc` counter (see *Data model*) — with that in place, two docs that each minted their own blank nodes can never collide when merged.

`rdf { }` exposes this as `include`, taking an already-built `Doc` the same way `about` takes an already-built `Description`:

```fsharp
[<CustomOperation("include")>]
member _.Include(doc: Doc, other: Doc) : Doc = Doc.merge doc other
```

```fsharp
rdf {
    prefix "schema" "https://schema.org/"
    about (describe (Node.Iri gameUri) { typ "schema:Game"; property "schema:name" "Tic-tac-toe" })
    include otherDoc   // e.g. facts about the same gameUri built by a different function
}
```

### Integration point

`Discovery.fs`'s `gameJsonLd` string template is replaced by a call to `gameLinkedData` above, piped through `Doc.toJsonLd`. The handler sets `Content-Type: application/ld+json` itself, same as it does today — this package has no opinion on response plumbing.

### Serving it over HTTP: an independent resource, not content negotiation

Not decided anywhere earlier in this design, and worth being explicit about now rather than leaving it implicit: the JSON-LD representation lives at its **own resource** (e.g. `/games/{id}/linked-data`), advertised from the main resource via a `Link: rel="alternate"` header — not served from the same URL as the main representation via `Accept`-based content negotiation.

Two reasons. First, precedent: every existing "here's another machine-readable representation" case in Frank — `Frank.JsonHome`'s `home` link, `Frank.OpenApi`'s `service-desc` link, the deferred ALPS `profile` link — already uses exactly this shape: a separate resource, advertised by a `Link` header appended via `Response.OnStarting`. Frank.JsonHome's own `linkHeaderMiddleware` (`JsonHome.fs`) is the concrete pattern to copy: build the RFC 8288 field value once, append (not assign) it in `OnStarting` so it survives exception-handler-regenerated responses.

Second, `Frank`'s `ContentNegotiation.fs` doesn't actually fit this job: it negotiates among registered ASP.NET Core MVC `IOutputFormatter`s for a strongly-typed `.NET` object (`OutputFormatterSelector.SelectFormatter`), not between two already-built strings based on `Accept`. Routing JSON-LD through it would mean writing and registering a custom `TextOutputFormatter` — ceremony nothing else in Frank takes on for this.

This needs no new code in `Frank.Rdf` or Frank core — a second `resource { get ... }` block calling `Doc.toJsonLd`, plus a few lines of `Link`-header-appending middleware copied from `Frank.JsonHome`'s pattern, both live entirely in the consuming app. It's called out here so the tic-tac-toe follow-on plan (see the implementation plan's "Out of scope" section) doesn't have to rediscover it.

## Error handling and edge cases

| Situation | Behaviour |
|---|---|
| `property`/`typ`/predicate CURIE uses an undeclared prefix | Throws at `toGraph` time with the unresolved CURIE named in the message. Fail fast rather than emit a garbage IRI. |
| Same prefix declared twice with different URIs | Throws — ambiguous mapping. |
| `Node.blank ()` handle reused across `describe` blocks | Same underlying blank node — this is how you assert more statements about a node you don't have an IRI for, not an error. |
| `describe` with no `typ`/`property` calls, passed to `about` | Omitted — no triples asserted, so no trace of the subject appears in the graph. |
| A `Node.Iri` pointing at a non-existent/unchecked URI | Not validated — Frank has no way to know if it resolves, same as `sameAs` in the current hand-rolled version. |

## Implementation order

1. **`RdfTypes.fs`** — the model, plus unit tests for construction (no serialization yet).
2. **`Rdf.fs`: `DescribeBuilder` + `RdfBuilder` + `Doc.toGraph`** — `DescribeBuilder` first in isolation (`typ`/`property` overloads accumulating into a `Description`), then `RdfBuilder`'s `prefix`/`about`/`triple` operations threading `Doc` the same way `ResourceBuilder`'s operations thread `ResourceSpec`, then prefix resolution and blank node minting in `toGraph`. Unit-tested by inspecting the resulting `Graph`'s triples directly (subject/predicate/object), not through JSON-LD.
3. **`Doc.toJsonLd`** — wire up `JsonLdWriter`, expanded-form output.
4. **`Doc.merge` + `include`** — trivial once `Node.blank` is GUID-based from step 1; covered by tests proving cross-document blank nodes don't collide.
5. **tic-tac-toe integration** — replace `gameJsonLd`.

Each stage independently verifiable, matching how `Frank.JsonHome` was staged.

## Testing

New project `test/Frank.Rdf.Tests`.

- **Unit, no serialization**: `DescribeBuilder` in isolation — `typ`/`property` overload resolution (literal and `Node` forms), multi-valued properties, producing a plain `Description`. `RdfBuilder` separately — `prefix` accumulation, `about` absorbing a `Description`, bare `triple`, two consecutive `about` calls, an empty `describe` block. No `Combine`/`Delay` cases to test, since neither builder has any — that's the point of matching `handler`/`get`'s shape instead of inventing composition machinery.
- **Graph-level**: `toGraph` output asserted by triple count/shape for a representative document (including the two-subject `QuantitativeValue` case), not by string comparison.
- **Round-trip check** (the JSON-LD equivalent of JsonHome's golden-document test): serialize with `Doc.toJsonLd`, then parse the result back into a graph with dotNetRDF's own JSON-LD reader, and assert the two graphs are isomorphic. This is the strongest available check that the expanded output means what the input graph meant — stronger than diffing against a hand-written expected string, which is exactly the kind of brittleness that made the compact-form attempt fragile.
- **Merge**: two independently-built `Doc`s (built by two separate `rdf { }` calls, each minting its own blank node) combined via `Doc.merge`/`include`, asserting the merged graph contains the union of both and that the two blank nodes remain distinct — the concrete proof that GUID-based `Node.blank` actually prevents the collision described in *Data model*. A second case merging two docs that legitimately share a prefix (same name, same URI) to confirm that's a no-op, not a spurious conflict.
- **tic-tac-toe regression**: the existing schema.org fields (`@type`, `name`, `description`, `numberOfPlayers`, `sameAs` to Wikidata/DBpedia) are all present in the new expanded output, via the round-trip graph rather than a literal string comparison against the old compact form.

## Future work (separate)

- **`Frank.Provenance`** and **`Frank.Validation`** (SHACL) both take a `ProjectReference` to this package and build their graphs through `Doc`/`Graph`/`Doc.toGraph` rather than re-wiring dotNetRDF from scratch — that's this package's foundation role. Neither reuses the `rdf { }` CE's vocabulary directly, though: PROV wants `activity`/`entity`/`agent`/`used`/`wasGeneratedBy`, SHACL wants `shape`/`targetClass`/`minCount`/`pattern` — different enough shapes (a SHACL shape isn't "a resource with properties," it's a constraint description) that each gets its own purpose-built CE producing a `Doc`, not `about`/`property` pressed into service for a vocabulary they weren't designed for.
- **`Frank.Provenance`** — PROV-JSON, motivated by the tic-tac-toe leaderboard query. Next sub-project.
- **`Frank.Validation`** (SHACL) — deferred behind Provenance.
- **ALPS-as-Discovery** — deferred furthest; joins the existing [ALPS/protocol design](2026-07-28-frank-alps-protocol-design.md).
- **A second protocol sample** with meaningfully different role dynamics, to pressure-test this shape before generalizing it.
- **Codegen** — only after the shape above has proven out across two or more real examples. Not started until then. See [[feedback_outside_in_before_codegen]].
- **Compact-form JSON-LD** — revisit once expanded form has been in real use and the earlier failure mode is understood well enough to name.
