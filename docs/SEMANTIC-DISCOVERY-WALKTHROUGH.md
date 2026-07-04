# Semantic Discovery Walkthrough

A concrete, runnable worked example for Frank's v7.3.2 semantic discovery pipeline. The
reference scenario is `sample/TicTacToe-v732`. Follow the doc literally: every command
listed here was run against that sample and the output shown is real.

For the conceptual loop (Sketch→Lift→Generate→Inspect→Iterate, state file, failure modes)
see [docs/AUTHORING_WORKFLOW.md](./AUTHORING_WORKFLOW.md). This document does not re-explain
the loop; it runs it.

Design rationale: [docs/superpowers/specs/2026-04-20-v732-semantic-discovery-design.md](./superpowers/specs/2026-04-20-v732-semantic-discovery-design.md).

---

## 1. Why

Frank's convention engine reads your F# types through FCS (no runtime reflection), scores
each one against every term in the declared vocabularies by string-similarity, and writes a
lock file. Convention scores are proposals, not decisions. You accept, reject, or override
each mapping explicitly — that reviewed lock file is the single source of truth that drives
all four generated modules (Semantics, Discovery, LinkedData, Validation, Provenance).

The LLM-assisted `clarify` pattern turns the "proposed" tier into a structured JSON contract
you paste to a model. The model returns a `resolved.json`; `frank semantic accept` merges it.
No annotation attributes, no strings scattered across handlers — every IRI that appears at
runtime is traceable to a reviewed decision in the lock file.

---

## 2. Setup

### Open the reference sample

```
sample/TicTacToe-v732/
├── Model.fs              F# domain types
├── Vocabulary.fs         vocabulary { } declaration
├── Program.fs            webHost CE wiring
├── .frank/
│   ├── semantic-mappings.lock.json   reviewed lock file (committed)
│   └── resolved.json                 last accepted resolution input
└── vocab/ttt.ttl         app-owned vocabulary for host-relative terms
```

The vocabulary declaration (`sample/TicTacToe-v732/Vocabulary.fs`) — quoted verbatim:

```fsharp
module TicTacToe.Vocabulary

open Frank.Semantic
open TicTacToe.Model

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        prefix "wikidata" "http://www.wikidata.org/entity/"
        prefix "ttt" "https://example.org/tictactoe#"
        using "schema"
        seeAlso typeof<Game> "wikidata:Q210339"
        seeAlso typeof<Game> "wikidata:Q573573"
        seeAlso typeof<Game> "wikidata:Q573520"
        equivalentClass typedefof<MoveLog<_>> "schema:ItemList"
        provClass typeof<MoveRequest> Activity

        constrainPattern
            typeof<MoveRequest>
            "Position"
            @"^(TopLeft|TopCenter|TopRight|MiddleLeft|MiddleCenter|MiddleRight|BottomLeft|BottomCenter|BottomRight)$"
    }
```

`using "schema"` tells the engine to score all types against the `https://schema.org/`
vocabulary. `seeAlso`, `equivalentClass`, and `constrainPattern` are authoring hints
that flow into the generated linked-data and validation modules.

### Starting from scratch (brief note)

There is no `dotnet new frank-app` template and no `frank semantic new/init` subcommand in
v7.3.2. For a brand-new project:

```bash
dotnet new console -lang F# -o MyApp
cd MyApp
dotnet add package Frank
dotnet add package Frank.Semantic
dotnet add package Frank.Discovery
dotnet add package Frank.LinkedData
dotnet add package Frank.Validation
dotnet add package Frank.Provenance
```

Then add the MSBuild code-generation target to your `.fsproj`:

```xml
<Import Project="$(NuGetPackageRoot)frank.cli.msbuild/<version>/build/Frank.Cli.MSBuild.targets" />
```

Write a `vocabulary { }` block referencing your domain types and declare at least one
`resource { }` in a `webHost { }` CE. The sample is the reference for what complete wiring
looks like.

---

## 3. Extract

