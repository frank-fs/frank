# Frank.Alps

Frank.Alps serves [ALPS](https://datatracker.ietf.org/doc/draft-amundsen-richardson-foster-alps/) (Application-Level Profile Semantics, draft-07) profile documents describing what your resources *mean* and which transitions are available. Profiles are hand-authored F# values — never derived from CLR types or view templates — and each transition is bound to the endpoint that implements it, so the served document is filtered per principal and, optionally, per resource state.

## Features

- **Hand-authored profiles**: plain `|>` combinators, or a `descriptor { }` computation expression — both produce identical `Descriptor` values
- **Compile-checked references**: `rt`/`href`/`from` take a `Descriptor` value, not a string id, so a dangling reference is a compile error, not a wrong document
- **Composite-state authoring**: `contains`/`initial`/`regions` express substate (OR) and orthogonal (AND) decomposition, riding `ext` so documents stay spec-valid for readers that don't know them
- **Zero-friction binding**: `binds descriptor` inside `handler { }` attaches a transition to the endpoint that implements it — no separate registry to keep in sync
- **Startup validation**: a transition's authored `type` is checked against its bound HTTP method; a mismatch fails host startup, and an unbound transition is logged as a warning, rather than either shipping silently
- **Authorization- and state-filtered documents**: reads stock `IAuthorizeData`/`AuthorizationPolicy` metadata (no `Frank.Auth` dependency) at every depth of the profile, and optionally filters by a `CurrentStateResolver` you supply
- **Two HTTP exposures**: an app-wide document at `/.well-known/alps.json`, and a per-resource excerpt wired into `negotiate { }`
- **Zero NuGet dependencies**: only `FrameworkReference Microsoft.AspNetCore.App` and a project reference to `Frank`; no dependency on `Frank.Auth`, `Frank.Rdf`, or `Frank.OpenApi`

## Installation

```bash
dotnet add package Frank.Alps
```

## Quick Start

```fsharp
open Frank.Builder
open Frank.Alps

module Catalog =
    let openState = semantic "open" |> def "https://tictactoe.example/states/open"
    let closedState = semantic "closed" |> def "https://tictactoe.example/states/closed"

    let viewGame = safe "viewGame"
    let makeMove = unsafe "makeMove" |> from [ openState ] |> rt closedState

    // Transitions may be authored at the top level or nested under the state they act on.
    let game = semantic "game" |> doc "A tic-tac-toe game" |> contains [ viewGame; makeMove ]

webHost args {
    useDefaults

    resource "/games/{id}" {
        // Advertises the per-resource excerpt available at this same url.
        link (fun ctx ->
            Seq.singleton
                { Target = string ctx.Request.Path
                  Rel = "profile"
                  Params = [ "type", "application/alps+json" ] })

        get (
            negotiate {
                accepts "application/json" (handler {
                    handle getGameJson
                    binds Catalog.viewGame
                })

                accepts "application/alps+json" (Alps.excerpt None)
            }
        )

        post (handler {
            handle makeMoveHandler
            binds Catalog.makeMove
        })
    }

    useAlps [ Catalog.openState; Catalog.closedState; Catalog.game ]
}
```

A `descriptor { }` computation expression offers the same vocabulary as the `|>` combinators above, producing an identical `Descriptor`:

```fsharp
descriptor "makeMove" { unsafe; from [ Catalog.openState ]; rt Catalog.closedState }
```

## API Reference

### Authoring Operations

- `semantic` / `safe` / `unsafe` / `idempotent` `"id"` — Constructs a descriptor of that `type` (`semantic` is the spec's default). Also available as zero-argument custom operations inside `descriptor { }`
- `doc "text"` / `docWith { Value; Href; Format; ContentType; Tag }` — Human-readable documentation
- `def "iri"` — The descriptor's source-definition IRI
- `named` / `rel` / `tag` — `name`, `rel`, and `tag` from draft-07 §2.2
- `ext "id" "value"` / `extWith { Id; Href; Value; Tag }` — Author-specific extension data
- `link "href" "rel"` / `linkWith { Href; Rel; Title; Tag }` — An RFC 8288 web link, distinct from descriptor inheritance
- `contains [ children ]` — Nests descriptors (draft-07 §2.2.4), deliberately unrestricted by child type
- `rt target` / `href target` / `from [ sources ]` — Descriptor-typed references: a dangling one is a compile error, not a wrong document. `hrefExternal "uri"` is the escape hatch for a document this codebase doesn't own
- `initial` / `regions [ children ]` — Composite-state structure: the default child of a `contains` list, and orthogonal (AND) rather than substate (OR) decomposition. Both ride `ext` under `https://frank-fs.github.io/alps-ext/`, so documents stay spec-valid for readers that don't know them

### Binding Transitions to Resources

`binds descriptor`, inside a `handler { }` block, records which transition an endpoint implements. That binding is what makes the rest work:

- **Startup validation**: a transition's authored `type` is checked against its bound HTTP method (`safe` → GET/HEAD, `idempotent` → PUT/DELETE, `unsafe` → POST). A mismatch fails host startup rather than serving a wrong document. A transition in the profile that nothing binds is logged as a startup warning and omitted from the document.
- **Authorization filtering**: reads stock `IAuthorizeData`/`AuthorizationPolicy` endpoint metadata, so it works with `Frank.Auth` without referencing it and equally with a plain `AuthorizeAttribute`. Evaluation failures deny. Filtering applies at every depth of the profile, so a guarded transition nested under a semantic state is hidden too. Semantic descriptors are never filtered — vocabulary, not capability.

### Two HTTP Exposures

- **App-wide document** — `useAlps [ ... ]` serves the whole profile at `/.well-known/alps.json` (configurable) and advertises it with a `Link: rel="profile"` response header. Filtered by authorization only; there is no resource instance in scope to have a state.
- **Per-resource excerpt** — `Alps.excerpt resolver`, wired into a `negotiate { }` block's `accepts "application/alps+json"` case, serves just the transitions bound to *this* resource's route (every HTTP method's, not only the one it runs under). Filtered by authorization *and*, when `resolver` is `Some`, by state.

Both emit `Cache-Control: private, no-cache` and `Vary: Authorization` whenever any bound endpoint is guarded.

### State-Based Filtering

`from [ states ]` marks a transition valid only from the given source state(s); a transition with no `from` is never state-filtered. `CurrentStateResolver` — a plain `string -> Uri option`, wired at composition time — answers "what state is this specific resource in":

```fsharp
let resolver: CurrentStateResolver =
    fun resourceIri -> if isFinished resourceIri then Some closedIri else Some openIri

accepts "application/alps+json" (Alps.excerpt (Some resolver))
```

No dependency on any store: the natural implementation queries a provenance or event store, and an absent resolver (or one returning `None`) simply means state filtering does not apply. Matching walks `contains` ancestry rather than requiring exact equality, so being in a substate satisfies a transition declared `from` any of its ancestors.

`ProtocolGraph.ofProfile` derives the read-only `{ FromState; Transition; ToState }` edge set from the authored profile — one edge per `from` state on a transition that also declares `rt`. Nothing in this package executes a transition or owns what state a resource is actually in.

## Architecture

Frank.Alps is built on:

1. **Frank**: Provides the computation expression framework for defining HTTP resources and the `HandlerDefinition`/endpoint-metadata mechanism `binds` writes into
2. **ASP.NET Core**: `Endpoint`/`EndpointDataSource` are read directly (`EndpointSurface`) rather than through an ApiExplorer dependency

There is no dependency on `Frank.Rdf`, `Frank.JsonHome`, `Frank.Provenance`, or `Frank.Auth` — `AuthorizationFilter` reads stock ASP.NET Core authorization metadata, so it composes with `Frank.Auth` without referencing it.

See `sample/Frank.Alps.Sample` for a runnable demonstration of both HTTP exposures and both `Link` headers.

## Related Projects

- [Frank](https://github.com/frank-fs/frank) - F# web framework
- [ALPS](https://datatracker.ietf.org/doc/draft-amundsen-richardson-foster-alps/) - Application-Level Profile Semantics (draft-07)
- [RFC 6906](https://www.rfc-editor.org/rfc/rfc6906) - The 'profile' Link Relation Type

## License

MIT License - see LICENSE file for details
