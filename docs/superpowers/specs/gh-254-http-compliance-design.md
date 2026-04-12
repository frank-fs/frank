---
source: "github issue #254"
title: "HTTP protocol compliance"
milestone: "v7.4.0"
state: "CLOSED"
type: spec
---

# HTTP protocol compliance

> Extracted from [frank-fs/frank#254](https://github.com/frank-fs/frank/issues/254)

## Thesis

Frank builds on ASP.NET Core and claims "ASP.NET Core Native" as a constitution
principle and "pit of success" as its design goal. HTTP responses from Frank
middleware must comply with the RFCs that govern HTTP semantics. A framework that
generates incorrect headers, missing mandatory fields, or ignores content
negotiation undermines client trust and breaks standards-aware agents that rely
on HTTP semantics for navigation.

## Current problem

The statechart middleware and order fulfillment sample have several HTTP protocol
issues identified by expert review:

1. **405 missing mandatory Allow header.** When `ResolveHandlers` returns `None`
   (no handlers found for the current state at all), the middleware sets
   `ctx.Response.StatusCode <- 405` but emits no Allow header (Middleware.fs:89).
   The Allow header is only set in the inner branch where handlers exist but the
   specific method doesn't match (Middleware.fs:99-102). RFC 9110 Section 15.5.6:
   "The origin server MUST generate an Allow header field in a 405 response
   containing a list of the target resource's currently supported methods." The
   spec is unconditional — even an empty Allow header must be present.

2. **202 responses missing Content-Location.** The sample's event handlers return
   202 Accepted with no Location or Content-Location header. RFC 9110 Section
   15.3.3: a 202 response "ought to" include a representation describing the
   request's current status and a pointer to a status monitor. After a state
   transition, the client has no standard way to discover where the resource is
   now. `Content-Location` pointing to the resource's own URI signals that the
   resource itself is the status monitor.

3. **Handlers bypass content negotiation entirely.** The sample's GET handler
   writes directly to the response stream via `ctx.Response.WriteAsync("state=...")`.
   This bypasses ASP.NET Core's content negotiation pipeline — the Accept header
   is ignored, no output formatters are consulted, and Content-Type is not set
   based on what was negotiated. ASP.NET Core supports content negotiation but
   does not do it automatically — media types must be registered, formatters
   configured, and handlers must participate. Frank's CE handler patterns write
   directly to the response, producing handlers that ignore Accept headers by
   default. If the "pit of success" produces handlers that don't participate in
   content negotiation, the pit isn't working. Content negotiation is RFC 9110
   Section 12 — it is protocol compliance, not a discovery concern.

## Definition: "compliant"

Every HTTP response from the statechart middleware and sample handlers conforms
to the relevant RFC 9110 sections. A standards-aware HTTP client (or test tool
like `curl -v`) observes correct status codes, mandatory headers, correct
Content-Type declarations, and content negotiation behavior on every response.

## Proposed solution

1. Add Allow header to the outer `None` branch of `HandleStateful` in
   Middleware.fs — compute allowed methods from the hierarchy (if present) or
   emit empty `Allow:` if no methods are available in this state.
2. Add `Content-Location` header to 202 responses pointing to the resource URI.
3. Register appropriate media type formatters and ensure handlers participate
   in content negotiation per RFC 9110 Section 12. The server should serve
   representations matching the client's Accept header and Content-Type must
   honestly describe the representation returned. Unsupported media types
   should return 406 Not Acceptable.

## Acceptance tests

Each test is verified by test-e2e.sh. The issue is not done until every test
produces the specified response.

### 1. 405 always includes Allow header — both middleware paths

```
PUT /orders/o1 (in any state — PUT is never registered)
→ 405
→ Allow header is present (lists available methods or is empty — but MUST exist)
```

```
POST /orders/o1 (in Delivered state — only GET registered)
→ 405
→ Allow: GET
```

This test is unfakeable: `curl -v` shows the response headers. If the Allow
header is missing on any 405, the test fails. Both the "no handlers at all"
path (ResolveHandlers returns None) and the "handlers exist but method doesn't
match" path must include Allow.

### 2. 202 responses include Content-Location

```
POST /orders/o1 (triggers state transition)
→ 202
→ Content-Location: /orders/o1
```

The client can follow Content-Location to observe the result of the accepted
request. Without it, the client must guess where to look after a transition.

### 3. Content negotiation per RFC 9110 Section 12

```
GET /orders/o1 -H "Accept: application/json"
→ 200, Content-Type: application/json

GET /orders/o1 -H "Accept: text/html"
→ 200, Content-Type: text/html (or 406 Not Acceptable if not supported)

GET /orders/o1 -H "Accept: text/event-stream"
→ 200, Content-Type: text/event-stream (or 406 if not supported)

GET /orders/o1 -H "Accept: application/xml"
→ 406 Not Acceptable (if XML not supported)
```

The Content-Type must match the actual representation returned. The server
must not return a body in one format while declaring another, must not ignore
the Accept header, and must return 406 for unsupported media types rather
than silently returning a different format.

### 4. Allow header consistent across response types

```
GET /orders/o1 (in Authorize state, hierarchy enabled)
→ 200
→ Allow header reflects hierarchy-aware method resolution

OPTIONS /orders/o1 (in Authorize state)
→ Allow header matches GET response's Allow header
```

The Allow header must be consistent between regular responses and OPTIONS,
and must reflect the hierarchical method resolution when hierarchy is enabled.

## Dependencies

- Depends on: #250 (hierarchical transitions operational) — Allow header
  computation must use operational hierarchy
- Independent of: #251, #252, #253

## Expert sources

- **Fielding**: 405 response missing mandatory Allow header when ResolveHandlers
  returns None (RFC 9110 Section 15.5.6 violation); 202 without
  Content-Location leaves clients without navigation after transitions;
  Allow header must be consistent between responses and OPTIONS
- **Claude**: GET returns opaque plain text with no Content-Type; no content
  negotiation; response format not machine-parseable
