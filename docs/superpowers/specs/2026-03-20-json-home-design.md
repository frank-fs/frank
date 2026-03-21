# JSON Home Document Generation for Frank.Discovery

**Issue:** #104 — Automatic Home Document Generation from Webhost CE Registrations
**Date:** 2026-03-20
**Status:** Design approved

## Overview

Add `useJsonHome` to `Frank.Discovery` that serves a JSON Home document ([draft-nottingham-json-home-06](https://datatracker.ietf.org/doc/html/draft-nottingham-json-home-06)) at `GET /` via content negotiation. The document is pre-computed at startup from registered resources, ALPS profiles, and OpenAPI metadata — zero per-request cost.

## Design Decisions

### All CE resources are included automatically

Every resource registered in the `webHost` CE appears in the home document. No opt-in, no opt-out, no entry point designation. The `webHost` CE IS the API surface declaration — a second declaration would violate the issue's constraint: "Derived, not declared."

Framework-internal endpoints (`/.well-known/frank-profiles`, `/alps/{slug}`, `/scalar/v1`) are excluded — they come from `EndpointDataSource` entries not registered via `ResourceEndpointDataSource`.

### Middleware before routing, not MapGet

The JSON Home document is served by middleware positioned before routing, matching the `OptionsDiscoveryMiddleware` pattern. This avoids route conflicts with user-defined root resources.

Strict `Accept` matching: only `application/json-home` (exact media type match, ignoring parameters and quality values). `*/*`, `application/json`, and partial types do NOT trigger the home document — clients must explicitly request it.

### Pre-computed at startup

Matches the Frank pattern used by affordance maps, profile documents, and every other discovery artifact. The home document is stable metadata — compute once, serve forever.

The middleware constructor takes `EndpointDataSource` as a DI-resolved parameter (same pattern as `OptionsDiscoveryMiddleware`). Pre-computation happens at middleware activation time, after all endpoints are registered.

### Link relation key resolution (fallback chain)

1. ALPS-derived: `{baseUri}#{resourceSlug}` from `ProjectedProfiles` (when available)
2. Route-based fallback: `urn:frank:{assemblyName}{routeTemplate}`
3. Future: explicit `rel` custom operation on `ResourceBuilder` (not in this issue)

### ALPS-enriched hrefVars

Route template variables are mapped to ALPS descriptor URIs when the resource has a matching ALPS profile. This gives clients semantic meaning for template variables, not just names.

- ALPS match: `"gameId": "http://example.com/alps/games#gameId"`
- Fallback: `"gameId": "urn:frank:MyApp/param/gameId"`

### API metadata from OpenAPI when available

The `api` top-level object pulls from OpenAPI configuration (resolved via `IOptions<OpenApiOptions>` at activation time):

- `api.title`: OpenAPI title if configured, otherwise `Assembly.GetEntryAssembly().GetName().Name`
- `api.links.describedBy`: `/.well-known/frank-profiles` (always, when profiles exist)

Resolution is order-independent — `useJsonHome` does not require `useOpenApi` to be called first.

### Method-grouped hints

Following the JSON Home spec's distinction:

- `hints.formats`: media types returned by GET (from `DiscoveryMediaType` on GET endpoints)
- `hints.accept-post`: media types accepted by POST
- `hints.accept-put`: media types accepted by PUT
- `hints.accept-patch`: media types accepted by PATCH
- `hints.allow`: all HTTP methods registered for the resource
- `hints.docs`: Scalar API reference URL (`/scalar/v1`) if `useOpenApi` is configured

`hints.allow` includes `OPTIONS` if `OptionsDiscoveryMiddleware` is registered in the pipeline. Detection mechanism: the CE operation checks whether `OptionsDiscoveryMiddleware` has been added to the middleware pipeline via a marker service registered by `useOptionsDiscovery`.

### Cross-linking with profiles

Both response header and body link to the profiles endpoint:

- Response header: `Link: </.well-known/frank-profiles>; rel="describedby"`
- Body: `api.links.describedBy: "/.well-known/frank-profiles"`

This enables the full discovery loop: `GET /` → JSON Home → follow `describedby` → profiles index → dereference ALPS profiles.

## Architecture

### Dependencies: optional services via DI, no new project references

`Frank.Discovery` does NOT take compile-time dependencies on `Frank.Resources.Model` or `Frank.OpenApi`. Instead, the CE operation resolves optional services from DI at middleware activation time:

- `ProjectedProfiles`: try-resolve from DI. If not registered (no CLI pipeline / no `useAffordances`), ALPS enrichment is skipped — link relations and hrefVars use URN fallbacks.
- `OpenApiOptions`: try-resolve `IOptions<OpenApiOptions>` from DI. If not registered (no `useOpenApi`), title falls back to assembly name, `hints.docs` is omitted.

This keeps `Frank.Discovery` loosely coupled. The projection from framework-specific types to `JsonHomeInput` happens entirely in the CE operation's closure.

### New files in Frank.Discovery

**`JsonHomeDocument.fs`** — Pure module. Zero dependencies (no ASP.NET Core, no Frank.Resources.Model). Takes a `JsonHomeInput` record, produces a JSON string.

```fsharp
type JsonHomeHints =
    { Allow: string list
      /// GET response media types. Serialized as JSON object {"media/type": {}} per JSON Home spec,
      /// NOT as a JSON array. The accept-* fields below ARE serialized as arrays.
      Formats: string list
      AcceptPost: string list option
      AcceptPut: string list option
      AcceptPatch: string list option
      DocsUrl: string option }

type JsonHomeResource =
    { RelationType: string
      /// Route template. If RouteVariables is empty, serialized as "href".
      /// If RouteVariables is non-empty, serialized as "hrefTemplate" + "hrefVars".
      RouteTemplate: string
      RouteVariables: Map<string, string>
      Hints: JsonHomeHints }

type JsonHomeInput =
    { Title: string
      DescribedByUrl: string option
      Resources: JsonHomeResource list }

module JsonHomeDocument =
    val build : JsonHomeInput -> string
```

**`JsonHomeMiddleware.fs`** — Middleware. Constructor takes pre-computed JSON string and optional profiles URL (passed via closure from CE operation). On `GET /` with `Accept: application/json-home`, serves the pre-computed document. Uses `MediaTypeHeaderValue` for Accept header parsing.

Response headers:
- `Content-Type: application/json-home`
- `Cache-Control: max-age=3600`
- `Vary: Accept`
- `Link: </.well-known/frank-profiles>; rel="describedby"` (when profiles exist)

### Updated file

**`WebHostBuilderExtensions.fs`** — New `useJsonHome` CE operation on `WebHostBuilder`. Registers the middleware with a closure that captures the pre-computed document.

### Data flow

```
Startup (impure boundary in CE operation):
  EndpointDataSource → project routes, methods, DiscoveryMediaType per method
  ProjectedProfiles (if available) → ALPS descriptor URIs for relations + hrefVars
  IOptions<OpenApiOptions> (if registered) → api.title, hints.docs
  Assembly metadata → fallback title
  ↓
  JsonHomeDocument.build (pure) → JSON string
  ↓
  JsonHomeMiddleware(preComputedJson, describedByUrl)

Request path:
  GET / Accept: application/json-home → 200 + pre-computed string
  GET / Accept: text/html → pass through to next middleware
  GET / Accept: */* → pass through
  GET / Accept: application/json → pass through
  POST / → pass through
  Any other path → pass through
```

## Example Output

```json
{
  "api": {
    "title": "Frank.TicTacToe.Sample",
    "links": {
      "describedBy": "/.well-known/frank-profiles"
    }
  },
  "resources": {
    "http://example.com/alps/games#game": {
      "hrefTemplate": "/games/{gameId}",
      "hrefVars": {
        "gameId": "http://example.com/alps/games#gameId"
      },
      "hints": {
        "allow": ["GET", "POST", "OPTIONS"],
        "formats": {
          "application/json": {}
        },
        "accept-post": ["application/json"],
        "docs": "/scalar/v1"
      }
    },
    "http://example.com/alps/games#gameSse": {
      "hrefTemplate": "/games/{gameId}/sse",
      "hrefVars": {
        "gameId": "http://example.com/alps/games#gameId"
      },
      "hints": {
        "allow": ["GET"],
        "formats": {
          "text/event-stream": {}
        }
      }
    }
  }
}
```

## Testing

### Unit tests (pure module)

- Empty resources → valid document with empty `resources` object
- Single resource with `href` (no template variables)
- Resource with `hrefTemplate` and `hrefVars`
- ALPS-enriched `hrefVars` alongside URN-fallback `hrefVars` in same document
- Method-grouped hints: `formats` vs `accept-post` vs `accept-put`
- `DocsUrl` present → `hints.docs` included; absent → omitted
- Link relation fallback chain: ALPS-derived, URN-based
- `api.title` and `api.links.describedBy` serialization
- `DescribedByUrl = None` → `api.links` omitted

### Integration tests (TestHost)

- `GET /` with `Accept: application/json-home` → 200, correct headers (`Content-Type`, `Vary`, `Cache-Control`, `Link`)
- `GET /` with `Accept: text/html` → passes through
- `GET /` with `Accept: */*` → passes through
- `GET /` with `Accept: application/json` → passes through
- `GET /` with `Accept: application/json-home; charset=utf-8` → 200 (parameters ignored)
- `GET /` with `Accept: application/json-home, text/html;q=0.9` → 200 (quality values handled)
- `POST /` → passes through
- Document contains all registered resources with correct routes, methods, media types
- Works without `useOpenApi` — title falls back to assembly name, no `hints.docs`
- Works without ALPS profiles — URN-based link relations and hrefVars
- Works alongside `useOptionsDiscovery` and `useLinkHeaders` without interference
- Middleware ordering independence: `useJsonHome` before/after `useDiscovery` produces same result

## Out of scope

- Entry point designation mechanism (unnecessary — all CE resources are entry points)
- Additional representations at `/` (`text/html`, `application/ld+json`) — follow-on work
- `api-catalog` / `.well-known` integration
- Combinatorial integration tests across all middleware (#150)
- `useFullDiscovery` bundle (#148)
- Auto-load AffordanceMap (#149)

## References

- [draft-nottingham-json-home-06](https://datatracker.ietf.org/doc/html/draft-nottingham-json-home-06)
- [RFC 8288](https://www.rfc-editor.org/rfc/rfc8288) — Web Linking
- [RFC 6570](https://www.rfc-editor.org/rfc/rfc6570) — URI Template
