# Frank.Rdf Sample

Demonstrates [`Frank.Rdf`](../../src/Frank.Rdf): the `rdf { }` CE authoring RDF triples
across two subjects (a game and its `numberOfPlayers` value), `Doc.merge` folding in facts
shared across every resource, and `Doc.writeJsonLd` streaming straight into the response
body.

## Run it

```bash
dotnet run --project sample/Frank.Rdf.Sample/
```

## Try it

```bash
curl -s http://localhost:5000/games/1 | jq
curl -s http://localhost:5000/games/2 | jq
curl -i http://localhost:5000/games/999   # 404 -- not in the in-memory list
```

Notice the output is **expanded-form JSON-LD**: no `@context`, every predicate and type is
a full `https://schema.org/...` IRI rather than a short `schema:` name. That's a deliberate
choice of `Frank.Rdf` -- see the design doc's *Serialization* section for why.

## What this sample deliberately doesn't do

`GET /games/{id}` always returns JSON-LD, with no `Accept`-based negotiation for HTML or
plain JSON alternatives. That's not the recommended end state -- the design doc's *Serving
it over HTTP* section calls for real content negotiation, choosing a representation by
`Accept` the way ASP.NET's `MediaTypeFormatter`/`IOutputFormatter` tradition always
supported (different media types can mean entirely different representation-generation
code paths, not just different serializations of one object). Building that formatter here
would be throwaway work: the tic-tac-toe follow-on plan builds it for real. This sample's
job is `Frank.Rdf` itself, not the full response-negotiation story around it.