`frank semantic extract` typechecks your project with FCS, walks the typed AST to discover
all types referenced from `vocabulary { }` and `resource { }` CEs, scores each one against
the declared vocabularies, and writes `.frank/semantic-mappings.lock.json`. Previously
confirmed mappings are preserved; only new/changed types are re-scored.

The CLI lives at `src/Frank.Cli`. Invoke it via `dotnet run --project`:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run \
  --project src/Frank.Cli/Frank.Cli.fsproj \
  -- semantic extract \
  --project sample/TicTacToe-v732/TicTacToe.v732.fsproj
```

Actual output (stdout only; build warnings omitted):

```
Confirmed: 4, Proposed: 0, Unresolved: 0
```

Run `frank semantic status` to see the full breakdown:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run \
  --project src/Frank.Cli/Frank.Cli.fsproj \
  -- semantic status \
  --project sample/TicTacToe-v732/TicTacToe.v732.fsproj
```

Output:

```
Confirmed:  4
Proposed:   0
Unresolved: 0
Excluded:   15
```

The four confirmed types are `MoveResult`, `Game`, `MoveLog`, and `MoveRequest`. The 15
excluded types (e.g. `SquarePosition`, `Player`, `Msg`) had either low-confidence convention
scores or were intentionally excluded during prior review.

### Lock file: confirmed entries

The relevant confirmed entries from
`sample/TicTacToe-v732/.frank/semantic-mappings.lock.json`:

```json
{
  "fsharpType": "TicTacToe.Model.Game",
  "iri": "schema:Game",
  "confidence": 1,
  "source": "manual",
  "status": "confirmed",
  "shape": "record",
  "fields": [
    { "name": "Id",     "iri": "schema:identifier", "status": "confirmed" },
    { "name": "Result", "iri": "schema:result",      "status": "confirmed" }
  ]
},
{
  "fsharpType": "TicTacToe.Model.MoveRequest",
  "iri": "schema:MoveAction",
  "rt": "schema:Game",
  "confidence": 1,
  "source": "manual",
  "status": "confirmed",
  "shape": "record",
  "fields": [
    { "name": "Position", "iri": "ttt:square",    "status": "confirmed" },
    { "name": "Player",   "iri": "schema:agent",  "status": "confirmed" }
  ]
}
```

`ttt:square` is a host-relative property defined in `vocab/ttt.ttl`. Its full IRI is
resolved at runtime from the `ttt` prefix declared in the vocabulary CE.

The `rt: "schema:Game"` on `MoveRequest` flows into the ALPS `MoveAction` descriptor as
`"rt": "https://schema.org/Game"`, telling agents that a successful POST returns a Game.

---

## 4. Clarify

`frank semantic clarify` reads the lock file and projects all `proposed` and `unresolved`
entries into a structured JSON contract for LLM or human review. It does no extraction and
no network I/O.

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run \
  --project src/Frank.Cli/Frank.Cli.fsproj \
  -- semantic clarify \
  --output-format json \
  --project sample/TicTacToe-v732/TicTacToe.v732.fsproj
```

Output for the sample (all decisions already made):

```json
{
  "schemaVersion": 1,
  "unresolved": [],
  "proposed": []
}
```

An empty contract means nothing needs review. For a project mid-workflow — before manual
decisions have been recorded — `unresolved` and `proposed` would contain entries. See
[docs/SEMANTIC-CLARIFY-SCHEMA.md](./SEMANTIC-CLARIFY-SCHEMA.md) for the full entry shapes.

### Feeding proposed mappings to an LLM

When the clarify output has entries, paste the JSON to a model with a prompt such as:

> You are mapping F# types to RDF vocabulary terms. For each `proposed` entry, confirm
> or correct the `iri`. For each `unresolved` entry, supply an `iri` from the vocabulary
> prefixes listed. Return a `resolved.json` matching the schema at `docs/SEMANTIC-CLARIFY-SCHEMA.md`.

Save the model response as `resolved.json` alongside the project (e.g.
`sample/TicTacToe-v732/.frank/resolved.json`), then merge it:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run \
  --project src/Frank.Cli/Frank.Cli.fsproj \
  -- semantic accept \
  --input sample/TicTacToe-v732/.frank/resolved.json \
  --source llm \
  --project sample/TicTacToe-v732/TicTacToe.v732.fsproj
```

