# Frank.Provenance

**Date**: 2026-08-02
**Branch**: `provenance` (not yet created)
**Status**: Draft — awaiting review

## Context

[Frank.Rdf](2026-07-30-frank-rdf-design.md) (frank-fs/frank#483) has shipped: `rdf { }`/`describe { }` build a flat-triple `Doc`, serialized to expanded-form JSON-LD, proven against the tic-tac-toe sample's `/games/{id}` resource with real JSON/JSON-LD content negotiation. Its own "Future work" section named `Frank.Provenance` as the next sub-project, motivated by the tic-tac-toe leaderboard needing PROV-JSON, and explicitly anticipated that it would need "its own purpose-built CE producing a `Doc`, not `about`/`property` pressed into service for a vocabulary they weren't designed for" — this design follows that.

### Prior attempt

`feature/v7.3.2` (now invalidated, `[[project_v3_rollback]]`) carried a full `src/Frank.Provenance/` package. Two independent things are judged to be why it didn't stick, per its own design doc (`docs/superpowers/specs/2026-06-27-v732-provenance-vertical-design.md`, that branch) and the Frank.Rdf design's Context section:

1. **Codegen/reflection-driven vocabulary mapping.** A `vocabulary { }` CE mapped CLR types to IRIs, resolved by a build-time FCS codegen step (`GeneratedProvenanceResolver`). `[[feedback_outside_in_before_codegen]]` — codegen was the first thing built, so every wrong assumption about representation got baked into the generator.
2. **App-specific concerns leaking into a general package.** Tic-tac-toe's specific game-state lineage chain (`ProvenanceGraph.buildLineageGraph`, base64url-encoded state-entity keys, per-node route rendering) was built directly into `Frank.Provenance` itself, not kept in the consuming sample.

What's salvageable, mined and credited below rather than ported wholesale: the base PROV-O IRI constants (`ProvVocabulary.fs`), the general shape of a store contract (`Append` + queries), and the observation that `Frank.Validation`'s `Shapes.toShapesGraph : ShapeDecl list -> ShapesGraph` — hand-authored declarations totaled into a graph, no derivation — was never actually the problem; it's the same shape this design uses.

### Reference specifications

| Spec | Version | Media type |
|---|---|---|
| PROV-O (PROV Ontology) | [W3C Recommendation](https://www.w3.org/TR/prov-o/) | — (vocabulary, not a serialization) |
| PROV-O starting-point classes/properties used here | §4 of the above | — |
| RDF 1.1 / JSON-LD 1.1 | inherited from [Frank.Rdf](2026-07-30-frank-rdf-design.md) | `application/ld+json` |
| SPARQL 1.1 Query Language | [W3C Recommendation](https://www.w3.org/TR/sparql11-query/) | — |

## Goals

1. Record PROV-O provenance — who/what did something, to what, and when — as real RDF, built on Frank.Rdf's `Doc`/`Node`/`Value`/`Description`, not a parallel triple model.
2. A named, closed PROV vocabulary layer (`ProvClass`, `ProvRelation`, a `Prov` module of constructor functions) so authoring stays faithful to documented PROV-O concepts, never bare `describe { typ "..." }` calls with hand-typed IRI strings.
3. Two ways to produce a record — explicit (a handler authors one directly) and opt-in per-resource auto-capture (generic HTTP-derived fallback) — with an intentional, caller-chosen way to correlate them, not automatic deduplication.
4. A seam for auto-capture to get richer typing from `Frank.Alps` later, with zero dependency in either direction today.
5. A bounded, queryable, in-memory store, with query shapes that are themselves closed and provenance-scoped rather than an open door to arbitrary SPARQL.
6. Two HTTP exposures: query history for a resource, and inline per-request provenance via the content-negotiation pattern `Frank.Rdf` already proved out.

## Non-goals

- **Codegen or reflection-based type→IRI mapping**, in any form. See Context.
- **Automatic request/response body snapshotting.** Records carry only what's explicitly attached (via `Prov.enrich` or an explicit `ProvenanceRecord`'s `Properties`), never an auto-captured body — this is exactly where app-specific data leaked into the package last time.
- **App-specific lineage-chain graph rendering** (the old `buildLineageGraph`/state-entity-key machinery). `QueryByResource`-style results are enough for a consumer to build that itself; it does not belong in this package until it's proven across 2+ real consumers.
- **Hard dependency on `Frank.Alps`, `Frank.OpenApi`, or `Frank.Statecharts`.** The enrichment seam is a plain function type the application wires, not a project reference.
- **Durable storage.** v1 ships in-memory only. Tracked separately: frank-fs/frank#486.
- **An open/arbitrary SPARQL surface on `IProvenanceStore`.** SPARQL is the internal query mechanism; the public contract is a closed, named query vocabulary. See *Store*.
- **Promoting the SPARQL/named-graph store mechanism to `Frank.Rdf`.** It may generalize well, but `Frank.Provenance` is its only consumer today — same reasoning as not growing Frank core for the enrichment seam. Revisit if `Frank.Validation` or another package wants the same shape.

## The design

### Package structure

`src/Frank.Provenance/`, multi-targeting `net8.0;net9.0;net10.0` (matching Frank core and Frank.Rdf). Depends on `Frank`, `Frank.Rdf`, and `dotNetRdf.Core` (already a transitive dependency via Frank.Rdf; the SPARQL engine it needs — `LeviathanQueryProcessor`, `InMemoryDataset`, `SparqlQueryParser` — ships in `dotNetRdf.Core` with no additional package reference, already proven in `test/Frank.Rdf.Tests/QueryVerificationTests.fs`). No dependency on `Frank.Alps`, `Frank.OpenApi`, or `Frank.Statecharts`.

### Vocabulary layer

The PROV-O "starting-point" classes and the relations this package uses, as closed, struct discriminated unions — data-free cases, so `[<Struct>]` is a clear win (no heap allocation, no field-reservation trade-off, since there's no data on any case):

```fsharp
[<Struct; RequireQualifiedAccess>]
type ProvClass =
    | Activity
    | Entity
    | Agent

module ProvClass =
    val toIri: ProvClass -> string

[<Struct; RequireQualifiedAccess>]
type ProvRelation =
    | WasGeneratedBy
    | WasAssociatedWith
    | Used
    | StartedAtTime
    | EndedAtTime
    | WasDerivedFrom
    | SpecializationOf

module ProvRelation =
    val toIri: ProvRelation -> string
```

A `Prov` module of named constructor functions builds on these plus Frank.Rdf's `describe`/`property`/`typ`, so callers never write a raw PROV IRI string:

```fsharp
module Prov =
    val activity: id: Node -> Description
    val entity: id: Node -> Description
    val agent: id: Node -> Description
    val wasGeneratedBy: activity: Node -> Description -> Description
    val wasAssociatedWith: agent: Node -> Description -> Description
    val used: entity: Node -> Description -> Description
    val startedAtTime: DateTimeOffset -> Description -> Description
    val endedAtTime: DateTimeOffset -> Description -> Description
    val wasDerivedFrom: entity: Node -> Description -> Description
    val specializationOf: entity: Node -> Description -> Description
```

Non-PROV vocabulary (e.g. a domain type like `schema:OrderAction` alongside `prov:Activity`, per the old design's AT1) is mixed in with plain Frank.Rdf `describe`/`typ` calls next to `Prov.activity` — the `Prov` module covers exactly the closed PROV-O vocabulary above, nothing else.

### Record shape

```fsharp
type ProvenanceRecord =
    { Activity: Node
      Resource: Node
      Agent: Node
      StartedAt: DateTimeOffset
      EndedAt: DateTimeOffset
      ActivityType: Uri option
      Properties: (string * Value) list }
```

No `BodyAttributeValue`, no `ProvAgent`, no `ProvOClass`-as-loose-string-constant — `Node`/`Value` are Frank.Rdf's own types, reused directly. `ProvenanceRecord.toDoc : ProvenanceRecord -> Doc` (internal) is where the record's fields become PROV-O triples: `Activity` is typed via `Prov.activity` and, if `ActivityType` is `Some`, additionally `typ`d with that domain IRI (plain Frank.Rdf `describe`, not routed through `Prov` — this is the AT1-style "`@type` includes both `prov:Activity` and `schema:OrderAction`" case); `Resource` is typed via `Prov.entity` and connected to the Activity with `Prov.wasGeneratedBy`; `Agent` is typed via `Prov.agent` and connected with `Prov.wasAssociatedWith`; `StartedAt`/`EndedAt` become `Prov.startedAtTime`/`Prov.endedAtTime` on the Activity; `Properties` are attached to the Activity as-is.

### Recording

Two ways to produce a record, both always available:

- **Explicit**: a handler builds a `ProvenanceRecord` (or a `Description` via `Prov`'s constructors, for cases that don't fit the flat record shape) and calls `Prov.record : HttpContext -> ProvenanceRecord -> unit`, which resolves the registered `IProvenanceStore` from `ctx.RequestServices` and appends. No ambient state involved.
- **Auto-capture**: opt-in per resource (`resource "/games/{id}" { useProvenance }`). Middleware mints the request's Activity at the start of the pipeline and stashes a handle in `HttpContext.Items`. It records regardless of outcome — including error responses, consistent with treating this as a log, not a success-only audit trail. Default fields: HTTP method, route pattern as `Resource`, start/end timestamps, `ctx.User` as `Agent`. `ActivityType` comes from the configured `ActivityTypeResolver` (below) if present and it returns `Some`; otherwise the Activity stays untyped (`prov:Activity` with no domain type) — graceful degradation, never a dropped or failed record.
- **Correlating the two, intentionally**: `Prov.enrich : (string * Value) list -> HttpContext -> unit` adds properties to the current request's auto-captured Activity, if auto-capture is on for this resource. `Prov.record` always creates a separate, freestanding record. The call site decides which it wants — nothing attempts to detect or merge similar records after the fact.

### Enrichment seam

```fsharp
type ActivityTypeResolver = Microsoft.AspNetCore.Http.Endpoint -> Uri option
```

A plain function slot on auto-capture's config, operating on the standard ASP.NET Core `Endpoint` — not a Frank-specific type, and not a new interface in Frank core. This works because `HandlerDefinitionMetadata.toConventions` (`src/Frank/HandlerDefinition.fs`) already does `b.Metadata.Add m` for every `HandlerDefinition.Metadata` entry, so anything a future `Frank.Alps` attaches via `HandlerDefinition.addMetadata` when binding a transition is retrievable at request time via `ctx.GetEndpoint().Metadata.GetMetadata<'T>()` — standard ASP.NET Core plumbing, nothing new to build. `Frank.Alps` would eventually expose a plain function of this exact shape; the application's composition root wires it into `Frank.Provenance`'s config if it wants that. Zero dependency either direction, and no commitment made today about `Frank.Alps`'s own metadata shape.

### Store

```fsharp
[<RequireQualifiedAccess>]
type ProvenanceQuery =
    | ByResource of resourceIri: string
    | ByAgent of agentIri: string
    | ByActivityId of activityIri: string

type SparqlQueryResult =
    | Bindings of SparqlResultSet   // SELECT / ASK
    | Graph of IGraph                // CONSTRUCT / DESCRIBE

type IProvenanceStore =
    abstract Append: ProvenanceRecord -> unit
    abstract Query: ProvenanceQuery -> SparqlQueryResult
```

`ProvenanceQuery` is the public, closed vocabulary of query shapes this package recognizes as provenance-meaningful — a caller cannot ask for arbitrary SPARQL, only a named shape. SPARQL is the internal mechanism, not the public contract: each `ProvenanceQuery` case maps to a pre-built, parameterized `SparqlQuery` (via `SparqlParameterizedString`, avoiding string-concatenation injection into the query text) run against the store's data. Adding a new provenance-meaningful query shape later means adding a DU case — F# flags every non-exhaustive match at that point — not adding a new interface method, and never accepting open query text from a caller.

`MailboxProcessorProvenanceStore` — the v1, in-memory implementation, proof-of-concept-appropriate for this package's current scope (`[[project_actor_model_trajectory]]`) — holds a dotNetRDF `TripleStore`. Each `Append`ed record's `ProvenanceRecord.toDoc |> Doc.toGraph` output becomes one named graph in it; `Query` runs `LeviathanQueryProcessor` over an `InMemoryDataset` wrapping the whole store, the same mechanism already proven in `test/Frank.Rdf.Tests/QueryVerificationTests.fs`. Bounded eviction (`ProvenanceStoreConfig { MaxRecords; EvictionBatchSize }`) removes the oldest named graph(s) — a native `TripleStore` operation, no hand-rolled resource/agent/time indexes needed. The mailbox serializes all access, so eviction and an in-flight query can't race.

### HTTP surface

- **Sidecar query endpoint**: `GET /provenance?resource=<iri>`, running `ProvenanceQuery.ByResource` and returning the resulting graph as expanded-form JSON-LD, matching Frank.Rdf's own output convention.
- **Inline, content-negotiated**: a request to the resource itself with `Accept: application/ld+json; profile="http://www.w3.org/ns/prov"` gets that request's own record back, negotiated the same way `/games/{id}` already does JSON vs. JSON-LD, advertised via a `Link: <...>; rel="describedby"` header using the existing `WebLink`/resource-scoped link mechanism (`src/Frank/WebLink.fs`).

## Error handling and edge cases

| Situation | Behaviour |
|---|---|
| `useProvenance` enabled but no `IProvenanceStore` registered | Fails at startup (registration-time), not on the first request. |
| `ActivityTypeResolver` returns `None`, or none configured | Untyped `prov:Activity` — recorded, never dropped or errored. |
| Handler throws / response is 4xx or 5xx | Auto-capture still records the request — this is a log, not a success-only trail. |
| `Prov.enrich` called when auto-capture isn't enabled for the resource | No-op — there's no ambient Activity to enrich. Not an error; the call site chose an auto-capture-dependent operation on a resource that doesn't have it. |
| `Prov.record` and auto-capture both fire for the same request | Two separate records, as chosen at each call site — see *Recording*. |
| Query against an empty store | Empty result (`Bindings` with zero rows, or an empty `Graph`), not an error. |
| Store at `MaxRecords` | Oldest named graph(s) evicted before/as part of the next `Append`. |

## Testing

Mirrors `Frank.Rdf`'s established pattern (`test/Frank.Rdf.Tests`):

- **Vocabulary layer**: `Prov.activity`/`Prov.wasGeneratedBy`/etc. produce the correct triples for a representative record — unit-tested by inspecting the resulting `Doc`/`Graph`, not string comparison.
- **Round-trip**: `ProvenanceRecord.toDoc |> Doc.toJsonLd`, parsed back via dotNetRDF's own JSON-LD reader, asserted isomorphic — same pattern as Frank.Rdf's own round-trip tests.
- **Query shapes**: one test per `ProvenanceQuery` case, in the same style as `QueryVerificationTests.fs` — assert the pre-built SPARQL for `ByResource`/`ByAgent`/`ByActivityId` actually retrieves the right records and nothing else.
- **Store**: `Append` + eviction at `MaxRecords`, oldest-first; concurrent `Append`/`Query` via the mailbox (no torn reads).
- **Auto-capture**: `useProvenance` on a `TestHost`-backed resource records HTTP method/route/status/timestamps/agent by default; records on error responses too; `ActivityTypeResolver` present vs. absent (typed vs. untyped Activity).
- **Correlation**: `Prov.enrich` lands on the same Activity auto-capture created for that request; `Prov.record` in the same handler produces a second, separate record; `Prov.enrich` with auto-capture off is a no-op, not an error.
- **HTTP surface**: sidecar endpoint returns the right graph for `?resource=`; inline negotiation matches Frank.Rdf's existing JSON vs. JSON-LD test shape; `Link: rel="describedby"` header present.

## Future work (separate)

- **`Frank.Alps` integration** — once ALPS's own design lands, it exposes a plain `Endpoint -> Uri option` function matching `ActivityTypeResolver`, wired by the application. Nothing to build here until then.
- **"Current state" queries for ALPS** — frank-fs/frank#471's open question "where does resource state enter" may be answerable as a derived read over this store (e.g. a `ProvenanceQuery.Latest of resourceIri` case returning the most recent record) rather than a separate state-tracking mechanism. Symmetric to the `ActivityTypeResolver` seam: ALPS would define its own `CurrentStateResolver = resourceIri: string -> Uri option` seam, optionally backed by a query against this store, wired by the application — not a dependency either direction. See the journal-reframing and seam-shape discussion on that issue (posted 2026-08-02) for the full reasoning and what's honestly lost versus the original Harel-statechart journal concept. Not added speculatively here — wait for ALPS's own design (in progress in a parallel session) to confirm the actual shape needed.
- **Durable store** — frank-fs/frank#486 (SQLite-as-substrate vs. a persistence hook on `MailboxProcessorProvenanceStore`).
- **`[<Struct>]` for existing Frank.Rdf/Frank.JsonHome types** — frank-fs/frank#485, unrelated to this package's own (already-struct) `ProvClass`/`ProvRelation`.
- **Promoting the named-graph/SPARQL store mechanism to `Frank.Rdf`** — only once a second real consumer (e.g. `Frank.Validation`) wants the same shape.
- **App-specific lineage-chain rendering** — stays out of this package; a consuming sample (tic-tac-toe leaderboard) builds it from `ProvenanceQuery.ByResource` results if/when needed.

## Sources

- W3C PROV-O: https://www.w3.org/TR/prov-o/
- [Frank.Rdf design](2026-07-30-frank-rdf-design.md) — foundation this package builds on.
- Prior attempt (reference only, not a starting point): `feature/v7.3.2`'s `src/Frank.Provenance/`, `docs/superpowers/specs/2026-06-27-v732-provenance-vertical-design.md`.
- SPARQL mechanism already proven in this repo: `test/Frank.Rdf.Tests/QueryVerificationTests.fs`.
- `HandlerDefinition.Metadata` → `Endpoint.Metadata` flow: `src/Frank/HandlerDefinition.fs`.
- `WebLink`/resource-scoped Link mechanism: `src/Frank/WebLink.fs`.
- frank-fs/frank#485 (struct evaluation follow-up), frank-fs/frank#486 (durable store follow-up).
