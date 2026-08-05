# Frank.Alps — multi-document ALPS profiles (per-resource, per-role)

**Date**: 2026-08-04
**Branch**: not yet created — implementation needs a worktree/branch off `master`
**Status**: Draft — ready for implementation

## Context

frank-fs/frank#488. [2026-08-02-frank-alps-protocol-design.md](2026-08-02-frank-alps-protocol-design.md) shipped v1 with exactly one application-wide profile at `/.well-known/alps.json`, an intentional, scoped-down choice — see that doc's *Non-goals*: "Per-resource or multi-document profiles in v1... The `href`/`hrefExternal` split exists specifically so this can be added later without changing the `Descriptor` type."

Originating goal, carried from [[project_http_simplifies_statecharts]]: a protocol computation expression mixing multi-party session types and Harel statecharts into role-projected statecharts, built outside-in this time (ALPS first) rather than inside-out (the rolled-back `v740` actor/codegen line — [[feedback_outside_in_before_codegen]]). Two grounding shapes drove the design session:

1. **tic-tac-toe** (`C:/Users/ryanr/Code/tic-tac-toe`) — many boards, same shape, seats (X/O/Observer) assigned *per game*, not stable identities. Root/lobby doc + one shared board-protocol doc.
2. **Order fulfillment** (not yet built) — buyer/seller/supplier/carrier, each a *stable*, identity-bound role across the whole order's life. The real test of role-projected views.

Key finding that reshaped scope: seats (tic-tac-toe) and roles (order fulfillment) are not the same mechanism. A seat is scoped to one resource instance and has no ALPS-expressible identity; a role is an authenticatable principal attribute. This design only tackles the role case — tic-tac-toe's turn-gating is pure state, not role, and needs nothing new.

## What's already built (no new code)

Investigation during design turned up that most of what looked like new scope already ships:

- **Per-resource documents**: `Alps.excerpt` (`Excerpt.fsi`) already serves a live, filtered document scoped to whatever resource it's wired into — every `binds`-bound descriptor sharing the current endpoint's route pattern (`EndpointSurface.descriptorsForRoute`), filtered by state (`CurrentStateResolver`) and principal. `sample/Frank.Alps.Sample/Program.fs`'s `gameResource` already demonstrates this via `accepts "application/alps+json" (Alps.excerpt (Some stateResolver))`.
- **Per-role documents**: both `Alps.excerpt` (`Excerpt.fs:24`) and the full app-wide document handler (`AlpsDocument.fs:84`) already run `AuthorizationFilter.filter` against `ctx`'s principal, pruning any descriptor whose bound endpoint the caller isn't authorized for, and marking the response `Vary: Authorization` when the result would differ by principal (`AlpsDocument.fs:92`, `AuthorizationFilter.varies`). So two principals with different roles hitting the *same* URL already get different, correctly-filtered documents today.
- **Compile-checked cross-document authoring**: `Descriptor.href`/`Descriptor.hrefExternal` (`Descriptor.fsi:89-96`) and the `href`/`hrefExternal` CE operations (`DescriptorBuilder.fsi:65-69`) already exist, unused until now.
- **`Frank.Auth` role/claim assignment**: `resource { requireRole "..."  }` / `requireClaim` / `requirePolicy` (`Frank.Auth/ResourceBuilderExtensions.fsi`) already set the `IAuthorizeData`/`AuthorizationPolicy` endpoint metadata `AuthorizationFilter.isAllowed` reads. No integration code needed between `Frank.Auth` and `Frank.Alps` — they already compose through ASP.NET Core's own authorization metadata.

Net effect: the only genuinely missing piece is one serialization bug.

## The bug

`Serialization.fs`'s `toJson` (called by both `AlpsDocument.fs:99` and `Excerpt.fs:65` — one shared serializer, one fix covers both HTTP exposures) assumes every descriptor a `Descriptor` value references is present in the same `profile: Descriptor list` being serialized right now. True in v1 (one document, everything's local); false the moment a served document is a filtered subset (an excerpt, or a role-pruned full document) that omits a descriptor another kept descriptor still points at. Three call sites make this assumption:

1. `resolveHref` (`Serialization.fs:23-26`) — `DescriptorRef.Local target` always renders `"#" + target.Id"`.
2. `d.Rt` (`Serialization.fs:92`) — a transition's target state always renders `"#" + target.Id"`.
3. `stateExtPairs` (`Serialization.fs:56-68`) — each `from`-state's `protocolState`/`availableInStates` `ext` value always renders `"#" + state.Id"`.

A `Semantic` descriptor is never `binds`-bound to an endpoint (only transitions are — `AlpsDocument.fs:60-61`), so it never appears in any excerpt or role-pruned subset; only the full app-wide document ever contains it. Any excerpt/pruned document referencing shared vocabulary (a semantic descriptor) or a role-filtered-out transition's state will emit a dangling `#id` fragment under the current code.

## The fix

Since every served document (full or excerpt) is a filtered view of exactly one authored profile — no independently-authored second document exists in this design, deliberately (see *Non-goals*) — cross-reference resolution has exactly two cases: present in what's being serialized right now → `#id`; anything else → `rootDocUri#id`.

```fsharp
// Serialization.fs
let private idsIn (profile: Descriptor list) : Set<string> =
    DescriptorTree.flattenAll profile |> List.map (fun d -> d.Id) |> Set.ofList

let private resolveRef (rootUri: Uri) (present: Set<string>) (id: string) : string =
    if Set.contains id present then "#" + id
    else rootUri.ToString() + "#" + id

// resolveHref, d.Rt's writer, and stateExtPairs's ext-value construction
// all route through resolveRef instead of hardcoding "#" + id.

let toJson (rootUri: Uri) (profile: Descriptor list) : string = ...
```

`idsIn` is computed once per call (`DescriptorTree.flattenAll` already exists, used the same way `unboundTransitions`/`documentHandler` compute `profileIds` today — `AlpsDocument.fs:65`, `:78`).

`rootUri` needs to reach both call sites:
- `AlpsDocument.fs`'s `documentHandler` already knows its own `AlpsOptions.Path` — trivially in scope.
- `Excerpt.fs`'s `Alps.excerpt` does not currently know it. Needs a small DI registration: when `useAlps` composes (`AlpsDocument.fs`'s `UseAlps` member), register the resolved root `Uri` as a singleton, read back via `ctx.RequestServices` inside `Alps.excerpt` — same pattern `EndpointSurface` already uses to reach `IServiceProvider` at request time.

No change to `AlpsOptions`, `Descriptor`, `DescriptorRef`, or any public authoring combinator. `href`/`hrefExternal` keep their existing signatures; only the *serializer* changes.

## Deferred / explicitly out of scope