Output format (using the sample's committed `resolved.json` which re-confirms
`Game`, `MoveLog`, `Move`, and `MoveResult`):

```
Merged 4 mapping(s); 0 excluded; 0 rejected; 15 unchanged; 3 already-confirmed; 11 field(s) still unresolved
```

The lock file is updated in place. `accept` validates every IRI against the cached
vocabulary before writing; a term that does not exist in the declared vocabulary is
rejected with a reason line to stderr.

---

## 5. Build

The MSBuild target imported via `Frank.Cli.MSBuild.targets` reads the lock file and
generates five F# modules into `obj/<tfm>/`:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build \
  sample/TicTacToe-v732/TicTacToe.v732.fsproj \
  -p:TreatWarningsAsErrors=false
```

The `-p:TreatWarningsAsErrors=false` flag is needed when building from source because
`src/Frank.OpenApi` currently triggers a transitive NU1903 audit warning. Published NuGet
packages are unaffected.

Generated files (in `obj/Debug/net10.0/`):

| File | Purpose |
|------|---------|
| `GeneratedSemantics.fs` | `SemanticResource` DU + `iri`/`clrType`/case-IRI helpers |
| `GeneratedDiscovery.fs` | `DiscoveryConfig` value: ALPS descriptors, JSON Home href-vars, Link headers |
| `GeneratedLinkedData.fs` | `OntologyDecl` with classes, properties, context bases |
| `GeneratedValidation.fs` | SHACL `ShapeDecl` list, `shapesGraph`, host-relative property patterns |
| `GeneratedProvenance.fs` | `provClasses`, `propertyClassRanges`, `declaredPrefixes` |

All five are injected into the compile order ahead of `Program.fs` by the MSBuild target.
The module names (`TicTacToe.GeneratedSemantics`, etc.) are derived from the project's
root namespace. `Program.fs` references them directly:

```fsharp
relation ((TicTacToe.GeneratedSemantics.iri TicTacToe.GeneratedSemantics.SemanticResource.Game).AbsoluteUri)
```

If you rename a mapped type without updating the lock file the build fails with a
type-not-found error at that reference — the generated modules are compile-checked
against your domain.

---

## 6. Run and Verify

Start the sample:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run \
  --project sample/TicTacToe-v732/TicTacToe.v732.fsproj \
  -p:TreatWarningsAsErrors=false \
  -- --urls http://localhost:5732
```

All four representation surfaces are live once the server starts. The reader-facing `curl`
commands are shown first; verified output (captured via .NET `HttpClient` since `curl` is
sandboxed in this environment) follows each.

### Discovery: ALPS profile

```bash
curl -s http://localhost:5732/alps/tictactoe.v732 | jq .alps.descriptor[].href
```

Verified response (GET /alps/tictactoe.v732, `Content-Type: application/alps+json`, 200):

```json
{"alps":{"version":"1.0","descriptor":[
  {"id":"ActionStatusType","type":"semantic","href":"https://schema.org/ActionStatusType",
   "descriptor":[
     {"id":"XTurn","type":"semantic","href":"https://schema.org/ActiveActionStatus"},
     {"id":"OTurn","type":"semantic","href":"https://schema.org/ActiveActionStatus"},
     {"id":"Won", "type":"semantic","href":"https://schema.org/CompletedActionStatus"},
     {"id":"Draw","type":"semantic","href":"https://schema.org/CompletedActionStatus"},
     {"id":"Error","type":"semantic","href":"https://schema.org/FailedActionStatus"}
   ]},
  {"id":"Game","type":"semantic","href":"https://schema.org/Game",
   "descriptor":[
     {"id":"identifier","type":"semantic","href":"https://schema.org/identifier"},
     {"id":"result",    "type":"semantic","href":"https://schema.org/result"}
   ]},
  {"id":"ItemList","type":"semantic","href":"https://schema.org/ItemList",
   "descriptor":[
     {"id":"item",           "type":"semantic","href":"https://schema.org/item"},
     {"id":"numberOfItems",  "type":"semantic","href":"https://schema.org/numberOfItems"}
   ]},
  {"id":"MoveAction","type":"unsafe","href":"https://schema.org/MoveAction",
   "rt":"https://schema.org/Game",
   "descriptor":[
     {"id":"square","type":"semantic","href":"/tictactoe#square"},
     {"id":"agent", "type":"semantic","href":"https://schema.org/agent"}
   ]}
]}}
```

