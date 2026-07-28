# Frank.Discovery — JSON Home and ALPS

**Date**: 2026-07-28
**Branch**: `json-home`
**Status**: Draft — awaiting review

## Context

Frank can describe itself to machines through OpenAPI (`Frank.OpenApi`), but OpenAPI describes *operations on paths*. Two other formats describe things OpenAPI cannot:

- **[JSON Home](https://datatracker.ietf.org/doc/html/draft-nottingham-json-home-06)** describes an API's *entry points* — which link relation types it offers, how to address them, and what each affords. A client can bootstrap from it without hard-coding URLs.
- **[ALPS](https://datatracker.ietf.org/doc/draft-amundsen-richardson-foster-alps/)** describes an API's *vocabulary and state transitions*, deliberately free of URLs, HTTP methods, and status codes. It says what the terms mean, not where they live.

Neither belongs in `Frank.OpenApi`. Both should be able to vary by who is asking — an anonymous client and an editor should not be told about the same affordances.

### Reference specifications

| Spec | Version | Media type |
|---|---|---|
| Home Documents for HTTP APIs | [draft-nottingham-json-home-06](https://datatracker.ietf.org/doc/html/draft-nottingham-json-home-06) | `application/json-home` |
| Application-Level Profile Semantics | [draft-amundsen-richardson-foster-alps-07](https://datatracker.ietf.org/doc/draft-amundsen-richardson-foster-alps/) | `application/alps+json` |
| Web Linking | [RFC 8288](https://www.rfc-editor.org/rfc/rfc8288) | — |
| URI Template | [RFC 6570](https://www.rfc-editor.org/rfc/rfc6570) | — |

## Goals

1. Serve a JSON Home document, advertised by a `Link` header on every response.
2. Serve an ALPS profile describing application vocabulary.
3. Filter both by the current principal's authorization, without requiring `Frank.Auth`.
4. Make `HandlerDefinition` extensible so this and future libraries can attach handler metadata without changing Frank core.

## Non-goals

- ALPS XML (`application/alps+xml`). JSON only.
- Deriving ALPS vocabulary from anything. See *Why ALPS vocabulary is authored*.
- Any dependency on `Frank.OpenApi`, or any NuGet package.
- Discovery of endpoints registered by means other than the `resource` builder.

## Package shape

One package, `src/Frank.Discovery/`, targeting `net8.0;net9.0;net10.0` (matching Frank core, not `Frank.OpenApi`'s narrower set). `FrameworkReference Microsoft.AspNetCore.App` plus `ProjectReference ../Frank`. **Zero NuGet dependencies** — ApiExplorer, `IAuthorizationService`, and `System.Text.Json` are all in the shared framework.

Two independent opt-in operations, `useJsonHome` and `useAlps`, over one shared extraction layer. They ship together because they share extraction and authorization filtering; splitting them would require a third shared-core package for what is a modest amount of code. Neither runs unless invoked.

```
ApiSurface.fs                 format-neutral model built from ApiDescription + endpoint metadata
DiscoveryMetadata.fs          metadata types the custom operations attach to endpoints
ResourceBuilderExtensions.fs  rel, hrefVar, docs, deprecated, gone
HandlerBuilderExtensions.fs   transition
AuthorizationFilter.fs        per-request principal filtering
JsonHome.fs                   document model + serializer
JsonHomeMiddleware.fs         endpoint + Link header
Alps.fs                       descriptor model, combinators, serializer
AlpsMiddleware.fs             endpoint
WebHostBuilderExtensions.fs   useJsonHome, useAlps
```

Each `.fs` gets a matching `.fsi`, per `CLAUDE.md`.

## Frank core changes

Two changes, both in the "we're working here anyway" category. Neither breaks the authoring surface, so this is a **minor version bump**.

### 1. `HandlerDefinition` becomes extensible

Today it is a closed record with nowhere for another library to write:

```fsharp
type HandlerDefinition =
    { Handler: RequestDelegate
      Name: string option
      Summary: string option
      Description: string option
      Tags: string list
      Produces: ProducesInfo list
      Accepts: AcceptsInfo list }
```

`HandlerDefinitionMetadata.toConventions` already maps every one of those fields 1:1 onto a stock ASP.NET metadata type — `EndpointNameMetadata`, `EndpointSummaryAttribute`, `EndpointDescriptionAttribute`, `TagsAttribute`, `ProducesResponseTypeMetadata`, `AcceptsMetadata`. The fields are a redundant staging copy of the metadata they become. So collapse them:

```fsharp
type HandlerDefinition =
    { Handler: RequestDelegate
      Metadata: obj list }

module HandlerDefinition =
    val tryFind : HandlerDefinition -> 'T option
    val findAll : HandlerDefinition -> 'T list
```

`toConventions` reduces to mapping each entry onto `b.Metadata.Add`, preserving declaration order.

**Impact**: `ProducesInfo` and `AcceptsInfo` are deleted. Every `handler` CE operation keeps its exact signature, so no handler-authoring code moves. `Frank.OpenApi` is unaffected — it touches only `def.Handler` and `toConventions`. `test/Frank.Tests/HandlerBuilderTests.fs` has roughly twenty assertions reading `def.Produces` / `def.Tags` that get rewritten against `def.Metadata` via the typed accessors.

### 2. Promote the per-method convention wrapper

`ResourceSpec.Metadata` conventions apply to *every* endpoint in a resource. `Frank.OpenApi/ResourceBuilderExtensions.fs:21-30` privately works around this by wrapping each convention to inspect the builder's `HttpMethodMetadata` and no-op unless the method matches. `Frank.Discovery` needs identical behaviour. Promote it into Frank core as `ResourceBuilder.AddMethodMetadata` and switch `Frank.OpenApi` to it.

## JSON Home

### Data flow

```
EndpointDataSource
  → IApiDescriptionGroupCollectionProvider  (ApiDescription per endpoint+method)
    → ApiSurface           group by route template, read discovery metadata     [cached]
      → filter by principal                                                     [per request]
        → JsonHome.Document
          → application/json-home
```

Extraction is cached against the ApiExplorer change token. Only filtering and serialization run per request.

### Mapping

| JSON Home | Source |
|---|---|
| key (link relation type) | `rel` operation — resources without one are omitted |
| `href` | route template, when it has no parameters |
| `hrefTemplate` | route template translated to RFC 6570, when it has parameters |
| `hrefVars` | `hrefVar "id" "https://example.org/param/widget"` |
| `hints.allow` | distinct `ApiDescription.HttpMethod` across the group |
| `hints.formats` | GET's `SupportedResponseTypes` content types |
| `hints.acceptPost` / `acceptPut` / `acceptPatch` | that method's `SupportedRequestFormats` |
| `hints.docs` | `docs` operation |
| `hints.status` | `deprecated` / `gone` operations |
| `api.title`, `api.links` | `useJsonHome` configuration |

Hint names use draft-06's camelCase forms (`acceptPatch`, `preconditionRequired`, `authSchemes`), **not** the hyphenated forms from earlier drafts that most blog posts still show.

### Authoring

```fsharp
resource "/products" {
    rel "tag:example.com,2026:products"
    docs "https://example.com/docs/products"
    get listProducts
    post createProduct
}

resource "/products/{id:guid}" {
    rel "tag:example.com,2026:product"
    hrefVar "id" "https://example.com/param/product-id"
    get getProduct
    delete deleteProduct
}
```

### Route template translation

ASP.NET route templates are not URI Templates. This is a pure, table-tested function:

| ASP.NET | RFC 6570 |
|---|---|
| `{id}` | `{id}` |
| `{id:guid}` | `{id}` |
| `{id?}` | `{id}` |
| `{id=1}` | `{id}` |
| `{*rest}` | `{+rest}` |
| `{**rest}` | `{+rest}` |

### Well-known paths

`/.well-known/home.json` and `rel="home"` are **project convention, not registered**. JSON Home's own guidance is that a home document usually sits at `/`; neither the path nor the `home` relation type is in an IANA registry. Both are configurable, with these as defaults. Accepted knowingly.

### Link header

Emitted from `plugBeforeRouting` so it lands on every response including 404s — where a lost client most needs it. Appends to existing `Link` headers rather than replacing them.

```
Link: </.well-known/home.json>; rel="home"
```

## ALPS

ALPS contains no URLs, HTTP methods, or status codes. Vocabulary is authored as plain values; only a transition's `type` is derived, because the ALPS draft itself defines it in HTTP terms.

### Descriptors are values

```fsharp
module Catalog =
    let productId   = semantic "productId"   |> def "https://schema.org/productID"
    let productName = semantic "productName" |> doc "Display name"
    let price       = semantic "price"       |> doc "Price in minor units"
    let product     = semantic "product"     |> contains [ productId; productName; price ]
```

Four constructors — `semantic`, `safe`, `unsafe`, `idempotent` — so a profile can describe transitions that aren't bound to a handler, which real ALPS profiles routinely do. Combinators: `doc`, `def`, `tag`, `rel`, `contains`, `rt`.

### Transitions bind vocabulary to handlers

```fsharp
resource "/products" {
    rel "tag:example.com,2026:products"
    get  (handler { handle listProducts;  transition "listProducts"  Catalog.product })
    post (handler { handle createProduct; transition "createProduct" Catalog.product })
}

useAlps [ Catalog.product ]
```

`transition` takes a **new id** (a string, because it is minting a name — there is nothing to check it against) and a **descriptor value** for `rt` (compiler-checked, so no dangling fragment references). Its `type` comes from the HTTP method it is bound to, per the draft:

| Method | `type` |
|---|---|
| GET, HEAD | `safe` |
| PUT, DELETE | `idempotent` |
| POST | `unsafe` |

The earlier sketch of this connected the profile to resources by unchecked strings on both sides. It doesn't anymore.

### Output

```json
{ "alps": { "version": "1.0", "descriptor": [
    { "id": "product", "type": "semantic", "descriptor": [
        { "id": "productId", "def": "https://schema.org/productID" },
        { "id": "productName", "doc": { "value": "Display name" } },
        { "id": "price", "doc": { "value": "Price in minor units" } } ] },
    { "id": "listProducts",  "type": "safe",   "rt": "#product" },
    { "id": "createProduct", "type": "unsafe", "rt": "#product" } ] } }
```

`type` is omitted where it would be `semantic`, which the draft treats as the default.

## Authorization

Per request, for each candidate entry: combine the endpoint's `IAuthorizeData` and `AuthorizationPolicy` metadata, call `IAuthorizationService.AuthorizeAsync(ctx.User, endpoint, policy)`, drop what fails.

- Home document **resources** the principal cannot reach are omitted.
- ALPS **transitions** the principal cannot reach are omitted.
- ALPS **semantic descriptors are never filtered.** They are vocabulary, not capability; a term is not a secret, and pruning them would make the anonymous and authenticated documents gratuitously hard to diff.

This reads only stock ASP.NET metadata — exactly what `Frank.Auth/EndpointAuth.fs:11` emits, and equally what a plain `AuthorizeAttribute` produces. **No `Frank.Auth` reference.**

### Filtering is resource-granular today

`Frank.Auth`'s `requireAuth` / `requireRole` / `requireClaim` / `requirePolicy` are `ResourceBuilder` operations, so their metadata lands on *every* endpoint in the resource. Two transitions bound to different methods of the same resource therefore share one authorization outcome — a resource whose POST needs `catalog-editor` also gates its GET.

So filtering distinguishes resources, not methods within a resource. That is a `Frank.Auth` limitation, not a discovery one: the extensible `HandlerDefinition` from this design is precisely what would make handler-level `requireRole` possible, and once it exists this filtering becomes per-method with no change here. Out of scope for this work, but worth knowing why the granularity is what it is.

### The one real hazard

An auth-varying document behind a shared cache serves one principal's view to another. Whenever filtering is active, both endpoints emit:

```
Cache-Control: private, no-cache
Vary: Authorization
```

## Error handling and edge cases

| Situation | Behaviour |
|---|---|
| Two resources declare the same `rel` | Startup error. `resources` is a JSON object; silent overwrite is worse than a failure. |
| `hrefVar` names a variable not in the route template | Startup error — almost certainly a typo. |
| Template variable with no `hrefVar` | Allowed. `hrefVars` is optional per the draft. |
| Two transitions share a descriptor id | Startup error. Ids are document-wide unique in ALPS. |
| Resource declares `rel` but registers no handlers | Emitted with no `allow` hint. |
| `useAlps` with an empty profile | Serves a valid document with an empty `descriptor` array. |
| Authorization evaluation throws | Treat as denied, log a warning. Failing closed is the only safe direction. |
| Endpoint collection changes after caching | Change token invalidates the cached `ApiSurface`. |

## Implementation order

Four stages, each independently verifiable. Stage 1 is a prerequisite for stage 3 and can land on its own.

1. **Frank core** — collapse `HandlerDefinition` to `{ Handler; Metadata }`, add typed accessors, promote `AddMethodMetadata`, switch `Frank.OpenApi` over, rewrite the affected `HandlerBuilderTests` assertions. Existing suites green.
2. **JSON Home** — `Frank.Discovery` project, `ApiSurface` extraction, route template translation, `rel` / `hrefVar` / `docs` / `deprecated` / `gone`, serializer, `useJsonHome`, `Link` header.
3. **ALPS** — descriptor model and combinators, `transition`, serializer, `useAlps`.
4. **Authorization filtering** — applies to both documents at once, plus cache directives.

## Testing

Mirrors `test/Frank.Auth.Tests` and `test/Frank.OpenApi.Tests`; new project `test/Frank.Discovery.Tests`.

- **Pure functions, unit-tested without a server**: route template translation (table-driven over the mapping above), `ApiDescription` → `ApiSurface`, both serializers.
- **Golden documents**: reproduce the example documents from both drafts exactly from an equivalent Frank configuration. This is the strongest available check that we've read the specs right.
- **Integration** via `WebApplicationFactory`: media types, `Link` header presence on 200s and 404s, cache directives, and the same application requested anonymously versus with a role-bearing principal — asserting the two documents differ in exactly the governed entries and no others.
- **Regression**: existing Frank and Frank.OpenApi suites must pass unchanged after the `HandlerDefinition` collapse, apart from the rewritten `HandlerBuilderTests` assertions.

## Why ALPS vocabulary is authored

Worth recording, because the prior art appears to point elsewhere. [Spring Data REST](https://docs.spring.io/spring-data/rest/reference/metadata.html) — the largest real ALPS implementation — auto-generates descriptors, one per entity attribute, serving a profile per resource. So why not reflect the F# types already declared via `produces` / `accepts`?

Because that would describe the wrong thing. An ALPS profile explains the semantics of a **hypermedia document** — HTML, HAL, Collection+JSON, Siren. An arbitrary F# response record is not a hypermedia type, and reflecting its fields produces a description of a serialization shape wearing ALPS vocabulary. Spring gets away with it because its representations *are* generated from the entities it owns end to end; the entity and the hypermedia document are the same artifact. Frank has no such coupling.

The derivation source that would genuinely make sense is HTML templates — that is the document ALPS exists to annotate. Frank deliberately has no dedicated template language: Hox, Oxpecker.ViewEngine, Giraffe, and Falco are separate integrations, by design. There is nothing single to derive from, and building four derivations would re-couple Frank to view engines it intentionally keeps at arm's length.

So vocabulary is authored. If a future Frank gains a first-class hypermedia representation, deriving from *that* is the option worth revisiting — not reflecting CLR types.

## Future work (separate)

Authoring's one real cost is that nothing checks a profile against what the application actually renders: rename a field in a Hox template and the profile silently lies. `Frank.Analyzers` (FSharp.Analyzers.SDK) is the natural home for closing that — an analyzer that inspects authored profiles against view-engine templates or format serializers and reports descriptor ids that appear in one and not the other.

That reaches across every view engine Frank integrates with, so it is its own piece of work, not part of this one. Noted here so the drift risk is not mistaken for an unexamined gap.
