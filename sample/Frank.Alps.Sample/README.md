# Frank.Alps Sample

Demonstrates [`Frank.Alps`](../../src/Frank.Alps): a hand-authored ALPS profile for a
tic-tac-toe game -- two states (`open`/`closed`), one semantic descriptor for the resource
itself, and two transitions bound to the endpoints that implement them (`viewGame` on GET,
`makeMove` on POST, valid only `from` the `open` state).

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

**No authorization.** Every endpoint is public, so both exposures serve the same document
to every caller and neither emits `Cache-Control: private, no-cache` / `Vary: Authorization`
(those appear only when a bound endpoint actually carries authorization metadata). Add a
`requireRole` from `Frank.Auth` to the POST handler and `makeMove` disappears from both
documents for anyone lacking that role.

**No real game.** `getGameJson` echoes the route id and `makeMoveHandler` always answers
`{ "ok": true }`. This sample exists to prove out `Frank.Alps` and Frank core's
negotiation/link mechanisms, not to be tic-tac-toe's real API.