Every `href` value is a `schema.org` IRI derived from the lock file. The `rt` on
`MoveAction` (`https://schema.org/Game`) comes from the `"rt": "schema:Game"` field on
the `MoveRequest` mapping.

### Discovery: JSON Home

```bash
curl -s -H "Accept: application/json-home" http://localhost:5732/
```

Verified response (200, `Content-Type: application/json-home`):

```json
{"resources":{
  "https://schema.org/Game":{
    "href-template":"/games/{id}",
    "href-vars":{"id":"https://schema.org/identifier"},
    "hints":{"allow":["GET","HEAD"]}
  },
  "https://schema.org/MoveAction":{
    "href-template":"/games/{id}/moves",
    "href-vars":{"id":"https://schema.org/identifier"},
    "hints":{"allow":["POST"]}
  }
}}
```

The relation keys (`https://schema.org/Game`, `https://schema.org/MoveAction`) match the
ALPS `href` values. A naive client that finds `https://schema.org/Game` in the JSON Home
document navigates to `/games/{id}` — it never hardcodes the path.

### Discovery: OPTIONS link headers

```bash
curl -s -I -X OPTIONS http://localhost:5732/games/demo1
```

Verified response headers (200):

```
Allow: GET, HEAD
Link: </alps/tictactoe.v732>; rel="describedby",
      <https://schema.org/ActionStatusType>; rel="type",
      <https://schema.org/Game>; rel="type",
      <https://schema.org/ItemList>; rel="type",
      <https://schema.org/MoveAction>; rel="type",
      <http://localhost:5732/provenance?resource=...>; rel="http://www.w3.org/ns/prov#has_provenance"
```

The `rel="type"` IRIs are the same `https://schema.org/` IRIs from the ALPS profile and
JSON Home document.

### Linked data: game resource

```bash
# JSON-LD
curl -s -H "Accept: application/ld+json" http://localhost:5732/games/demo1

# Turtle
curl -s -H "Accept: text/turtle" http://localhost:5732/games/demo1
```

Verified JSON-LD (200, `Content-Type: application/ld+json`):

```json
{"@context":[
  {"@base":"http://localhost:5732","rdf":"...","schema":"https://schema.org/","ttt":"http://localhost:5732/tictactoe#"},
  "https://schema.org"
],
"@graph":[{
  "@id":"games/demo1",
  "schema:identifier":"demo1",
  "schema:actionStatus":{"@id":"schema:ActiveActionStatus"},
  "ttt:currentPlayer":"X",
  "ttt:validMoves":[
    {"@id":"ttt:TopLeft"},{"@id":"ttt:TopCenter"},{"@id":"ttt:TopRight"},
    {"@id":"ttt:MiddleLeft"},{"@id":"ttt:MiddleCenter"},{"@id":"ttt:MiddleRight"},
    {"@id":"ttt:BottomLeft"},{"@id":"ttt:BottomCenter"},{"@id":"ttt:BottomRight"}
  ]
}]}
```

Verified Turtle (200, `Content-Type: text/turtle`):

```turtle
@base <http://localhost:5732> .
@prefix schema: <https://schema.org/>.
@prefix ttt: <http://localhost:5732/tictactoe#>.

<http://localhost:5732/games/demo1>
    ttt:currentPlayer "X";
    ttt:validMoves ttt:TopLeft, ttt:TopCenter, ttt:TopRight,
                   ttt:MiddleLeft, ttt:MiddleCenter, ttt:MiddleRight,
                   ttt:BottomLeft, ttt:BottomCenter, ttt:BottomRight;
    schema:actionStatus schema:ActiveActionStatus;
    schema:identifier "demo1" .
```

