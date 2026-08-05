# Frank.Validation Sample

Demonstrates [`Frank.Validation`](../../src/Frank.Validation): SHACL-validating an
`application/ld+json` request body before the handler ever runs, and answering a violating
request with a real `sh:ValidationReport`. It reuses the same tiny `games` domain as
[`Frank.Rdf.Sample`](../Frank.Rdf.Sample) and [`Frank.Provenance.Sample`](../Frank.Provenance.Sample)
-- the same two in-memory entries -- so the three read as companion pieces: `Frank.Rdf.Sample`
authors and negotiates RDF, `Frank.Provenance.Sample` records provenance about it, and this one
guards what may be written.

`POST /games/{id}/moves` accepts a `schema:MoveAction` body and is guarded by two hand-authored
shapes, covering four different constraint categories between them:

- **`moveShape`** targets `schema:MoveAction` and requires exactly one `schema:position` typed
  `xsd:integer` (`datatype` + `minCount` + `maxCount`), and exactly one `schema:agent` whose value
  must itself conform to `personShape` (`node` -- the recursive, shape-based constraint).
- **`personShape`** targets `schema:Person`, requires exactly one `schema:name` and allows at most
  one `schema:email`, and is **`closed`** -- any other predicate on a Person is a violation. It
  lists `rdf:type` in `closed`'s ignored properties, because SHACL does not implicitly exempt
  `rdf:type` from a closed shape and every `sh:targetClass`-matched node necessarily carries one.

`personShape` is both referenced by `moveShape` via `node` *and* passed to `Shacl.toShapesGraph` as
a top-level shape, so it validates standalone as well as nested -- the sharing pattern the design
doc recommends.

The handler reports `triplesValidated`, read back from the graph the middleware already parsed via
`Validation.tryGetValidatedGraph ctx`, to show that a handler never has to re-parse the body it was
handed.

## Run it

```bash
dotnet run --project sample/Frank.Validation.Sample/
```

## Try it

