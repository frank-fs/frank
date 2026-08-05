# Frank.Alps Sample

Demonstrates [`Frank.Alps`](../../src/Frank.Alps) via two unrelated protocols registered on
one `useAlps` document: a tic-tac-toe game (below) and a ping/pong session protocol (see
*Ping/pong*, further down) that proves out cross-document reference resolution, state-gating,
and role-projection together.

The game: a hand-authored ALPS profile -- two states (`open`/`closed`), one semantic
descriptor for the resource itself, and two transitions bound to the endpoints that implement
them (`viewGame` on GET, `makeMove` on POST, valid only `from` the `open` state).

Its point is the **two HTTP exposures**, each advertised by its own `Link` header:

- **App-wide document** at `/.well-known/alps.json`, registered by `useAlps [ ... ]` and
  advertised on *every* response by an app-wide `Link: rel="profile"` header.
- **Per-resource excerpt** at `/games/{id}` itself, served by `Alps.excerpt` from inside a
  `negotiate { }` block and advertised by a *resource-scoped* `link` operation --
  `Link: </games/1>; rel="profile"; type="application/alps+json"`, same url as the primary
  representation, disambiguated by the `type` link-param. `rel="profile"` rather than
  `"alternate"` (which `Frank.Rdf.Sample` uses) because RFC 6906 defines `profile` for
  exactly this: the excerpt *is* this resource's profile, not another serialization of its
  data.

## Run it

```bash
dotnet run --project sample/Frank.Alps.Sample/
```

## Try it

```bash
# Two Link headers on one response: the app-wide one pointing at /.well-known/alps.json,
# and the resource-scoped one pointing at /games/1 itself.
curl -sD - -o /dev/null http://localhost:5000/games/1

# The app-wide document: the whole profile -- both states, the semantic "game"
# descriptor, and both transitions.
curl -s http://localhost:5000/.well-known/alps.json | jq

# The same app-wide Link header appears here too, since it applies to every response.
curl -sD - -o /dev/null http://localhost:5000/.well-known/alps.json

# The per-resource excerpt, at the SAME url as the JSON representation, selected purely
# by Accept: just the two transitions bound to /games/{id} -- viewGame (GET) and makeMove
# (POST) -- not the profile's vocabulary descriptors.
curl -s -H "Accept: application/alps+json" http://localhost:5000/games/1 | jq

# curl's default Accept (*/*), or an explicit "application/json", get the plain-JSON
# representation instead.
curl -s http://localhost:5000/games/1 | jq
```

Notice the excerpt lists `makeMove` even though the request was a GET: `Alps.excerpt`
gathers every HTTP method's `binds`-bound descriptor for the *resource*, not just the one
method it happens to be running under. That is the whole point -- a client reads one
document to learn what it may do next, not one per method.

Notice too that `makeMove` carries `protocolState`/`availableInStates` `ext` elements
pointing at `#open`. Those are projected automatically from its `from [ openState ]`
declaration at serialization time; they are not authored separately.

## `CurrentStateResolver`, backed by `Frank.Provenance` (frank-fs/frank#493)

This sample passes `Alps.excerpt (Some stateResolver)` -- a real `CurrentStateResolver`
backed by a `MailboxProcessorProvenanceStore` (`Frank.Provenance`), so `makeMove`'s
`from [ openState ]` declaration is genuinely enforced, not just serialized. `POST
/games/{id}` records a provenance activity typed `Catalog.closedState`'s own `def` IRI;
`stateResolver` answers "what state is this game in" by asking the store for the most
recently recorded activity against that resource (`ProvenanceQuery.Latest`) and reading
its domain type back off the returned graph.

