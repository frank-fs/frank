# v7.3.2 Track B — Frank.Provenance vertical design

Date: 2026-06-27
Issues: #325 (GeneratedProvenance emitter), #330 (Frank.Provenance package), #331 (HTTP test), #332 (4-way composition), #333 (capstone)
Spec source: `docs/superpowers/specs/2026-04-20-v732-semantic-discovery-design.md` §4, §5 (Frank.Provenance), §6 AC #3/#5/#6

## Thesis

A Frank resource that declares a domain type with a `provClass` mapping emits, for every HTTP request, a W3C PROV-O document describing the request as a `prov:Activity`, the response as a `prov:Entity`, and the authenticated principal as a `prov:Agent` — with the Activity carrying the **vocabulary-mapped domain IRI** (`schema:OrderAction`), resolved at build time, not a hardcoded provenance IRI. Provenance is queryable per-resource (lineage history) and renderable inline (this request's provenance), with no statechart coupling.

## Problem

`Frank.Provenance` was deleted in `4d85df54` and never rebuilt. The directory has zero `.fs`, is absent from `Frank.sln`, and the load-bearing v7.3.2 gap it leaves cascades: #331 (HTTP test), #332 (4-way IRI composition), #333 (all-four-packages capstone) are all blocked on it.

The old package coupled provenance to `Frank.Statecharts` (`ConformanceChecker`, `TransitionObserver`, `DualConformanceChecker`) and sourced PROV-O class IRIs from a hardcoded `ProvVocabulary` module. The v7.3.2 thesis requires the opposite: domain-type IRIs come from the project's `vocabulary { provClass … }` declarations, resolved through the same compile-time codegen pipeline as the other three verticals (LinkedData, Validation, Discovery).

## Definitions

- **PROV-O** — W3C provenance ontology, base namespace `http://www.w3.org/ns/prov#`. Three starting-point classes: `prov:Entity` (a thing), `prov:Activity` (something that acted on or produced entities), `prov:Agent` (something responsible). Key relations used: `prov:wasGeneratedBy` (Entity→Activity), `prov:wasAssociatedWith` (Activity→Agent), `prov:used` (Activity→Entity), `prov:startedAtTime`/`prov:endedAtTime`.
- **`provClass`** — vocabulary CE operation mapping an F# type to a `ProvOClass` (`Entity | Activity | Agent`). Already defined: `VocabularyBuilder.ProvClass`, stored in `VocabularyRegistry.ProvClasses : Map<string, ProvOClass>`, surfaced on `ResolvedResource.ProvClass : ProvOClass option`.
- **`produces typeof<T> <status>`** — existing Frank.OpenApi handler-CE operation (`HandlerBuilder.Produces`) that attaches the **standard ASP.NET** `ProducesResponseTypeMetadata(status, type, contentTypes)` to the endpoint. This is the per-operation, per-status response-type declaration provenance reads to learn which F# type a request produced.
- **`GeneratedProvenance`** — build-time-generated module: a static `typeName → (ProvOClass, IRI)` map plus `knownNamespaces`. The sole generated artifact; the IRI mapping is never resolved at runtime.

## Design decisions (settled during brainstorm 2026-06-27)

1. **IRI mapping is compile-time generated, never runtime-resolved.** `GeneratedProvenance.fs` carries `typeName → (ProvOClass, IRI)`. The runtime never reflects a type to mint an IRI.
2. **No route→type FCS extractor.** The codegen model is type-centric (`ResolvedModel.Resources` are domain types, not routes). The request→type bridge is supplied at runtime by **stable ASP.NET endpoint metadata** — the route pattern (resource IRI) and `ProducesResponseTypeMetadata` (the type, by status). Both are intrinsic ASP.NET surfaces, not Frank-invented attachable conventions.
3. **Reuse `produces`, do not compete with it.** `produces` stays an OpenApi extension. The consumer's *resource project* references `Frank.OpenApi` to author `produces`; `Frank.Provenance` itself takes **no** `Frank.OpenApi` dependency — at runtime it reads the **standard** `Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata` that `produces` attaches (verified `src/Frank.OpenApi/HandlerDefinition.fs:71`). This keeps Frank.Provenance multi-targeting net8/9/10 (Frank.OpenApi is net10.0-only) with zero coupling. No second `CustomOperation("produces")` is defined anywhere (that would be an F# CE conflict). *(Corrected 2026-06-27 during execution: the earlier "Frank.Provenance depends on Frank.OpenApi" was a misread — the metadata is standard ASP.NET, not an OpenApi type.)*
4. **Graceful degradation.** Provenance always records request=Activity / response=Entity / principal=Agent. The Activity carries a domain-type IRI **only when** the operation declared `produces typeof<T>` for the response status and `T` has a `provClass`. Otherwise the Activity is an untyped `prov:Activity`. Coverage is never silently dropped.
5. **Two exposures.** (a) Sidecar query endpoint `GET /provenance?resource=<iri>` serves lineage history from the store. (b) Content-negotiated inline: a request carrying `Accept: application/ld+json; profile="http://www.w3.org/ns/prov"` receives that request's PROV-O graph as the response body.
6. **Store is load-bearing.** Port the deleted `MailboxProcessorStore` (append + resource/agent/time indexes + bounded eviction), dropping only statechart-derived record fields. Backs the query endpoint.
7. **Zero statechart coupling.** No `Frank.Statecharts`/`Frank.Statecharts.Core` reference. Statechart-augmented provenance is deferred to Track A/C (spec §5, §9).

## Architecture

### Package dependency graph

```
Frank ──────────────┐
Frank.Semantic ─────┤
                    └──> Frank.Provenance ──> dotNetRdf.Core
                         (reads standard ASP.NET IProducesResponseTypeMetadata;
                          NO Frank.OpenApi dependency)

GeneratedProvenance.fs (emitted into obj/, conditional on Frank.Provenance ref)
```

### Compile-time

`src/Frank.Cli.Core/ProvenanceEmitter.fs` — Fabulous.AST emitter, mirrors `DiscoveryEmitter.fs`/`ValidationEmitter.fs`. **No string concatenation** (CLAUDE.md: codegen MUST use Fabulous.AST+Fantomas).

Input: `ResolvedModel`. Output: `GeneratedProvenance.fs` containing module `GeneratedProvenance` with:

```fsharp
module GeneratedProvenance

// One entry per ResolvedResource whose ProvClass.IsSome.
// Key   = ResolvedResource.FSharpType (full type name).
// Value = (ProvOClass case name, domain IRI or "").
//   IRI = ResolvedResource.ClassIri when Some; "" when the type has a provClass
//   category but no class-IRI mapping (still a typed prov:Activity, just no domain IRI).
let provClasses : (string * (string * string)) list =
    [ "MyApp.OrderPlaced", ("Activity", "https://schema.org/OrderAction")
      "MyApp.Ping",        ("Activity", "")   // provClass but no ClassIri
      // … ]

let knownNamespaces : string[] = [| "https://schema.org/"; … |]
```

The IRI source is `ResolvedResource.ClassIri` (the vocabulary-mapped class, e.g. `schema:OrderAction`); `provClass` supplies only the `ProvOClass` category. An empty IRI string means "category known, domain IRI absent" — the resolver treats `""` as `None`.

**Key-matching caveat:** the map key is the FCS `FSharpType` full name; the middleware looks up by the runtime `Type.FullName` from `ProducesResponseTypeMetadata`. These agree for non-generic types. Generic domain types are out of scope for v7.3.2 (FCS `'T` vs .NET backtick arity render differently); a generic produced-type degrades to untyped `prov:Activity` rather than mismatching.

`src/Frank.Cli.MSBuild/GenerateProvenanceTask.fs` + `build/*.targets` entry — mirrors `GenerateValidationTask`. Conditional generation gated on a `Frank.Provenance` package reference (spec §4 "Conditional generation"). Generated file injected ahead of consumers (`src/CLAUDE.md` compile-order gotcha).

### Runtime package `src/Frank.Provenance/`

Compile order (fsproj `@(Compile)`):

| File | Responsibility |
|------|----------------|
| `ProvenanceTypes.fs` | `ProvenanceRecord`, `Agent`, `ProvenanceStoreConfig`, `ProvenanceConfig`. Pure data. |
| `ProvVocabulary.fs` | Base PROV-O term IRIs (the ontology constants — `prov:Activity` etc.). Not domain IRIs. |
| `ProvenanceGraph.fs` | Pure: `ProvenanceRecord → IGraph`. Builds the PROV-O triples (Activity/Entity/Agent + relations) using base PROV terms + the record's domain IRI. |
| `MailboxProcessorStore.fs` | Ported store: append + resource/agent/time indexes + bounded eviction. `IProvenanceStore`. |
| `GeneratedProvenanceResolver.fs` | Scan assemblies for `GeneratedProvenance`, read `provClasses`+`knownNamespaces` → `ProvenanceConfig`. Fails closed (mirror `GeneratedValidationResolver`). |
| `ProvenanceMiddleware.fs` | Per-request capture; status→type via `ProducesResponseTypeMetadata`; type→IRI via config; build record; append; content-negotiated inline emit. |
| `Frank.Provenance.fs` | `useProvenance` / `useProvenanceWith` CE operations + query-endpoint registration. `TryAddSingleton`. |

### End-to-end flow

```
POST /orders          (handler declares: produces typeof<OrderPlaced> 201)
  │
  ├─ handler runs → 201
  │
  ├─ ProvenanceMiddleware (after handler):
  │     endpoint route pattern         → resource IRI  ("/orders")
  │     ProducesResponseTypeMetadata @ 201 → typeof<OrderPlaced>
  │     GeneratedProvenance.provClasses["MyApp.OrderPlaced"] → (Activity, schema:OrderAction)
  │     ctx.User                       → prov:Agent
  │     build ProvenanceRecord; store.Append(record)
  │
  └─ if Accept negotiates prov profile → body = ProvenanceGraph.toJsonLd(record)
        {
          "@context": "http://www.w3.org/ns/prov",
          "@id": "/orders/123", "@type": "prov:Entity",
          "prov:wasGeneratedBy": {
            "@type": ["prov:Activity", "https://schema.org/OrderAction"],
            "prov:wasAssociatedWith": { "@id": "<principal>", "@type": "prov:Agent" },
            "prov:endedAtTime": "2026-06-27T…"
          }
        }

GET /provenance?resource=/orders/123  → store.QueryByResource → PROV-O ld+json list
```

## Acceptance tests (falsifiable HTTP pairs)

### AT1 — typed Activity from provClass mapping (issue #331, spec AC #3)

Resource maps `OrderPlaced` via `vocabulary { using "schema"; provClass typeof<OrderPlaced> Activity }` and the POST handler declares `produces typeof<OrderPlaced> 201`.

```
POST /orders               Accept: application/ld+json; profile="http://www.w3.org/ns/prov"
→ 201
  body @type includes "prov:Activity" AND "https://schema.org/OrderAction"
  body contains prov:wasAssociatedWith → an object @type "prov:Agent"
  body does NOT contain a hardcoded urn:frank: or ProvVocabulary-minted activity IRI
```

### AT2 — query endpoint serves lineage

```
POST /orders   (×2)
GET  /provenance?resource=/orders   Accept: application/ld+json
→ 200, body is a PROV-O graph listing 2 Activity records, each prov:wasAssociatedWith an Agent
```

### AT3 — graceful degradation (no produces)

Operation with no `produces` declaration.

```
GET /health
→ 200; provenance recorded; the Activity is `prov:Activity` with no domain-type IRI (untyped, not an error, not dropped)
```

### AT4 — 4-way composition (issue #332, spec AC #5)

One resource composing LinkedData + Validation + Provenance + Discovery. The same F# field/type resolves to the **same IRI** in: JSON-LD `@context`, SHACL property path, **PROV-O Activity `@type`**, and ALPS descriptor. Assert the PROV-O Activity IRI equals the LinkedData class IRI for the mapped type.

### AT5 — capstone (issue #333, spec AC #6)

Tic-tac-toe with `vocabulary { using "schema" }`: a naive client POSTs a move, then `GET /provenance?resource=<game>` and reads schema.org-aligned Activity types — navigating provenance purely via discovery, no hardcoded PROV/ schema IRIs in the client.

### Negative

- `GeneratedProvenance` absent (no provClass entries) → `useProvenance` records untyped Activities only; no crash, no build error.
- Lock file with `proposed`/`unresolved` entry → `dotnet build` fails at the existing lock gate before any provenance generation (shared gate, spec §4).

## Work sequencing (outside-in, TDD; subagents in worktree with adversarial review)

1. **Failing E2E first** — AT1 as a failing HTTP test (TestHost), hand-stub `GeneratedProvenance` to green, delete stub → red.
2. **Emitter real** — `ProvenanceEmitter.fs` + unit tests (Fabulous.AST render → FCS typecheck gate, mirror `ValidationEmitter` tests). Re-green AT1 off generated file.
3. **Runtime package** — types → graph → store → resolver → middleware → CE, each TDD. Green AT2, AT3.
4. **MSBuild task** — `GenerateProvenanceTask` + target; conditional-generation + compile-order verified end to end.
5. **Composition + capstone** — wire AT4, AT5 into the sample; verify yourself at absolute worktree paths.

Each non-trivial implementation step runs as a subagent in this worktree with an adversarial review pass (TDD tests committed before impl; re-run suites at absolute paths — never trust agent-reported counts).

## Non-goals (v7.3.2)

- Statechart-augmented PROV-O (transition lineage) — Track A/C.
- Persistent/durable store — in-memory `MailboxProcessorStore` only.
- `prov:wasDerivedFrom` entity-to-entity chains across requests beyond what the store's resource index supports.
- Re-typing via response-body `@type` sniffing (rejected: fragile, defeats the generated mapping).

## Sources

- W3C PROV-O: https://www.w3.org/TR/prov-o/#prov-starting-point-owl-terms
- v7.3.2 semantic-discovery design: `docs/superpowers/specs/2026-04-20-v732-semantic-discovery-design.md`
- Mirror implementations: `src/Frank.Validation/` (resolver, middleware, JsonLdLoader), `src/Frank.Cli.Core/ValidationEmitter.fs`, `src/Frank.Cli.Core/DiscoveryEmitter.fs`
- Existing `produces`: `src/Frank.OpenApi/HandlerBuilder.fs`, `src/Frank.OpenApi/HandlerDefinition.fs`
- Ported store: `git show 4d85df54~1:src/Frank.Provenance/MailboxProcessorStore.fs`
- Shared serialization: `src/Frank.Semantic/RdfSerialization.fs`
