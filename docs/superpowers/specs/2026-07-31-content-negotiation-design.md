# Frank Content Negotiation

**Date**: 2026-07-31
**Branch**: `017-content-negotiation`
**Status**: Draft — awaiting review

## Context

`src/Frank/ContentNegotiation.fs` provides `negotiate`/`ctx.Negotiate(statusCode, body)`, built on ASP.NET Core MVC's `OutputFormatterSelector`/`IOutputFormatter` pipeline (registered via `AddMvcCore()`). Its own doc-comment cites its source as "content negotiation *by hand*" — an accurate description. Unlike the older ASP.NET Web API (`HttpConfiguration.Formatters`, `IContentNegotiator` — a first-class, standalone concept independent of any dispatch model), ASP.NET Core never shipped content negotiation as a standalone service; the MVC-era conneg engine is coupled to `IActionResult` and buried inside `ObjectResultExecutor`. `negotiate` reaches into that coupling rather than using something designed to stand alone.

`ctx.Negotiate` is never actually called anywhere in this repository (confirmed by grep), and the one sample that claims to demonstrate content negotiation, `getProductNegotiated` in `sample/Frank.OpenApi.Sample/Handlers.fs:148-170`, is fake — its own comment admits it always returns JSON regardless of `Accept`.

This surfaced while designing [`Frank.Rdf`](2026-07-30-frank-rdf-design.md), whose *Serving it over HTTP* section treats JSON-LD as a representation choice on an existing resource (`GET /games/{id}` returning HTML, JSON, or `application/ld+json` at the same URL, selected by `Accept`) rather than a sibling resource, and names this issue as one of two explicit prerequisites blocking that work. [frank-fs/frank#483](https://github.com/frank-fs/frank/issues/483) (the `Frank.Rdf` tracking issue) is paused specifically on this and [#481](https://github.com/frank-fs/frank/issues/481) (a separate `IResponseLinkProvider` gap, out of scope here).

Tracked as [frank-fs/frank#482](https://github.com/frank-fs/frank/issues/482). A prior pass through this work produced a technology-agnostic requirements spec (`specs/017-content-negotiation/spec.md`, via this repo's speckit workflow) establishing the same requirements this design formalizes: independently-produced representations per media type, correct `Accept`-based selection with quality-value precedence, a 406 fallback, and no forced MVC dependency. This document supersedes that spec with the actual mechanism decision and concrete design; the speckit artifacts are retired in favor of this doc.

## Goals

1. Replace `ContentNegotiation.fs`'s `IOutputFormatter`-based mechanism with a lean, Frank-native primitive that doesn't require `AddMvcCore()`.
2. Support representations that are genuinely independent producers per media type — not one shared object reformatted differently per format. Driving cases: `Frank.Rdf` adding an `application/ld+json` representation built from RDF metadata, distinct from a resource's JSON/HTML bodies; and the ASP.NET Web API-era prior art of an `image/png` representation built from a database lookup, sharing nothing with the same resource's JSON representation.
3. Correct, tested `Accept`-header selection, including quality-value precedence, with a 406 response when nothing matches.
4. A working sample that actually varies its response by `Accept`, replacing the fake one.
5. Free integration with OpenAPI document generation through the existing `HandlerDefinition.Metadata` pipeline — no changes required in `Frank.OpenApi` itself.

## Non-goals

- **A `negotiate` operation directly on `ResourceBuilder`.** Content negotiation selects a representation for a given HTTP method's response — orthogonal to which method is being defined. It composes at the handler level instead, the same seam `handler { }` already uses (see *Composition*). `ResourceBuilder.fs` and `WebHostBuilder.fs` are unchanged by this design.
- **Preserving the old `ctx.Negotiate(statusCode, body)` shape.** Nothing in the repository calls it today, so there is no back-compat obligation. The new shape deliberately drops the `statusCode` parameter — each representation's own handler sets its own response status, consistent with every other Frank handler.
- **`IOutputFormatter`'s one-object-many-formatters model.** Explicitly rejected — see *Why not `IOutputFormatter`*.
- **Non-HTTP-facing representations** (Turtle, RDF/XML, etc.). This is a generic HTTP mechanism; format-specific concerns belong to the packages that plug into it (e.g. `Frank.Rdf`).
- **Per-representation metadata sugar beyond what `handler { }` already provides** (e.g. auto-inferred `produces` entries for bare-function representations). YAGNI until a real need shows up; see *Future work*.

## Package shape

New file pair replacing `ContentNegotiation.fs`/`.fsi` entirely: `src/Frank/NegotiateBuilder.fs`/`.fsi`, moving from the current bare `namespace Frank` into `namespace Frank.Builder` — it belongs with `HandlerBuilder`/`ResourceBuilder`, not off on its own.

`Frank.fsproj` compile order changes from:

```
ContentNegotiation.fsi / .fs
HandlerDefinition.fsi / .fs
HandlerBuilder.fsi / .fs
ResourceBuilder.fsi / .fs
WebHostBuilder.fsi / .fs
```

to:

```
HandlerDefinition.fsi / .fs
HandlerBuilder.fsi / .fs
NegotiateBuilder.fsi / .fs
ResourceBuilder.fsi / .fs
WebHostBuilder.fsi / .fs
```

No new NuGet dependency: `Microsoft.Net.Http.Headers` (including `MediaTypeHeaderValue` and `MediaTypeHeaderValueComparer`) ships in the `Microsoft.AspNetCore.App` shared framework reference Frank already targets — not in `Microsoft.AspNetCore.Mvc.Core`. This is the concrete confirmation of the issue's central claim: RFC-correct, quality-aware `Accept` parsing is available without pulling in MVC.

Since `NegotiateBuilder.Run` produces a plain `HandlerDefinition` — the same type `HandlerBuilder.Run` already produces — and `ResourceBuilder`'s `Get`/`Post`/etc. already accept one, **this is purely additive**. No changes to `ResourceBuilder.fs` or `WebHostBuilder.fs`.

## The design

### Why not `IOutputFormatter`

`OutputFormatterSelector` operates on an object that's already been produced: a handler builds a value, then the selector picks a formatter to *serialize* that one value differently per media type. That model has no room for representations that need entirely different production paths — a JSON-LD representation assembled from separate RDF metadata, or an image fetched from storage — without eagerly producing every representation before negotiation even runs, defeating the purpose. It also cannot be used without `AddMvcCore()`, since `OutputFormatterSelector` is an MVC service.

The older ASP.NET Web API's `IContentNegotiator`/`MediaTypeFormatter` pipeline didn't have this restriction: a formatter's contract was "given this media type won, produce a body" — never restricted to reformatting one shared object graph. Formatters producing an image from a per-resource database lookup, sharing nothing with the formatter producing that resource's JSON representation, were a normal pattern under that model. This design follows that lineage instead of MVC's, matching what `Frank.Rdf`'s HTTP-serving section (linked above) already anticipated.

**Decision: `ContentNegotiation.fs` is fully replaced**, not kept alongside the new mechanism. Nothing in the repository depends on the old shape, and the new mechanism can express "one object, many formats" just as easily as "independent producers" — there's no case the old mechanism covered that the new one doesn't.

### API shape

```fsharp
namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type NegotiateSpec =
    { Representations: (string * RequestDelegate) list  // (mediaType, producer), registration order preserved
      Metadata: obj list }

    static member Empty: NegotiateSpec

[<Sealed>]
type NegotiateBuilder =
    new: unit -> NegotiateBuilder

    member Yield: 'T -> NegotiateSpec
    member Run: spec: NegotiateSpec -> HandlerDefinition

    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: RequestDelegate -> NegotiateSpec
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> Task<'a>) -> NegotiateSpec
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> Async<'a>) -> NegotiateSpec
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> unit) -> NegotiateSpec
    member Accepts: spec: NegotiateSpec * mediaType: string * handlerDef: HandlerDefinition -> NegotiateSpec

[<AutoOpen>]
module NegotiateFunctions =
    val negotiate: NegotiateBuilder
```

The `Accepts` overload set mirrors `ResourceBuilder`'s `Get`/`Post` family: a bare handler function in any of the shapes Frank already accepts, a raw `RequestDelegate`, or a `HandlerDefinition` produced by `handler { }`. It's named `accepts` (not `provides`) to read the same way the HTTP `Accept` request header does — this is the operation that says which representation a request's `Accept` value maps to.

`Run` validates that at least one representation was registered, mirroring `HandlerBuilder.Run`'s existing "handler must be set" check:

```fsharp
member _.Run(spec: NegotiateSpec) =
    if List.isEmpty spec.Representations then
        failwith "At least one representation must be registered using the 'accepts' operation"
    { Handler = RequestDelegate(dispatch spec.Representations)
      Metadata = spec.Metadata }
```

### Composition with `resource { }` / `handler { }` — one layer of nesting, not a new kind

Frank core already has an established two-CE composition pattern (`handler { }` feeding `resource { }`'s `get`/`post`): the inner CE is self-contained, `Run` returns a plain value, and the outer CE's operation takes that value as an ordinary parameter — no `Combine`, no `Delay`, on either side. `negotiate { }` follows the identical shape, plugging into the exact overload `ResourceBuilder.Get` already has for a `HandlerDefinition`:

```fsharp
resource "/products/{id}" {
    get (negotiate {
        accepts "application/json" (handler { produces typeof<Product> 200; handle getProductJson })
        accepts "text/html" getProductHtml
        accepts "application/ld+json" getProductJsonLd
    })
}
```

A top-level `negotiate` operation directly on `ResourceBuilder` was considered and rejected: content negotiation selects a representation for whichever HTTP method is being defined, which is orthogonal to *which* method that is. A `ResourceBuilder`-level operation would have to either assume GET or invent a way to carry the method alongside representation data, duplicating what `get`/`post`/etc. already express. Nesting under the method operation is not extra ceremony forced by this design — it's the same seam `handler { }` already established for exactly this reason.

### Dispatch algorithm

The `RequestDelegate` `Run` builds does the following, using `Microsoft.Net.Http.Headers.MediaTypeHeaderValue` (shared framework, not MVC):

1. Parse `ctx.Request.Headers.Accept` into a list of `MediaTypeHeaderValue` entries. Entries that fail to parse are dropped rather than treated as fatal; if nothing parses at all (header absent, empty, or entirely malformed), fall through to step 4.
2. Sort the parsed entries by effective quality (`MediaTypeHeaderValueComparer.QualityComparer`, the same comparer ASP.NET Core's own formatter selection uses internally), highest preference first.
3. Walk the sorted list; for each entry, find the first registered representation (in registration order) whose media type it matches, using `MediaTypeHeaderValue.MatchesMediaType` (handles exact matches and `type/*`/`*/*` wildcards). The first representation found this way wins — this is also how equal-quality ties resolve, by registration order.
4. If step 1 yielded nothing to walk (no usable `Accept` information), select the first-registered representation as the default.
5. If step 3 completes without a match, respond with `406 Not Acceptable` and no body.
6. Otherwise, set `ctx.Response.ContentType` to the *winning representation's* declared media type (not the client's wildcard/pattern) and invoke only that representation's delegate. No other representation's delegate is ever invoked.

Exact call shapes (`TryParseList` vs. `ParseList`, the comparer's sort direction) are verified against the real API during implementation, the same way the `Frank.Rdf` plan verified each dotNetRDF call against actual documentation — this section fixes the *behavior*, not unverified method signatures.

### OpenAPI / metadata integration

`Frank.OpenApi` does not itself inspect `HandlerDefinition` — it relies on `Microsoft.AspNetCore.OpenApi` reading ASP.NET Core's own endpoint metadata, which Frank core already populates from `HandlerDefinition.Metadata` via `HandlerDefinitionMetadata.toConventions` and `ResourceSpec.Build`. Because `NegotiateSpec.Metadata` accumulates from any `accepts` call that passed a `handler{}`-built `HandlerDefinition` (concatenating each representation's metadata into the final `HandlerDefinition.Metadata`, in registration order), this flows through the exact same pipeline with no new code in `Frank.OpenApi`:

```fsharp
get (negotiate {
    accepts "application/json" (handler { produces typeof<Product> 200 })
    accepts "text/html" (handler { producesEmpty 200 })
})
```

produces a generated OpenAPI operation listing both response representations. A representation registered as a bare function (no `handler { }`) contributes no metadata — the same rule that already applies to bare-function handlers everywhere else in Frank.

### Sample fix

`getProductNegotiated` in `sample/Frank.OpenApi.Sample/Handlers.fs` is rewritten to use `negotiate { }` with representations that genuinely differ (e.g. JSON vs. HTML), removing the comment admitting the current version always returns JSON.

## Error handling and edge cases

| Situation | Behaviour |
|---|---|
| `negotiate { }` with no `accepts` calls | `Run` throws, mirroring `HandlerBuilder.Run`'s "handler must be set" validation. |
| `Accept` matches no registered representation | `406 Not Acceptable`, no body written. |
| `Accept` absent, or only wildcards (`*/*`) | First-registered representation, treated as the default. |
| `Accept` present but entirely unparseable | Treated the same as absent — falls back to the first-registered representation, never a `500`. |
| Two or more representations tie on effective quality | Registration order breaks the tie; first-registered of the tied set wins. |
| A representation not selected for a given request | Its delegate is never invoked — verified by tests asserting non-selected producers don't run. |

## Implementation order

1. `NegotiateBuilder.fs`/`.fsi`: `NegotiateSpec`, `NegotiateBuilder`, dispatch function, `Run` validation. Unit tests alongside (quality sort, wildcard match, 406, default-on-absent/malformed, invoke-only-the-winner).
2. Remove `ContentNegotiation.fs`/`.fsi`; update `Frank.fsproj` compile order.
3. Metadata-merge behavior: a representation registered via `handler { produces ... }` shows up correctly in `HandlerDefinition.Metadata`; spot-check against `Frank.OpenApi`'s generated document.
4. Fix `getProductNegotiated` in `Frank.OpenApi.Sample`.

## Testing

New `test/Frank.Tests/NegotiateBuilderTests.fs`, following `HandlerBuilderTests.fs`'s and `DatastarTests.fs`'s existing style — `DefaultHttpContext()` with `Response.Body` swapped for a `MemoryStream`, `Request.Headers.Accept` set directly, no real host needed.

- Multiple representations registered; correct one selected for `Accept: application/json`, `Accept: text/html`, etc.
- Quality-value precedence: `Accept: text/html;q=0.3, application/json;q=0.8` selects the `application/json` representation.
- `Accept` matching nothing registered → 406, empty body.
- `Accept` absent, and `Accept: */*` → default (first-registered) representation.
- Malformed `Accept` header → default representation, not an error.
- Non-selected representations' producers are never invoked (assert via a mutable flag per representation).
- A representation registered via `handler { produces ... }` contributes its metadata to the final `HandlerDefinition.Metadata`; one registered as a bare function contributes none.
- `negotiate { }` with zero `accepts` calls throws.

## Future work (separate)

- `Frank.Rdf`'s `application/ld+json` representation ([#483](https://github.com/frank-fs/frank/issues/483)) is the first real consumer of this mechanism beyond the sample.
- Thin per-representation metadata sugar (e.g. inferring `produces` for bare-function representations) if a real need for it shows up — not built speculatively now.
- Additional representation formats (Turtle, RDF/XML, etc.) are Frank.Rdf's concern, not this mechanism's — nothing here is JSON/HTML-specific.