This wiring is glue code that lives in this sample, not in either package: `Frank.Alps`
has no reference to `Frank.Provenance` and vice versa (see both packages' design docs) --
only this application depends on both, matching how a real consumer would compose them.

```bash
# Before any move: the excerpt still offers makeMove -- no provenance recorded yet, so
# stateResolver returns [] and state-filtering does not apply (same as CurrentStateResolver
# absent entirely).
curl -s -H "Accept: application/alps+json" http://localhost:5000/games/1 | jq '.alps.descriptor[].id'

# Record a move.
curl -s -X POST http://localhost:5000/games/1 -o /dev/null

# After the move: makeMove is gone. stateResolver now reports "closed" (the ActivityType
# recorded above), which does not satisfy makeMove's from [ openState ] guard.
curl -s -H "Accept: application/alps+json" http://localhost:5000/games/1 | jq '.alps.descriptor[].id'
```

## What this sample still doesn't do

**No real game.** `getGameJson` echoes the route id and `makeMoveHandler` always answers
`{ "ok": true }`. This sample exists to prove out `Frank.Alps` and Frank core's
negotiation/link mechanisms, not to be tic-tac-toe's real API.

## Ping/pong: doc-linking, state-gating, and role-projection together (frank-fs/frank#488)

A second, unrelated protocol added alongside the game, registered on the same `useAlps`
document. Where the game demonstrates per-resource excerpts and `Frank.Provenance` state
filtering, ping/pong demonstrates the three pieces the game sample *doesn't* cover:

- **Cross-document `href`/`rt` resolution.** `PingPong.participant` is a `semantic`
  descriptor that is never `binds`-bound to any endpoint -- it exists purely so `ping` and
  `pong` can `href participant`. Because it's unbound, it never appears in any per-resource
  excerpt (only in the full document), so every excerpt referencing it has to resolve that
  reference against `/.well-known/alps.json#participant` instead of a local `#participant`
  fragment. Same story for `rt`: `ping`'s `rt awaitingPong` and `pong`'s `rt awaitingPing`
  point at states that are likewise excerpt-absent (states are never `binds`-bound). This is
  the fix this plan's Tasks 1-2 made to `Serialization.toJson`, `AlpsDocument.fs`, and
  `Excerpt.fs` -- before that fix, these would have serialized as dangling `#participant` /
  `#awaitingPong` fragments.
- **State-gating.** `ping` declares `from [ awaitingPing ]`, `pong` declares
  `from [ awaitingPong ]`. A `Frank.Provenance`-backed `CurrentStateResolver` (same pattern
  as the game's `stateResolver`, reusing the same store instance -- see below) makes
  `/sessions/{id}/ping`'s excerpt genuinely stop offering `ping` once a ping has been
  recorded, and `/sessions/{id}/pong` pick it up in turn.
- **Role-projection.** `/sessions/{id}/ping` and `/sessions/{id}/pong` each carry a
  `requireRole` (`Frank.Auth`) -- `"pinger"` and `"ponger"` respectively. The full document
  at `/.well-known/alps.json` prunes `ping`/`pong` per caller: a `pinger`-authenticated
  request sees `ping` but not `pong`, and vice versa.

### Try it

```bash
dotnet run --project sample/Frank.Alps.Sample/

# Create a session.
curl -s -X POST http://localhost:5000/sessions
# => {"id":"<guid>"}
ID=<guid-from-above>

# List sessions.
curl -s http://localhost:5000/sessions | jq

# The excerpt at .../ping: "ping" is present, and its href/rt resolve into the root document
# even though "participant"/"awaitingPong" never appear in this excerpt itself.
curl -s -H "X-Api-Key: pinger-key" -H "Accept: application/alps+json" \
  http://localhost:5000/sessions/$ID/ping | jq

# Wrong role: 403.
curl -s -o /dev/null -w '%{http_code}\n' -H "X-Api-Key: ponger-key" \
  -H "Accept: application/alps+json" http://localhost:5000/sessions/$ID/ping

# Anonymous: 401.
curl -s -o /dev/null -w '%{http_code}\n' -H "Accept: application/alps+json" \
  http://localhost:5000/sessions/$ID/ping

# Ping.
curl -s -X POST -H "X-Api-Key: pinger-key" http://localhost:5000/sessions/$ID/ping

# State-gated: "ping" is now absent from its own excerpt (session moved to awaitingPong).
curl -s -H "X-Api-Key: pinger-key" -H "Accept: application/alps+json" \
  http://localhost:5000/sessions/$ID/ping | jq

# "pong" is now available in its place.
curl -s -H "X-Api-Key: ponger-key" -H "Accept: application/alps+json" \
  http://localhost:5000/sessions/$ID/pong | jq

# Pong -- session returns to awaitingPing.
curl -s -X POST -H "X-Api-Key: ponger-key" http://localhost:5000/sessions/$ID/pong

# Role-projection on the FULL document: a pinger sees "ping" but not "pong", and vice versa.
curl -s -H "X-Api-Key: pinger-key" http://localhost:5000/.well-known/alps.json | jq '.alps.descriptor[].id'
curl -s -H "X-Api-Key: ponger-key" http://localhost:5000/.well-known/alps.json | jq '.alps.descriptor[].id'
```

### Test principals

Demo-only "X-Api-Key" authentication (`PingPongAuth` in `Program.fs`, the same shape as
`sample/Frank.JsonHome.Sample/ApiKeyAuth.fs`): key `pinger-key` maps to role `pinger`, key
`ponger-key` maps to role `ponger`. No header at all is anonymous.

### One `Frank.Provenance` store, two protocols

Ping/pong reuses the same `MailboxProcessorProvenanceStore` instance the game resource
already uses, rather than standing up a second store. The existing `stateResolver`
convention -- "the domain `rdf:type` asserted on the most recently ended activity that
`prov:wasGeneratedBy` this resource" -- says nothing about there being only one state
machine live in a store; it extends cleanly to ping/pong's own two alternating states as
long as the two protocols' resource IRIs never collide, which a distinct base IRI
(`https://pingpong.example` vs. tic-tac-toe's `https://tictactoe.example`) guarantees. A
second store instance would only be justified by an isolation requirement (e.g. separate
persistence/retention per protocol) that neither protocol has here.

One wrinkle the game sample doesn't have: ping/pong exposes *three* routes
(`/sessions/{id}`, `.../ping`, `.../pong`) for one session identity, where the game has
exactly one route per game. `pingPongStateResolver` strips the `/ping`/`/pong` suffix
before building the resource IRI, so a POST to `.../ping` and a later GET excerpt at
`.../pong` (or the plain `/sessions/{id}` view) all resolve to the same provenance
resource.
