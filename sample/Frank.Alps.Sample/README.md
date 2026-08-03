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

## What this sample deliberately doesn't do

**No `CurrentStateResolver`.** The sample passes `Alps.excerpt None`, so `makeMove`'s
`from [ openState ]` declaration is serialized but never enforced -- there is no provenance
or event store here to ask "what state is *this* game in". Passing
`Alps.excerpt (Some resolver)` is what makes the excerpt drop `makeMove` for a finished
game; see `Excerpt.fsi` and the README's *State-based filtering* section.

**No authorization.** Every endpoint is public, so both exposures serve the same document
to every caller and neither emits `Cache-Control: private, no-cache` / `Vary: Authorization`
(those appear only when a bound endpoint actually carries authorization metadata). Add a
`requireRole` from `Frank.Auth` to the POST handler and `makeMove` disappears from both
documents for anyone lacking that role.

**No real game.** `getGameJson` echoes the route id and `makeMoveHandler` always answers
`{ "ok": true }`. This sample exists to prove out `Frank.Alps` and Frank core's
negotiation/link mechanisms, not to be tic-tac-toe's real API.
