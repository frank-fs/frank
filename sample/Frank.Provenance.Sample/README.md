# Frank.Provenance Sample

Demonstrates [`Frank.Provenance`](../../src/Frank.Provenance): recording a PROV-O
`ProvenanceRecord` on every real request that touches a resource, and querying the recorded
activities back out as JSON-LD via `IProvenanceStore.Query`. It reuses the same tiny
`games` domain as [`Frank.Rdf.Sample`](../Frank.Rdf.Sample) -- the same two in-memory entries,
the same `gameUri` convention (`{baseUri}/games/{id}`) -- so the two samples read as companion
pieces: `Frank.Rdf.Sample` demonstrates authoring and negotiating RDF, this one demonstrates
recording and querying provenance about resources on top of it.

`GET /games/{id}` appends one `ProvenanceRecord` per successful lookup: a freshly minted
Activity IRI (`{baseUri}/activities/{guid}`, unique per request), the game's own IRI as the
Resource, a single fixed `{baseUri}/agents/anonymous` as the Agent (there's no real auth in
this sample -- see `Frank.Auth`'s sample for that), and `ActivityType = https://schema.org/ViewAction`.
`GET /provenance?resource={iri}` queries `ProvenanceQuery.ByResource` for that IRI and
serializes the resulting dotNetRDF graph as JSON-LD.

## Run it

```bash
dotnet run --project sample/Frank.Provenance.Sample/
```

## Try it

The important thing to see here isn't any single response -- it's that `Append` and `Query`
are talking to the *same* store across independent HTTP requests: viewing a game a second time
adds a second recorded activity to what the next `/provenance` query returns.

```bash
# Before anyone has viewed game 2, its provenance is an empty graph -- not an error.
curl -s "http://localhost:5000/provenance?resource=http://localhost:5000/games/2" | jq
# []

# View game 2 once. This appends a ProvenanceRecord to the store.
curl -s http://localhost:5000/games/2 | jq
# {"id":"2","name":"Connect Four","numberOfPlayers":2}

# Query provenance for game 2 again: one activity now shows up, wasGeneratedBy-linked to the
# game's own IRI, typed both prov:Activity and schema:ViewAction, with the fixed anonymous agent.
curl -s "http://localhost:5000/provenance?resource=http://localhost:5000/games/2" | jq

# View the SAME game a second time...
curl -s http://localhost:5000/games/2 | jq

# ...and query provenance once more: now TWO activities are recorded against games/2 -- proof
# the append/query loop is real and accumulates across independent requests, not faked.
curl -s "http://localhost:5000/provenance?resource=http://localhost:5000/games/2" | jq
# "prov:wasGeneratedBy" now lists two distinct activity IRIs, each with its own
# startedAtTime/endedAtTime/wasAssociatedWith.

# The response is genuinely JSON-LD -- the Content-Type says so.
curl -i "http://localhost:5000/provenance?resource=http://localhost:5000/games/2" | grep -i Content-Type
# Content-Type: application/ld+json

# A resource that was never viewed also comes back as an empty graph, not a 404 or a 500 --
# querying provenance is independent of whether the resource id itself is "real".
curl -s "http://localhost:5000/provenance?resource=http://localhost:5000/games/nobody-viewed-this" | jq
# []

# Missing the `resource` query parameter entirely is a real 400, not a crash.
curl -i "http://localhost:5000/provenance"
# HTTP/1.1 400 Bad Request
# {"error":"missing required query parameter 'resource'"}

# A game id that doesn't exist in the tiny in-memory dict is a plain 404 -- and, correctly,
# records no provenance at all (there's nothing real to attribute an activity to).
curl -i http://localhost:5000/games/999
# HTTP/1.1 404 Not Found
```

## What this sample still doesn't do

No auth and no real agents -- every activity is attributed to a single fixed
`agents/anonymous` IRI, which is honest about the absence of a real user model rather than
fabricating one. No `ActivityTypeResolver` or automatic capture middleware that infers
`ActivityType`/records provenance for you from route metadata -- that's follow-on-plan scope
for `Frank.Provenance`, not something this sample builds ahead of it; here, `recordView` calls
`store.Append` explicitly, by hand, in the handler. No persistence -- `MailboxProcessorProvenanceStore`
is in-memory only, and every recorded activity is lost on restart, same as the `games` dict
itself. No `ByAgent`/`ByActivityId` querying exposed over HTTP -- only `ByResource`, since that's
the shape this sample's `/provenance?resource=` endpoint needs to prove the record/query loop
works; the other two `ProvenanceQuery` cases are already covered by
`Frank.Provenance`'s own unit tests.