- **Tic-tac-toe seat-scoped documents.** Seats (X/O/Observer) are per-game, not stable identities — no ALPS-expressible role to hang a document or `requireRole` off. Tic-tac-toe's own multi-doc grounding is two documents only (root lobby + one shared board profile, structural `href` linking), turn-gating handled entirely by existing state-based `Alps.excerpt` filtering plus live HATEOAS affordances in the game representation itself — not a new ALPS mechanism. Left to the separate `tic-tac-toe` repo, owned by the user directly.
- **Hierarchical/composite states** (Harel nested states — `CompositeStateTests.fs` already exists). Not needed for ping/pong; a good candidate for order fulfillment later (e.g. "in transit" composite over pickup/warehouse/out-for-delivery). Not designed here.
- **Analyzer guardrail**: at most one `useAlps` per `webHost` and at most one per resource. Real gap, separate issue — not blocking this design.
- **Order fulfillment sample app.** Not built. The real test of *stable, multi-role* projection (vs. this design's two-role ping/pong proof); next milestone after this one lands.
- **A genuinely separate, independently-authored second document** (vs. a filtered view of one profile). Not needed for anything decided in this session — if it's ever needed, `hrefExternal` already covers documents this codebase doesn't own, and the `resolveRef`-to-single-root-doc assumption above would need revisiting.

## Sample: ping/pong (`sample/Frank.Alps.Sample`)

Proves the fix end-to-end. Added alongside the existing `gameResource` (not replacing it — that one demonstrates the separate `Frank.Provenance`/`CurrentStateResolver` integration from #493).

**Descriptors**:

```fsharp
module PingPong =
    let participant = semantic "participant" |> doc "A session participant"
    // unbound to any endpoint -- lives only in the root document, forces
    // every reference to it through the new cross-doc resolution path.

    let awaitingPing =
        semantic "awaitingPing" |> doc "Waiting for a ping"
        |> def "https://pingpong.example/states/awaitingPing"
    let awaitingPong =
        semantic "awaitingPong" |> doc "Waiting for a pong"
        |> def "https://pingpong.example/states/awaitingPong"
    let session = semantic "session" |> doc "A ping-pong session"

    let listSessions = safe "listSessions" |> rt session
    let createSession = unsafe "createSession" |> rt session
    let viewSession = safe "viewSession" |> rt session

    let ping =
        unsafe "ping" |> from [ awaitingPing ] |> rt awaitingPong |> href participant
    let pong =
        unsafe "pong" |> from [ awaitingPong ] |> rt awaitingPing |> href participant
```

**Resources**:

| Route | Method(s) | Descriptor(s) | Auth |
|---|---|---|---|
| `/sessions` | GET, POST | `listSessions`, `createSession` | none |
| `/sessions/{id}` | GET | `viewSession` (state-resolved via the existing Provenance `CurrentStateResolver` pattern) | none |
| `/sessions/{id}/ping` | POST | `ping` | `requireRole "pinger"` |
| `/sessions/{id}/pong` | POST | `pong` | `requireRole "pinger"` |

Separate sub-routes for `ping`/`pong` rather than one POST on `/sessions/{id}`: keeps the one-descriptor-per-endpoint mapping `binds` already assumes everywhere else in this package.

**What this proves**:
1. **Doc-linking**: `ping`/`pong`'s `href participant` — `participant` is absent from both the `/sessions/{id}/ping` and `/sessions/{id}/pong` excerpts (unbound, so never in `descriptorsForRoute`'s result) — exercises the `resolveRef` fallback to the root document's URI.
2. **State-gating**: `Alps.excerpt` on `/sessions/{id}/ping` shows `ping` only while state is `awaitingPing` — reuses the existing `CurrentStateResolver`/`Excerpt.satisfiesState` machinery unchanged.
3. **Role-projection**: a `pinger`-authenticated request to `/.well-known/alps.json` sees `ping` but not `pong` (and vice versa for `ponger`) — reuses the existing `AuthorizationFilter` integration in `AlpsDocument.fs` unchanged. Test-principal/claims setup follows `sample/Frank.JsonHome.Sample/ApiKeyAuth.fs`'s existing pattern.

Per `CLAUDE.md`'s package-deliverable rule, this stays inside the existing `Frank.Alps.Sample`/`Frank.Alps.Tests` pair — no new package, so no new README/sample obligation beyond updating what's there.

## Verification

- `Frank.Alps.Tests`: unit coverage for `resolveRef`/`idsIn` (present → `#id`, absent → `rootUri#id`) directly, plus an integration test serializing a deliberately-incomplete `profile` list and asserting no dangling `#id`.
- `Frank.Alps.Sample` + a Playwright/HTTP test (mirroring `SampleIntegrationTests.fs`): full ping/pong cycle — create session, alternate `ping`/`pong` as two differently-authenticated principals, assert 403 on wrong-turn and wrong-role calls, assert the served excerpt and full document each resolve `href`/`rt`/`from` references correctly at every step.
- Build across all three TFMs (`net8.0;net9.0;net10.0`) per project convention — signature mismatches on `toJson`'s new `rootUri` parameter only surface at compile time across targets.