```bash
# A conforming move: one integer position, one agent that is a valid Person.
# 201, and the handler reports how many triples the middleware had already parsed for it.
curl -s -X POST http://localhost:5000/games/1/moves \
  -H 'Content-Type: application/ld+json' \
  -d '[{"@id":"http://localhost:5000/moves/1","@type":["https://schema.org/MoveAction"],
        "https://schema.org/position":[{"@value":4}],
        "https://schema.org/agent":[{"@id":"http://localhost:5000/people/alice",
          "@type":["https://schema.org/Person"],
          "https://schema.org/name":[{"@value":"Alice"}]}]}]' | jq
# {"accepted":true,"gameId":"1","triplesValidated":5}

# A missing position: 422, application/problem+json, with a flattened violations array.
# The handler never ran.
curl -s -X POST http://localhost:5000/games/1/moves \
  -H 'Content-Type: application/ld+json' \
  -d '[{"@id":"http://localhost:5000/moves/2","@type":["https://schema.org/MoveAction"]}]' | jq
# {"status":422,"title":"SHACL validation failed",
#  "type":"https://www.w3.org/TR/shacl/#validation-report",
#  "violations":[{"constraintComponent":"...#MinCountConstraintComponent",
#                 "focusNode":"http://localhost:5000/moves/2",
#                 "message":"There should be at least 1 value(s).",
#                 "resultPath":"https://schema.org/position","severity":"Violation"},
#                { ... the same for the missing https://schema.org/agent ... }]}

# The same violating request, but asking for JSON-LD: you get a REAL sh:ValidationReport graph
# instead of Problem Details. This is the dual-path 422.
curl -s -X POST http://localhost:5000/games/1/moves \
  -H 'Content-Type: application/ld+json' -H 'Accept: application/ld+json' \
  -d '[{"@id":"http://localhost:5000/moves/2","@type":["https://schema.org/MoveAction"]}]' | jq
# [{"@id":"_:c369260d-...","@type":["http://www.w3.org/ns/shacl#ValidationReport"],
#   "http://www.w3.org/ns/shacl#conforms":[{"@value":"false",
#     "@type":"http://www.w3.org/2001/XMLSchema#boolean"}],
#   "http://www.w3.org/ns/shacl#result":[ ... one node per violation ... ]}]

# A position that is a string, not an integer: sh:datatype fires.
curl -s -X POST http://localhost:5000/games/1/moves \
  -H 'Content-Type: application/ld+json' \
  -d '[{"@id":"http://localhost:5000/moves/3","@type":["https://schema.org/MoveAction"],
        "https://schema.org/position":[{"@value":"four"}],
        "https://schema.org/agent":[{"@id":"http://localhost:5000/people/alice",
          "@type":["https://schema.org/Person"],
          "https://schema.org/name":[{"@value":"Alice"}]}]}]' | jq '.violations[].constraintComponent'
# "http://www.w3.org/ns/shacl#DatatypeConstraintComponent"

# An agent carrying a predicate personShape does not declare: the CLOSED constraint fires.
curl -s -X POST http://localhost:5000/games/1/moves \
  -H 'Content-Type: application/ld+json' \
  -d '[{"@id":"http://localhost:5000/moves/4","@type":["https://schema.org/MoveAction"],
        "https://schema.org/position":[{"@value":4}],
        "https://schema.org/agent":[{"@id":"http://localhost:5000/people/bob",
          "@type":["https://schema.org/Person"],
          "https://schema.org/name":[{"@value":"Bob"}],
          "https://schema.org/telephone":[{"@value":"555-0100"}]}]}]' | jq '.violations[].constraintComponent'
# "http://www.w3.org/ns/shacl#ClosedConstraintComponent"    <- personShape: schema:telephone is not allowed
# "http://www.w3.org/ns/shacl#NodeConstraintComponent"      <- moveShape: the agent does not conform
# Both are reported, at two different focus nodes (the move, and the person) -- the recursive
# sh:node constraint and the closed shape it points at each raise their own result.

# Malformed JSON-LD is a 400, deliberately distinct from a 422 -- a parse failure is not a
# SHACL violation.
curl -i -s -X POST http://localhost:5000/games/1/moves \
  -H 'Content-Type: application/ld+json' -d '{not valid json' | head -1
# HTTP/1.1 400 Bad Request

# THE BYPASS, worth seeing for yourself: the same violating body sent as application/json is
# NOT validated. It reaches the handler, which reports triplesValidated: 0 because there is no
# pre-parsed graph. Only application/ld+json is intercepted -- see the package README.
curl -s -X POST http://localhost:5000/games/1/moves \
  -H 'Content-Type: application/json' \
  -d '[{"@id":"http://localhost:5000/moves/5","@type":["https://schema.org/MoveAction"]}]' | jq
# {"accepted":true,"gameId":"1","triplesValidated":0}

# A game id that does not exist is the handler's own 404 -- reached only because the body
# itself was valid, which is the point: validation and application logic are separate concerns.
curl -i -s -X POST http://localhost:5000/games/999/moves \
  -H 'Content-Type: application/ld+json' \
  -d '[{"@id":"http://localhost:5000/moves/6","@type":["https://schema.org/MoveAction"],
        "https://schema.org/position":[{"@value":4}],
        "https://schema.org/agent":[{"@id":"http://localhost:5000/people/alice",
          "@type":["https://schema.org/Person"],
          "https://schema.org/name":[{"@value":"Alice"}]}]}]' | head -1
# HTTP/1.1 404 Not Found
```

## What this sample still doesn't do

No persistence -- accepted moves are acknowledged, not stored; the `games` dict is in-memory and
the moves go nowhere, because the point here is the validation boundary, not a game engine. No
auth. No SPARQL-based constraint (`sh:sparql`) and no complex property paths -- both are covered by
`Frank.Validation`'s own tests, and adding them here would obscure the four categories this sample
does demonstrate end to end. No `GET` of the shapes graph itself: shapes are values held by the
application, and this package has no opinion on publishing them.
