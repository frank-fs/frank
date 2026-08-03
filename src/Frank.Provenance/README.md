# Frank.Provenance

An F# library for recording and querying [PROV-O](https://www.w3.org/TR/prov-o/) provenance — who (`Agent`) did what (`Activity`), producing which resource (`Resource`/`Entity`), and when. Built directly on `Frank.Rdf`'s `Doc`/`Description` model rather than a parallel triple representation, so every provenance record is, underneath, just RDF. Zero ASP.NET Core dependency, same as `Frank.Rdf` itself — no `ProjectReference` to [Frank](https://github.com/frank-fs/frank), no `FrameworkReference` to `Microsoft.AspNetCore.App`.

## Features

- **Closed PROV-O vocabulary**: `ProvClass` (`Activity` / `Entity` / `Agent`) and `ProvRelation` (`WasGeneratedBy` / `WasAssociatedWith` / `Used` / `StartedAtTime` / `EndedAtTime` / `WasDerivedFrom` / `SpecializationOf`) are `[<Struct; RequireQualifiedAccess>]` unions, each with a `toIri` function. Callers never write a raw PROV IRI string
- **`Prov` module**: named constructor functions (`Prov.activity`, `Prov.entity`, `Prov.agent`, `Prov.wasGeneratedBy`, `Prov.wasAssociatedWith`, `Prov.used`, `Prov.startedAtTime`, `Prov.endedAtTime`, `Prov.wasDerivedFrom`, `Prov.specializationOf`) that build directly on `Frank.Rdf.Description` for hand-composing PROV-O statements
- **`ProvBuilder` computation expression**: `activity`/`entity`/`agent { }` mirror the `Prov` module's constructor/modifier functions one-for-one -- plain `|>` combinators and the CE produce structurally identical `Description` values
- **`ProvenanceRecord` + `toDoc`**: a single PROV-O record (`Activity`, `Resource`, `Agent`, `StartedAt`/`EndedAt`, an optional domain `ActivityType`, and arbitrary `Properties`), projected into a `Doc` via `ProvenanceRecord.toDoc`
- **Closed `ProvenanceQuery` vocabulary**: `ByResource` / `ByAgent` / `ByActivityId` are the only recognized query shapes. There is no public API accepting a raw SPARQL query or query string — SPARQL is purely an internal implementation detail (`ProvenanceStore.toSparqlQuery` is `internal`); adding a new provenance-meaningful query shape means adding a case to `ProvenanceQuery`, not widening the surface to open query text
- **`MailboxProcessorProvenanceStore`**: the v1, in-memory `IProvenanceStore` implementation — one dotNetRDF `TripleStore` holding one named graph per appended record, queried via SPARQL over the store's union graph, with bounded eviction of the oldest records once `ProvenanceStoreConfig.MaxRecords` is exceeded (`EvictionBatchSize` is clamped so the record just appended is never evicted). A `MailboxProcessor` serializes concurrent `Append`/`Query` calls, and a malformed record never kills the mailbox loop — later `Append`/`Query` calls still succeed

## Installation

```bash
dotnet add package Frank.Provenance
```

## Quick Start

```fsharp
open System
open Microsoft.Extensions.Logging.Abstractions
open Frank.Rdf
open Frank.Provenance

let record: ProvenanceRecord =
    { Activity = Node.Iri "https://example.org/activities/1"
      Resource = Node.Iri "https://example.org/games/1"
      Agent = Node.Iri "https://example.org/users/42"
      StartedAt = DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)
      EndedAt = DateTimeOffset(2026, 8, 2, 12, 0, 1, TimeSpan.Zero)
      ActivityType = None
      Properties = [] }

let store =
    new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)
    :> IProvenanceStore

store.Append(record)

// SPARQL never leaks out to the caller -- Query takes a closed ProvenanceQuery case and
// returns a plain VDS.RDF.IGraph (ByResource/ByAgent/ByActivityId all compile to CONSTRUCT/
// DESCRIBE queries under the hood, so the result is always a Graph, never Bindings).
match store.Query(ProvenanceQuery.ByResource "https://example.org/games/1") with
| SparqlQueryResult.Graph g -> printfn "%d triples about this resource" g.Triples.Count
| SparqlQueryResult.Bindings _ -> ()
```

`ProvenanceRecord.toDoc` is also usable on its own, independent of any store, whenever you just want the `Doc` (for example, to merge it into another document via `Doc.merge`, or to serialize it directly with `Doc.toJsonLd`):

```fsharp
let doc = ProvenanceRecord.toDoc record
let json = Doc.toJsonLd doc
```

For finer-grained control than `ProvenanceRecord` gives you, build a `Description` directly from the `Prov` module's constructors:

```fsharp
let activityDescription =
    Prov.activity (Node.Iri "https://example.org/activities/1")
    |> Prov.wasAssociatedWith (Node.Iri "https://example.org/users/42")
    |> Prov.startedAtTime (DateTimeOffset.UtcNow)
```

Or the same thing via the `ProvBuilder` computation expression, which produces a structurally identical `Description`:

```fsharp
let activityDescription =
    activity (Node.Iri "https://example.org/activities/1") {
        wasAssociatedWith (Node.Iri "https://example.org/users/42")
        startedAtTime DateTimeOffset.UtcNow
    }
```

## Scope

This package is the core, HTTP-independent half of provenance support: recording (`ProvenanceRecord`), storage/querying (`IProvenanceStore`, `MailboxProcessorProvenanceStore`), and the RDF/PROV-O modeling underneath. `HttpContext`-touching pieces — auto-capture middleware that records provenance from an in-flight request, or HTTP endpoints that expose `ProvenanceQuery` over the wire — are follow-on work, not yet part of this package.

## Related Projects

- [Frank](https://github.com/frank-fs/frank) — F# web framework
- `Frank.Rdf` — the RDF/JSON-LD library this package is built on
- [PROV-O](https://www.w3.org/TR/prov-o/) — the W3C provenance ontology this package implements
