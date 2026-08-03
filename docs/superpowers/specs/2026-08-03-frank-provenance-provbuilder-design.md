# Frank.Provenance: `ProvBuilder` CE

**Date**: 2026-08-03
**Branch**: `worktree-missing-provenance-ce`
**Status**: Draft — awaiting review

## Context

The [2026-08-02 Frank.Provenance design](2026-08-02-frank-provenance-design.md) shipped `Prov.fs` — a module of named PROV-O constructor/modifier functions over `Frank.Rdf.Description` (`Prov.activity`, `Prov.entity`, `Prov.agent`, and 7 relation functions: `wasGeneratedBy`, `wasAssociatedWith`, `used`, `startedAtTime`, `endedAtTime`, `wasDerivedFrom`, `specializationOf`) — but no CE sugar over it. Every other flat-accumulator type in this codebase that's meant as a hand-authoring surface gets one: `Frank.Rdf.Description` has `describe`/`DescribeBuilder`, `Frank.Alps.Descriptor` has `descriptor`/`DescriptorBuilder`. `Prov`'s own `Description` pipeline never got its CE.

This gap traces back to a prior, now-invalidated attempt: `feature/v7.3.2` (`[[project_v3_rollback]]`) built a `useProvenance`/`useProvenanceWith` CE — but that was a `WebHostBuilder` *extension* for wiring ASP.NET Core middleware/endpoint registration, a different shape entirely (needs `Microsoft.AspNetCore.App`), and it lived entirely on the rolled-back branch. The current, from-scratch `Frank.Provenance` package is deliberately ASP.NET-Core-free (see the 2026-08-02 design's Goals #1–2 and package structure), so that CE doesn't apply here even in spirit — and no record/`Description`-building CE analogous to `DescriptorBuilder` was ever built for this package, on any branch. This design adds that missing piece against the *current* package.

## Goals

1. CE sugar over `Prov.fs`'s existing functions, producing identical `Description` values to the equivalent `|>` chain — pure syntax, no new authoring logic.
2. Same shape discipline as `DescribeBuilder`/`DescriptorBuilder`: one accumulator (`Description`), no `Combine`/`Delay`, `Run` returns a plain value, `Yield` generic (`'a -> Description`) + `Zero` required (same CE-desugaring reasons documented on `DescribeBuilder`).
3. Demonstrate real value in the sample: express a PROV-O relation `ProvenanceRecord.toDoc` cannot produce today (`wasDerivedFrom`/`specializationOf`/`used`), since `IProvenanceStore.Append` only accepts a `ProvenanceRecord`, not an arbitrary `Description`.

## Non-goals

- **Replacing `ProvenanceRecord.toDoc`'s existing `|>` pipeline.** It stays as-is — no clear win from switching an already-working, already-tested internal pipeline to the CE form.
- **A `WebHostBuilder`/middleware CE.** Out of scope for this package per the 2026-08-02 design (Goals #1–2); would need `Microsoft.AspNetCore.App`, which this package deliberately doesn't reference.
- **CE support for switching "kind" (Activity/Entity/Agent) mid-block.** `Prov.activity`/`entity`/`agent` are constructors (`Node -> Description`), not modifiers (`Description -> Description`) — there is no existing pipeline function that rewrites an in-progress `Description`'s PROV class, so the CE doesn't invent one. See *Design*.

## Design

### Shape

`ProvBuilder` is a `[<Sealed>]` CE builder over `Description`, added as `src/Frank.Provenance/ProvBuilder.fsi`/`.fs`, compiled directly after `Prov.fsi`/`.fs` in `Frank.Provenance.fsproj`'s `<Compile>` order (it depends on `Prov`).

`Prov.activity`/`entity`/`agent` each already fully determine a `Description`'s starting PROV class via `describe id { typ (ProvClass.toIri ...) }` — there is no existing function that takes an in-progress `Description` and rewrites its class, the way `DescriptorBuilder.Semantic`/`Safe`/`Unsafe`/`Idempotent` rewrite `Descriptor.Type` (a plain field on an otherwise-unrelated accumulator). Inventing a "switch kind mid-block" operation would mean either discarding already-accumulated statements or merging in ways `Prov.fs` doesn't define — new behavior, not sugar. So instead of one entry point (`descriptor id { safe; ... }`'s shape), there are three, each producing a `ProvBuilder` already seeded via the matching `Prov` constructor:

```fsharp
val activity: id: Node -> ProvBuilder
val entity: id: Node -> ProvBuilder
val agent: id: Node -> ProvBuilder
```

Each entry point just forwards to the matching `Prov` constructor and wraps the result: `let activity (id: Node) = ProvBuilder(Prov.activity id)`, and likewise for `entity`/`agent`. `Yield`/`Zero` both return the builder's stored `initial` value — sound because `Description` is immutable, so there's nothing to recompute.

The 7 CustomOperations map one-to-one, same name, onto `Prov.fs`'s 7 modifier functions — each member body is exactly `d |> Prov.<name> <args>`:

```fsharp
[<Sealed>]
type ProvBuilder =
    new: initial: Description -> ProvBuilder
    member Yield: 'a -> Description
    member Zero: unit -> Description
    member Run: d: Description -> Description

    [<CustomOperation("wasGeneratedBy")>]
    member WasGeneratedBy: d: Description * activity: Node -> Description

    [<CustomOperation("wasAssociatedWith")>]
    member WasAssociatedWith: d: Description * agent: Node -> Description

    [<CustomOperation("used")>]
    member Used: d: Description * entity: Node -> Description

    [<CustomOperation("startedAtTime")>]
    member StartedAtTime: d: Description * t: DateTimeOffset -> Description

    [<CustomOperation("endedAtTime")>]
    member EndedAtTime: d: Description * t: DateTimeOffset -> Description

    [<CustomOperation("wasDerivedFrom")>]
    member WasDerivedFrom: d: Description * entity: Node -> Description

    [<CustomOperation("specializationOf")>]
    member SpecializationOf: d: Description * entity: Node -> Description
```

Usage:

```fsharp
activity myActivity {
    wasAssociatedWith agentNode
    startedAtTime t
    endedAtTime t
}

entity connectFour {
    wasDerivedFrom ticTacToe
}
```

`activity`/`entity`/`agent` as bare `[<AutoOpen>]` names don't collide with anything: `Prov.activity`/`Prov.entity`/`Prov.agent` are only ever called qualified (`Prov` is not `[<AutoOpen>]`), and no other bare `activity`/`entity`/`agent` exists in `Frank.Provenance` today.

### `ProvenanceRecord.toDoc`

Untouched. Its `Prov.activity record.Activity |> Prov.wasAssociatedWith ... |> Prov.startedAtTime ... |> Prov.endedAtTime ...` pipeline keeps working exactly as today; `ProvBuilder` is an alternative authoring surface, not a replacement.

### Sample: `GET /provenance/lineage`

Added to `sample/Frank.Provenance.Sample/Program.fs`. `IProvenanceStore.Append` only accepts `ProvenanceRecord`, and `ProvenanceRecord.toDoc` only ever emits `wasGeneratedBy`/`wasAssociatedWith`/`startedAtTime`/`endedAtTime` — there is no way to record `wasDerivedFrom` through the store today. This endpoint hand-authors (via `ProvBuilder`, not through `IProvenanceStore`) a `Description` asserting that Connect Four (`/games/2`) `wasDerivedFrom` Tic-tac-toe (`/games/1`), wraps it via `rdf { about ... }`, and serves it directly as JSON-LD via `Doc.toJsonLd` — a genuine case for the CE to exist, not a contrived one:

```fsharp
let private catalogLineage (baseUri: string) : Doc =
    rdf {
        about (entity (Node.Iri $"{baseUri}/games/2") { wasDerivedFrom (Node.Iri $"{baseUri}/games/1") })
    }

let private getCatalogLineage =
    fun (ctx: HttpContext) ->
        task {
            let baseUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
            ctx.Response.ContentType <- "application/ld+json"
            do! ctx.Response.WriteAsync(catalogLineage baseUri |> Doc.toJsonLd)
        }

let private lineageResource = resource "/provenance/lineage" { get getCatalogLineage }
```

Registered alongside the sample's existing resources in `webHost { ... }`.

## Testing

`test/Frank.Provenance.Tests/ProvBuilderTests.fs`:

- One test per entry point (`activity`/`entity`/`agent`) asserting the resulting `Description`'s `Statements` include the correct `rdf:type` triple.
- One test per CustomOperation asserting it appends the same statement `Prov`'s equivalent modifier function would.
- One test proving CE/`|>` equivalence directly: `activity a { wasAssociatedWith g; startedAtTime t1; endedAtTime t2 }` structurally equals `Prov.activity a |> Prov.wasAssociatedWith g |> Prov.startedAtTime t1 |> Prov.endedAtTime t2` — the same property `DescriptorBuilder`'s own doc comment asserts of itself.

## Sources

- [2026-08-02 Frank.Provenance design](2026-08-02-frank-provenance-design.md) — the package this CE extends.
- `src/Frank.Rdf/Rdf.fsi` — `DescribeBuilder`/`describe`, the pattern this CE follows, including the `Yield`-generic/`Zero`-required CE-desugaring rationale.
- `src/Frank.Alps/DescriptorBuilder.fsi`/`.fs` and `src/Frank.Alps/Descriptor.fsi`/`.fs` — the closest existing analogue (constructor functions + modifier functions, wrapped 1:1 by CustomOperations).
- Prior, non-applicable CE: `feature/v7.3.2`'s `useProvenance`/`useProvenanceWith` (`WebHostBuilder` extension, different shape, rolled back — reference only).