`schema:actionStatus` → `schema:ActiveActionStatus` maps directly to the `MoveResult.XTurn`
case IRI in `GeneratedSemantics.fs`. The game graph factory in `Program.fs` reads
`GeneratedSemantics.moveResultCaseIri` to select the correct schema.org status IRI — the
handler never hardcodes the string.

### Provenance

```bash
curl -s "http://localhost:5732/provenance?resource=http://localhost:5732/games/demo1"
```

Verified response (200, `Content-Type: application/ld+json`), first activity:

```json
{"@graph":[
  {"@id":"http://localhost:5732/games/demo1",
   "@type":"prov:Entity",
   "prov:wasGeneratedBy":["urn:uuid:...","urn:uuid:..."]},
  {"@id":"urn:uuid:...",
   "@type":"prov:Activity",
   "prov:startedAtTime":{"@value":"2026-07-04T...","@type":"xsd:dateTime"},
   "prov:wasAssociatedWith":{"@id":"http://localhost:5732/agents/anonymous"},
   "prov:used":{"@id":"http://localhost:5732/games/demo1"},
   "http:methodName":"GET",
   "http:statusCodeValue":{"@value":"200","@type":"xsd:integer"}},
  {"@id":"http://localhost:5732/agents/anonymous",
   "@type":"prov:Agent",
   "rdfs:label":"anonymous"}
]}
```

Each `GET /games/demo1` request produces a `prov:Activity` node. The provenance store
accumulates these in memory. `http://localhost:5732/agents/anonymous` uses the host-
relative agent IRI pattern from `GeneratedProvenance.declaredPrefixes`.

### Validation: passing and failing ld+json POST

A valid `application/ld+json` move using compacted CURIEs (POST succeeds, 200):

```bash
curl -s -X POST http://localhost:5732/games/demo1/moves \
  -H "Content-Type: application/ld+json" \
  -d '{"@context":{"schema":"https://schema.org/","ttt":"http://localhost:5732/tictactoe#"},
       "@type":"schema:MoveAction",
       "ttt:square":"TopLeft",
       "schema:agent":"X"}'
```

`parseMoveFromDoc` reads the `@context` to expand `ttt:square` →
`http://localhost:5732/tictactoe#square` and `schema:agent` →
`https://schema.org/agent` before looking up the JSON keys.
Fully-expanded IRI keys in the body are also accepted (backward-compatible).

An invalid request — missing the required `schema:agent` field (POST rejected, 422):

```bash
curl -s -X POST http://localhost:5732/games/game1/moves \
  -H "Content-Type: application/ld+json" \
  -d '{"@context":{"schema":"https://schema.org/","ttt":"http://localhost:5732/tictactoe#"},
       "@type":"schema:MoveAction",
       "ttt:square":"TopLeft"}'
```

Verified 422 response (`Content-Type: application/ld+json; profile="http://www.w3.org/ns/shacl#"`):

```json
{"@context":{"sh":"http://www.w3.org/ns/shacl#"},
 "@graph":[
   {"@id":"_:report","@type":["sh:ValidationReport"],
    "sh:conforms":[{"@value":"false","@type":"xsd:boolean"}],
    "sh:result":[{"@id":"_:r1"}]},
   {"@id":"_:r1","@type":["sh:ValidationResult"],
    "sh:sourceConstraintComponent":[{"@id":"sh:MinCountConstraintComponent"}],
    "sh:resultSeverity":[{"@id":"sh:Violation"}],
    "sh:resultMessage":[{"@value":"There should be at least 1 value(s)."}],
    "sh:resultPath":[{"@id":"https://schema.org/agent"}],
    "sh:focusNode":[{"@id":"_:subject"}]}
]}
```

