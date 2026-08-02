# Frank.Rdf Sample

Demonstrates [`Frank.Rdf`](../../src/Frank.Rdf): the `rdf { }` CE authoring RDF triples
across two subjects (a game and its `numberOfPlayers` value), `Doc.merge` folding in facts
shared across every resource, and `Doc.writeJsonLd` streaming straight into the response
body. It also demonstrates real `Accept`-based content negotiation on `GET /games/{id}`:
a `negotiate { }` block serves a plain-JSON representation and the JSON-LD representation
at the *same* url, plus a resource-scoped `link` advertising the JSON-LD alternate.

## Run it

```bash
dotnet run --project sample/Frank.Rdf.Sample/
```

## Try it

```bash
# No Accept header, or an explicit "application/json", both get the plain-JSON
# representation -- a small DTO, no RDF involved.
curl -s http://localhost:5000/games/1 | jq
curl -s -H "Accept: application/json" http://localhost:5000/games/1 | jq

# Ask for "application/ld+json" and get the expanded-form JSON-LD representation
# instead, at the SAME url.
curl -s -H "Accept: application/ld+json" http://localhost:5000/games/1 | jq

# Every response from this resource carries a Link header advertising the JSON-LD
# alternate -- regardless of which representation was actually returned, and
# regardless of whether the game id exists.
curl -i -H "Accept: application/ld+json" http://localhost:5000/games/1 | grep -i Link
curl -i http://localhost:5000/games/999 | grep -i Link   # 404 -- not in the in-memory list
```

Notice the JSON-LD output is **expanded-form**: no `@context`, every predicate and type is
a full `https://schema.org/...` IRI rather than a short `schema:` name. That's a deliberate
choice of `Frank.Rdf` -- see the design doc's *Serialization* section for why. The plain-JSON
representation, by contrast, is an ordinary DTO with no RDF vocabulary in it at all -- the
two representations are genuinely independent code paths, not one object serialized two ways.

## What this sample still doesn't do

No `text/html` representation, and no integration with the real tic-tac-toe game (`games`
here is a tiny in-memory dictionary, not the actual game store). Both are out of scope for
this sample, which exists to prove out `Frank.Rdf` and Frank core's negotiation/link
mechanisms, not to be tic-tac-toe's real API.
