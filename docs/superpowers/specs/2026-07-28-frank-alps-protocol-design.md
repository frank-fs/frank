# Frank.Alps — ALPS and the protocol direction

**Date**: 2026-07-28
**Branch**: `json-home` (design only — not scheduled)
**Status**: Draft — design captured, implementation deferred

## Why this is its own document

ALPS was originally scoped alongside JSON Home in a single `Frank.Discovery` package. It got split out for two reasons.

**It kept moving.** Across one design session the ALPS design went from derived-from-types, to authored-with-string-references, to authored-with-typed-references, to authored-transitions-that-resources-implement. Each revision came from recognizing it was a larger idea than the previous framing allowed. JSON Home never moved.

**It is not a second document format.** ALPS's four descriptor types plus `rt` are a state machine: semantic descriptors are states, `safe`/`unsafe`/`idempotent` descriptors are edges, `rt` names the target state. [app-state-diagram](https://www.app-state-diagram.com/manuals/1.0/en/reference.html) exists precisely because an ALPS profile already *is* a state diagram. So ALPS is the front end of a protocol/statechart model, and belongs with that work rather than next to a document format.

JSON Home ships first and independently — see [Frank.JsonHome](2026-07-28-frank-jsonhome-design.md).

### Reference specifications

| Spec | Version | Media type |
|---|---|---|
| Application-Level Profile Semantics | [draft-amundsen-richardson-foster-alps-07](https://datatracker.ietf.org/doc/draft-amundsen-richardson-foster-alps/) | `application/alps+json` |
| The 'profile' Link Relation Type | [RFC 6906](https://www.rfc-editor.org/rfc/rfc6906) | — |

## The design

### Descriptors are authored values

ALPS contains no URLs, HTTP methods, or status codes. Vocabulary is authored as plain F# values with `|>` combinators — `doc`, `def`, `tag`, `rel`, `contains`, `rt`:

```fsharp
module Catalog =
    let productId     = semantic "productId"   |> def "https://schema.org/productID"
    let productName   = semantic "productName" |> doc "Display name"
    let price         = semantic "price"       |> doc "Price in minor units"
    let product       = semantic "product"     |> contains [ productId; productName; price ]

    let listProducts  = safe   "listProducts"  |> rt product
    let createProduct = unsafe "createProduct" |> rt product
    let replaceProduct = idempotent "replaceProduct" |> rt product
```

All four constructors — `semantic`, `safe`, `unsafe`, `idempotent` — so a profile is a complete protocol description whether or not anything implements it. That is the normal state of a design document.

`rt` takes a **descriptor value**, not a string. Dangling fragment references become compile errors rather than silently wrong documents. Only a descriptor's own id is a string, because it is minting a name and there is nothing to check it against.

### Resources implement transitions

```fsharp
resource "/products" {
    rel "tag:example.com,2026:products"
    get  (handler { handle listHandler;   binds Catalog.listProducts })
    post (handler { handle createHandler; binds Catalog.createProduct })
}

useAlps [ Catalog.product; Catalog.listProducts; Catalog.createProduct ]
```

The profile stands alone and can be diagrammed, diffed, or reviewed with no application wired up. Resources *implement* it; they don't constitute it.

### `type` is validated, not derived

An earlier version derived a transition's `type` from the HTTP method it was bound to. Deriving can never disagree with reality — but it can never *catch* anything either. Authoring the type and validating the binding at startup rejects a `safe` transition bound to POST, which is a real design error the deriving version would silently accept:

| Descriptor type | Valid methods |
|---|---|
| `safe` | GET, HEAD |
| `idempotent` | PUT, DELETE |
| `unsafe` | POST |

### Output

```json
{ "alps": { "version": "1.0", "descriptor": [
    { "id": "product", "descriptor": [
        { "id": "productId", "def": "https://schema.org/productID" },
        { "id": "productName", "doc": { "value": "Display name" } },
        { "id": "price", "doc": { "value": "Price in minor units" } } ] },
    { "id": "listProducts",  "type": "safe",   "rt": "#product" },
    { "id": "createProduct", "type": "unsafe", "rt": "#product" } ] } }
```

`type` is omitted where it would be `semantic`, which the draft treats as the default.

## Authorization filtering

The same mechanism JSON Home uses: combine the bound endpoint's `IAuthorizeData` and `AuthorizationPolicy` metadata, evaluate with `IAuthorizationService` against the current principal, drop transitions that fail. No `Frank.Auth` reference.

**Semantic descriptors are never filtered.** They are vocabulary, not capability; a term is not a secret, and pruning them would make the anonymous and authenticated documents gratuitously hard to diff.

Note what this filtering *is*: "which transitions are available to you right now" is the state-machine question with the principal as the only state variable. Adding resource state to that predicate later is the same mechanism, not a new one. That seam is the reason this design is worth keeping intact even though it isn't scheduled.

The same cache hazard applies — `Cache-Control: private, no-cache` and `Vary: Authorization` whenever filtering is active.

## Why vocabulary is authored, not derived

[Spring Data REST](https://docs.spring.io/spring-data/rest/reference/metadata.html) — the largest real ALPS implementation — auto-generates descriptors, one per entity attribute, serving a profile per resource. So why not reflect the F# types already declared via `produces` / `accepts`?

Because that describes the wrong thing. An ALPS profile explains the semantics of a **hypermedia document** — HTML, HAL, Collection+JSON, Siren. An arbitrary F# response record is not a hypermedia type, and reflecting its fields yields a description of a serialization shape wearing ALPS vocabulary. Spring gets away with it because its representations *are* generated from entities it owns end to end; the entity and the hypermedia document are the same artifact. Frank has no such coupling.

The derivation source that would genuinely make sense is HTML templates — that is the document ALPS exists to annotate. Frank deliberately has no dedicated template language: Hox, Oxpecker.ViewEngine, Giraffe, and Falco are separate integrations, by design. There is nothing single to derive from, and building four derivations would re-couple Frank to view engines it intentionally keeps at arm's length.

## The paired analyzer

Authoring's one real cost is drift: rename a field in a Hox template and the profile silently lies. `Frank.Analyzers` (FSharp.Analyzers.SDK, per spec 009) is the natural home for closing that — an analyzer that inspects authored profiles against view-engine templates or format serializers and reports descriptor ids appearing in one and not the other.

The authored-values design makes this materially easier than the earlier sketches would have. Descriptors are ordinary F# bindings with literal ids, so an analyzer can resolve them from the syntax tree without evaluating anything; and `rt` references are already compiler-checked, so the analyzer only has to check profile-against-template, not profile-against-itself.

This spans every view engine Frank integrates with, so it is its own work item.

## What this needs from Frank core

Both are proposed in the [JSON Home design](2026-07-28-frank-jsonhome-design.md) and may land before this work.

1. **Extensible `HandlerDefinition`** — collapsed to `{ Handler; Metadata: obj list }`. `binds` has nowhere to write otherwise. This was ALPS's original motivation; JSON Home does not need it.
2. **`IResponseLinkProvider`** — ALPS documents are advertised with `Link: <...>; rel="profile"` per RFC 6906, which wants the shared link mechanism rather than a second middleware fighting over the header.

## Open questions for when this is picked up

- **One profile or many?** This design assumes one application-wide profile at `/.well-known/alps.json`. Spring Data REST serves one per resource. Per-resource profiles interact with the state-machine framing in ways worth thinking through before choosing.
- **Where does resource state enter?** Authorization filtering is a degenerate affordance filter. Making it a general one needs a notion of current state that Frank does not have today, and that is the actual protocol/statechart work.
- **Does the profile belong in a `Frank.Alps` package or inside the protocol model?** Depends on whether the protocol model ships as its own library.