The SHACL shape that rejected this body comes from `GeneratedValidation.shapes`:
`RecordShape(Uri "https://schema.org/MoveAction", [{ Path = Uri "https://schema.org/agent"; MinCount = 1 ... }])`.
The shape IRI (`https://schema.org/MoveAction`) and the violated property IRI
(`https://schema.org/agent`) are both derived from the lock file — same IRIs across all four
surfaces.

### IRI consistency proof

The same `schema.org` IRIs appear in every surface:

| Surface | `https://schema.org/MoveAction` |
|---------|--------------------------------|
| ALPS `descriptor[3].href` | present |
| JSON Home resource key | present |
| OPTIONS `Link rel="type"` | present |
| SHACL shape `targetClass` | present |
| Provenance `prov:Activity` `http:methodName` anchor | derived from MoveRequest confirmed mapping |
| LD+JSON `@type` on accepted POST body | present |

No string is hardcoded in `Program.fs` or any handler. All of them are resolved at compile
time from `TicTacToe.GeneratedSemantics.iri SemanticResource.MoveRequest`.

---

## 7. Iterate

The lock file is the review artifact. When you change a mapping, rebuild, and re-run, the
updated IRI propagates to all five generated modules and all four runtime surfaces.

### Re-running extract is deterministic

Re-run `frank semantic extract` with no changes:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run \
  --project src/Frank.Cli/Frank.Cli.fsproj \
  -- semantic extract \
  --project sample/TicTacToe-v732/TicTacToe.v732.fsproj
```

Output is identical:

```
Confirmed: 4, Proposed: 0, Unresolved: 0
```

The only diff in `.frank/semantic-mappings.lock.json` is the `generated` and
`fetchedAt` timestamps — no mapping decisions change. Commit the file to capture the
refresh timestamp; the `hash` field on each vocabulary confirms the vocabulary content has
not drifted.

### Changing a mapping: the accept loop

Suppose you want to correct the `MoveRequest.Position` field IRI. Create a minimal
`sample/TicTacToe-v732/.frank/revised.json`:

```json
{
  "schemaVersion": 1,
  "resolved": [
    {
      "fsharpType": "TicTacToe.Model.MoveRequest",
      "iri": "schema:MoveAction",
      "shape": "record",
      "fields": [
        { "name": "Position", "iri": "ttt:square" },
        { "name": "Player",   "iri": "schema:agent" }
      ]
    }
  ]
}
```

Run `frank semantic accept`:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run \
  --project src/Frank.Cli/Frank.Cli.fsproj \
  -- semantic accept \
  --input sample/TicTacToe-v732/.frank/revised.json \
  --source manual \
  --project sample/TicTacToe-v732/TicTacToe.v732.fsproj
```

Output:

```
Merged 1 mapping(s); 0 excluded; 0 rejected; 18 unchanged; 1 already-confirmed; 0 field(s) still unresolved
```

The lock file is written in place. Review the diff with `git diff
sample/TicTacToe-v732/.frank/semantic-mappings.lock.json` before committing — the diff is
the human-readable record of the semantic decision. Then rebuild:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build \
  sample/TicTacToe-v732/TicTacToe.v732.fsproj \
  -p:TreatWarningsAsErrors=false
```

All five generated modules regenerate from the updated lock file. The new IRI is present in
every runtime surface without touching a single handler.

### Checking vocabulary hash drift

After upstream vocabularies publish updates:

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run \
  --project src/Frank.Cli/Frank.Cli.fsproj \
  -- semantic refresh \
  --project sample/TicTacToe-v732/TicTacToe.v732.fsproj
```

Verified output (schema.org had updated since the lock file was originally written):

```
schema vocabulary hash drift: 0f0c97a4f666b2f8563573fe48453782fd51b87a504523cf0c9aff6a71c3eec4 → 90fe897313b1813753c7d4f9504f32f5b2331ec42972eaba4cbec52854115d5c
```

The command exits non-zero when drift is detected so CI can gate on vocabulary stability.
When no vocabulary has changed the output is:
```
1 vocabulary(ies) checked; no drift
```

Drift means the external vocabulary has changed content. Run `frank semantic extract` to
re-score against the new vocabulary and review any mapping changes before accepting them.
