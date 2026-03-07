# How is Frank.Statecharts different from Webmachine or Freya?

## Executive Summary

Frank.Statecharts, [Webmachine](https://github.com/webmachine/webmachine), and [Freya](https://github.com/xyncro/freya) all use state machines in the context of HTTP resource handling, but they operate at fundamentally different levels of abstraction.

**Webmachine** (Erlang, 2009) and **Freya** (F#, 2014) model the *HTTP protocol itself* as a fixed decision graph. Their state machine is the HTTP specification: a flowchart of ~50 decision nodes that determines the correct status code for any request. The developer overrides callbacks (`resource_exists`, `is_authorized`, `allowed_methods`, etc.) and the framework walks the graph to produce a compliant HTTP response. The state machine is the same for every resource — only the callback answers change.

**Frank.Statecharts** models *application-level resource state* — the domain states that a resource moves through over its lifetime (e.g., a game progressing from `XTurn` to `OTurn` to `Won`). Each resource instance has its own persisted state, and the HTTP surface (available methods, status codes, representations) changes based on that state. The framework maps domain state transitions to HTTP semantics, while ASP.NET Core's middleware pipeline handles protocol-level concerns (routing, authentication, content negotiation) in the layer beneath.

This is a deliberate design choice. Frank is a lightweight library for building hypermedia web applications on ASP.NET Core, not a replacement for the HTTP framework underneath it. Where Webmachine and Freya re-implement HTTP compliance from scratch, Frank.Statecharts builds on the protocol compliance that ASP.NET Core already provides and adds the application-level state awareness that existing frameworks lack.

## Comparison Table

| Aspect | Webmachine | Freya | Frank.Statecharts |
|--------|-----------|-------|-------------------|
| **What the state machine models** | HTTP protocol decision tree (fixed) | HTTP protocol decision graph (fixed, composable) | Application domain state machine (user-defined) |
| **States** | ~50 HTTP decision nodes (`v3b13`..`v3p11`) | Layered specifications (Assertions, Permissions, Validations, etc.) | Domain states as F# discriminated unions (`XTurn`, `OTurn`, `Won`) |
| **Transitions** | Request processing flow through the fixed graph | Binary decision nodes with left/right branches | Domain events (`MakeMove`, `StartGame`) triggering state changes |
| **Per-instance state** | No — each request is independent | No — each request is independent | Yes — each resource instance has persisted state |
| **Guards** | Protocol-level callbacks (`is_authorized`, `is_conflict`) | Protocol-level decisions (`authorized`, `allowed`, `exists`) | Domain-level predicates (`isPlayersTurn`, `isParticipant`) |
| **Guard → HTTP mapping** | Implicit in graph position (e.g., `is_authorized` false → 401) | Implicit in specification terminal nodes | Explicit `BlockReason` → status code mapping (403, 409, 412, etc.) |
| **Method routing** | `allowed_methods` callback (resource-wide) | `methodsSupported` custom operation (resource-wide) | `inState` blocks with per-state method handlers |
| **Configuration approach** | Override ~30 resource callbacks with defaults | Computation expression writing to untyped `Map<string list, obj>` | Computation expression building typed `StatefulResourceSpec<'S,'E,'C>` record |
| **Abstraction layers** | 1 (callback → decision tree → response) | 6-7 (CE → untyped map → optics → Hephaestus → Hekate graph → optimized graph → execution) | 3 (CE → typed spec → metadata closures → middleware) |
| **State persistence** | None | None | `IStateMachineStore<'S,'C>` abstraction with `MailboxProcessor` default |
| **Observable hooks** | None | None | `onTransition` observers for state change events |
| **Platform** | Erlang/OTP, OWIN (.NET ports) | F#/OWIN | F#/ASP.NET Core |
| **Status** | Maintained (Erlang), .NET ports abandoned | Archived (2022) | Active development |

## Detailed Analysis

### Webmachine: HTTP Compliance as a Decision Tree

Webmachine's fundamental insight is that the HTTP specification already defines a complete decision procedure for handling any request. The [decision flowchart](https://github.com/webmachine/webmachine/wiki/Diagram) encodes this procedure as a directed acyclic graph where each node asks a yes/no question about the request or resource.

The developer implements a resource module by overriding callbacks, each with a sensible default:

```erlang
%% Erlang — Webmachine resource callbacks
resource_exists(ReqData, Context) -> {true, ReqData, Context}.
allowed_methods(ReqData, Context) -> {['GET', 'HEAD'], ReqData, Context}.
content_types_provided(ReqData, Context) -> {[{"text/html", to_html}], ReqData, Context}.
is_authorized(ReqData, Context) -> {true, ReqData, Context}.
```

The correct HTTP response — including status code, headers, and negotiated content type — emerges automatically from the graph traversal. A developer who overrides only `content_types_provided` and `to_html` gets correct 200, 304, 404, 405, and 406 responses without writing any status code logic.

**Strengths:**
- HTTP compliance is the framework's responsibility, not the developer's
- Sensible defaults mean minimal code for simple resources
- The fixed decision tree is well-documented and predictable

**Limitations:**
- No concept of per-instance resource state that persists across requests
- Every request is independent — the framework cannot express "this resource is in state X, so different methods are available"
- The decision tree is fixed; extending it requires modifying the framework itself

### Freya: Compositional HTTP Machines in F#

Freya brought the Webmachine concept to F# with a more compositional architecture. Instead of a single callback interface, Freya decomposed the HTTP decision graph into pluggable components assembled via a graph execution engine called [Hephaestus](https://github.com/xyncro/hephaestus).

```fsharp
// F# — Freya HTTP machine configuration
let resource =
    freyaHttpMachine {
        methodsSupported [ GET; HEAD; OPTIONS ]
        handleOk (fun _ -> freya {
            return {
                Description = { Charset = Some Charset.Utf8
                                Encodings = None
                                MediaType = Some MediaType.Html
                                Languages = None }
                Data = bytes }
        })
        authorized (freya { return true })
        exists (freya { return true })
    }
```

The architecture was deeply layered:

1. The `freyaHttpMachine { }` computation expression wrote configuration into an untyped `Map<string list, obj>`
2. Specification modules read this configuration through [Aether](https://github.com/xyncro/aether) optics (functional lenses)
3. Hephaestus compiled specifications into a [Hekate](https://github.com/xyncro/hekate) graph data structure
4. The graph was optimized (literal elimination, unreachable branch pruning)
5. The optimized graph was deconstructed back into a recursive machine structure
6. The machine was executed by recursive traversal threading state through decisions

**Strengths:**
- Composable: method-specific components (GET/HEAD, POST, PUT, DELETE) could be independently assembled
- Static/Dynamic value distinction enabled compile-time optimization of constant decisions
- Clean computation expression surface for the developer

**Limitations:**
- Six to seven layers of abstraction between user code and HTTP response made debugging opaque
- The untyped configuration map (`Map<string list, obj>`) sacrificed type safety — misconfiguration was invisible until runtime
- Fragmented across 10+ repositories (freya-core, freya-machines, freya-optics, freya-types, freya-routers, hephaestus, hekate, aether)
- The generic graph engine (Hephaestus) added substantial complexity to support a generality that was never used beyond HTTP
- Like Webmachine, no concept of per-instance resource state

### Frank.Statecharts: Application-Level State Machines

Frank.Statecharts operates at a different level entirely. Rather than modeling the HTTP protocol, it models the *application domain* — the states a resource moves through during its lifetime and how those states affect the HTTP surface.

```fsharp
// F# — Frank.Statecharts stateful resource
let gameResource = statefulResource "/games/{id}" {
    machine gameMachine
    resolveInstanceId (fun ctx -> ctx.Request.RouteValues["id"] :?> string)

    inState (forState XTurn [
        StateHandlerBuilder.get Handlers.getGame
        StateHandlerBuilder.post Handlers.handleMove
    ])
    inState (forState OTurn [
        StateHandlerBuilder.get Handlers.getGame
        StateHandlerBuilder.post Handlers.handleMove
    ])
    inState (forState Won [
        StateHandlerBuilder.get Handlers.getGame
        // No POST — 405 Method Not Allowed automatically
    ])
    inState (forState Draw [
        StateHandlerBuilder.get Handlers.getGame
    ])

    onTransition (fun evt -> broadcastGameUpdate evt)
}
```

The state machine definition is separate from the resource, using standard F# types:

```fsharp
type StateMachine<'State, 'Event, 'Context> =
    { Initial: 'State
      Transition: 'State -> 'Event -> 'Context -> TransitionResult<'State, 'Context>
      Guards: Guard<'State, 'Event, 'Context> list
      StateMetadata: Map<'State, StateInfo> }
```

At runtime, the middleware:

1. Resolves the instance ID from the route
2. Retrieves the current state from `IStateMachineStore`
3. Checks whether the HTTP method is registered for the current state (405 if not, with `Allow` header)
4. Evaluates guards against the current user's `ClaimsPrincipal` (403, 409, etc. if blocked)
5. Invokes the state-specific handler
6. If the handler set an event, applies the transition and persists the new state
7. Fires `onTransition` observers

**Key differences from Webmachine and Freya:**

- **Per-instance persistence**: Each resource instance (identified by route parameter) has its own state stored in `IStateMachineStore`. State survives across requests.
- **State-dependent method availability**: Different HTTP methods are available depending on the resource's current state. A game in `Won` state returns 405 for POST because no POST handler is registered for that state.
- **Domain-level guards with HTTP semantics**: Guards evaluate application predicates (is it your turn?) and map results to specific HTTP status codes via `BlockReason` (`NotAllowed` → 403, `NotYourTurn` → 409, `PreconditionFailed` → 412).
- **Typed configuration throughout**: The computation expression builds a `StatefulResourceSpec<'State, 'Event, 'Context>` record — fully generic, fully typed, no untyped maps or optics indirection.
- **Three abstraction layers, not seven**: CE → typed spec record → metadata closures → middleware dispatch. The middleware is a straightforward `match` expression, not a graph traversal.

### How the Layers Compose

Frank.Statecharts does not replace protocol-level HTTP handling — it sits above it:

```
Request
  → ASP.NET Core routing (URL matching)
  → ASP.NET Core authentication (cookie/JWT/etc.)
  → ASP.NET Core authorization (policy evaluation)
  → Frank.Statecharts middleware (state lookup, method check, guard evaluation)
  → State-specific handler (domain logic)
  → Transition + persistence
Response
```

This is the same layering that a Webmachine user gets, but the protocol-level decisions are handled by ASP.NET Core's well-tested middleware pipeline rather than a custom decision graph. Frank.Statecharts adds the layer that neither Webmachine nor Freya provides: state-dependent resource behavior.

## Summary

Frank.Statecharts opts for application-level semantics in keeping with Frank's goals as a lightweight library for building hypermedia web applications. Where Webmachine and Freya sought to own the entire HTTP decision pipeline — re-implementing protocol compliance from the ground up — Frank.Statecharts trusts the platform underneath it and focuses on the problem that existing frameworks leave unsolved: how should a resource's HTTP surface change as the resource moves through domain states?

This is a philosophical alignment with how HATEOAS actually works in practice. The hypermedia constraint says that available actions should be discoverable from the current representation. A game in `XTurn` state offers a "make move" affordance; a game in `Won` state does not. Frank.Statecharts makes this constraint enforceable at the framework level: the middleware returns 405 for methods that are genuinely unavailable in the current state, not because of HTTP protocol rules, but because of application semantics.

The result is a framework that is deliberately narrower than Webmachine or Freya but deeper in the dimension that matters for stateful hypermedia applications. It does not attempt to be a general-purpose HTTP compliance engine. It provides typed, persistent, observable, guard-protected state machines that map cleanly to the HTTP interface — and leaves everything else to ASP.NET Core.

## References

- [Webmachine](https://github.com/webmachine/webmachine) — Erlang HTTP resource framework with decision flowchart
- [Webmachine Decision Diagram](https://github.com/webmachine/webmachine/wiki/Diagram) — The fixed HTTP decision graph
- [Freya](https://github.com/xyncro/freya) — F# HTTP machine framework (archived)
- [Hephaestus](https://github.com/xyncro/hephaestus) — Graph-based state machine engine used by Freya
- [Liberator](https://clojure-liberator.github.io/liberator/) — Clojure port of the Webmachine approach
- [Frank](https://github.com/frank-fs/frank) — F# computation expressions for ASP.NET Core
- [ALPS Specification](http://alps.io/spec/drafts/draft-01.html) — Application-Level Profile Semantics
- [XState / Stately](https://stately.ai/) — State machine validation and visual editing
- [SCXML W3C Recommendation](https://www.w3.org/TR/scxml/) — State Chart XML standard
