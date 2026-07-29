# Frank.JsonHome

**Date**: 2026-07-28
**Branch**: `json-home`
**Status**: Draft — awaiting review

## Context

Frank can describe itself to machines through OpenAPI (`Frank.OpenApi`), but OpenAPI describes *operations on paths*. [JSON Home](https://datatracker.ietf.org/doc/html/draft-nottingham-json-home-06) describes an API's **entry points** — which link relation types it offers, how to address them, and what each affords — so a client can bootstrap without hard-coding URLs. It also varies by who is asking: an anonymous client and an editor should not be told about the same affordances.

This doesn't belong in `Frank.OpenApi`. It gets its own library.

ALPS was originally scoped into this work and has been split out — see [ALPS and the protocol direction](2026-07-28-frank-alps-protocol-design.md). The short version: ALPS turned out to be the front end of a protocol/statechart model rather than a second document format, and it belongs alongside that model rather than next to a document format.

### Reference specifications

| Spec | Version | Media type |
|---|---|---|
| Home Documents for HTTP APIs | [draft-nottingham-json-home-06](https://datatracker.ietf.org/doc/html/draft-nottingham-json-home-06) | `application/json-home` |
| Web Linking | [RFC 8288](https://www.rfc-editor.org/rfc/rfc8288) | — |
| URI Template | [RFC 6570](https://www.rfc-editor.org/rfc/rfc6570) | — |
| Link Relation Types for Web Services | [RFC 8631](https://www.rfc-editor.org/rfc/rfc8631) | — |

## Goals

1. Serve a JSON Home document describing the application's entry-point resources.
2. Advertise it with a `Link` header on every response, via a mechanism other extensions can share.
3. Filter the document by the current principal's authorization, without requiring `Frank.Auth`.

## Non-goals

- ALPS. Separate work.
- Any dependency on `Frank.OpenApi`, or any NuGet package.
- Discovery of endpoints registered by means other than the `resource` builder.
- Per-response links such as pagination's `next`/`prev`. Those are handler-time concerns, not application-time.

## Package shape

`src/Frank.JsonHome/`, targeting `net8.0;net9.0;net10.0` (matching Frank core, not `Frank.OpenApi`'s narrower set). `FrameworkReference Microsoft.AspNetCore.App` plus `ProjectReference ../Frank`. **Zero NuGet dependencies** — ApiExplorer, `IAuthorizationService`, and `System.Text.Json` are all in the shared framework.

```
ApiSurface.fs                 format-neutral model built from ApiDescription + endpoint metadata
HomeMetadata.fs               metadata types the custom operations attach to endpoints
ResourceBuilderExtensions.fs  rel, hrefVar, docs, deprecated, gone
UriTemplate.fs                ASP.NET route template -> RFC 6570
AuthorizationFilter.fs        per-request principal filtering
JsonHome.fs                   document model + serializer
WebHostBuilderExtensions.fs   useJsonHome
```

Each `.fs` gets a matching `.fsi`, per `CLAUDE.md`.

## Frank core changes

Three changes. None breaks the authoring surface, so Frank core takes a **minor version bump**.

### 1. A shared response-link mechanism

`Link` is a header multiple extensions want to contribute to, and it only works if they cooperate — `ctx.Response.Headers.Link <- value` clobbers whatever was already there, and every contributor would otherwise repeat RFC 8288 formatting. There are three known contributors:

| Relation | Document | Status |
|---|---|---|
| `service-desc` | OpenAPI document | [RFC 8631](https://www.rfc-editor.org/rfc/rfc8631) registered. `Frank.OpenApi` should emit it and currently doesn't. |
| `profile` | ALPS document | [RFC 6906](https://www.rfc-editor.org/rfc/rfc6906) registered. Needed by the deferred ALPS work. |
| `home` | JSON Home document | Project convention, not registered. |

So this is de-duplication of a real conflict, not anticipation of a hypothetical one.

```fsharp
type WebLink =
    { Target: string                      // URI-Reference
      Rel: string
      Params: (string * string) list }    // title, type, hreflang, anchor, ...

type IResponseLinkProvider =
    abstract GetLinks : HttpContext -> WebLink seq
```

One before-routing middleware resolves every registered `IResponseLinkProvider`, formats per RFC 8288, and **appends** to any `Link` headers already present. Before routing, so responses with no matching endpoint carry links too — a 404 is where a lost client most needs the home link.

`WebHostBuilder` gains a `link` operation as sugar for a static provider:

```fsharp
webHost argv {
    useJsonHome
    link "https://example.com/license" "license"
}
```

`Frank.JsonHome` then *contributes* `home` rather than owning a middleware. `Frank.OpenApi` adopting `service-desc` becomes a one-liner, though that is not part of this work.

### 2. `HandlerDefinition` becomes extensible

Today it is a closed record:

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

**This change has no consumer in this scope.** JSON Home needs no handler-level metadata at all — `rel`, `hrefVar`, `docs`, `deprecated`, and `gone` are resource-level, and `hints.formats` / `acceptPost` come from the existing `produces` / `accepts`. It was originally motivated by ALPS's `transition`, which has been split out. It is included anyway because it stands on its own:

- It is a net **deletion** — two public types and six redundant staging fields.
- It is the prerequisite for handler-level `requireRole` in `Frank.Auth`, which is the reason authorization filtering below is resource-granular rather than per-method.

Reasonable to defer to the ALPS work instead. Flagged rather than assumed.

**Impact**: `ProducesInfo` and `AcceptsInfo` are deleted. Every `handler` CE operation keeps its exact signature, so no handler-authoring code moves. `Frank.OpenApi` is unaffected — it touches only `def.Handler` and `toConventions`. `test/Frank.Tests/HandlerBuilderTests.fs` has roughly twenty assertions reading `def.Produces` / `def.Tags` that get rewritten against the typed accessors.

### 3. Promote the per-method convention wrapper

`ResourceSpec.Metadata` conventions apply to *every* endpoint in a resource. `Frank.OpenApi/ResourceBuilderExtensions.fs:21-30` privately works around this by wrapping each convention to inspect the builder's `HttpMethodMetadata` and no-op unless the method matches. Promote it into Frank core as `ResourceBuilder.AddMethodMetadata` and switch `Frank.OpenApi` to it, so the next library needing it doesn't write a third copy.

## JSON Home

### Data flow

```
EndpointDataSource
  → IApiDescriptionGroupCollectionProvider  (ApiDescription per endpoint+method)
    → ApiSurface           group by route template, read home metadata          [cached]
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

Resources without a `rel` are absent from the document. JSON Home is a curated entry point, not a sitemap, and the draft's own examples mint an extension relation type per resource (`tag:me@example.com,2016:widgets`).

### Route template translation

ASP.NET route templates are not URI Templates. A pure, table-tested function:

| ASP.NET | RFC 6570 |
|---|---|
| `{id}` | `{id}` |
| `{id:guid}` | `{id}` |
| `{id?}` | `{id}` |
| `{id=1}` | `{id}` |
| `{*rest}` | `{+rest}` |
| `{**rest}` | `{+rest}` |

### Well-known path and relation type

`/.well-known/home.json` and `rel="home"` are **project convention, not registered**. JSON Home's own guidance is that a home document usually sits at `/`; neither the path nor the `home` relation type is in an IANA registry. Both are configurable, with these as defaults. Accepted knowingly.

## Authorization

Per request, for each candidate resource: combine the endpoint's `IAuthorizeData` and `AuthorizationPolicy` metadata, call `IAuthorizationService.AuthorizeAsync(ctx.User, endpoint, policy)`, drop what fails.

This reads only stock ASP.NET metadata — exactly what `Frank.Auth/EndpointAuth.fs:11` emits, and equally what a plain `AuthorizeAttribute` produces. **No `Frank.Auth` reference.**

### Filtering is resource-granular

`Frank.Auth`'s `requireAuth` / `requireRole` / `requireClaim` / `requirePolicy` are `ResourceBuilder` operations, so their metadata lands on *every* endpoint in the resource. A resource whose POST needs `catalog-editor` therefore also gates its GET, and the whole resource appears or doesn't.

That is a `Frank.Auth` limitation rather than a JSON Home one. Core change 2 above is what would make handler-level `requireRole` possible; once it exists, per-method `allow` hints could be filtered too, with no change here.

### The one real hazard

An auth-varying document behind a shared cache serves one principal's view to another. Whenever filtering is active:

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
| Resource declares `rel` but registers no handlers | Emitted with no `allow` hint. |
| Authorization evaluation throws | Treat as denied, log a warning. Failing closed is the only safe direction. |
| Endpoint collection changes after caching | Change token invalidates the cached `ApiSurface`. |

## Implementation order

Three stages, each independently verifiable.

1. **Frank core** — `WebLink` / `IResponseLinkProvider` and its middleware, `HandlerDefinition` collapse with typed accessors, `AddMethodMetadata`, `Frank.OpenApi` switched over, affected `HandlerBuilderTests` assertions rewritten. Existing suites green.
2. **JSON Home document** — `Frank.JsonHome` project, `ApiSurface` extraction, route template translation, `rel` / `hrefVar` / `docs` / `deprecated` / `gone`, serializer, `useJsonHome`, and the `home` link provider.
3. **Authorization filtering** — principal evaluation and cache directives.

## Testing

Mirrors `test/Frank.Auth.Tests` and `test/Frank.OpenApi.Tests`; new project `test/Frank.JsonHome.Tests`.

- **Pure functions, unit-tested without a server**: route template translation (table-driven over the mapping above), `ApiDescription` → `ApiSurface`, the serializer, and RFC 8288 link formatting.
- **Golden document**: reproduce the example document from draft-06 exactly from an equivalent Frank configuration. This is the strongest available check that we've read the draft right.
- **Integration** via `WebApplicationFactory`: media type, `Link` header presence on 200s and 404s, multiple providers appending rather than clobbering, cache directives, and the same application requested anonymously versus with a role-bearing principal — asserting the two documents differ in exactly the governed resources and no others.
- **Regression**: existing Frank and Frank.OpenApi suites pass unchanged after the `HandlerDefinition` collapse, apart from the rewritten `HandlerBuilderTests` assertions.

## Future work (separate)

- **ALPS and the protocol direction** — [companion design](2026-07-28-frank-alps-protocol-design.md).
- **`Frank.OpenApi` emitting `service-desc`** — a one-liner once core change 1 lands, but not this work.
- **Per-response links** — pagination's `next`/`prev` are handler-time and need a different mechanism than `IResponseLinkProvider`.
