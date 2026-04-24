# v7.3.2 Track B — Semantic Discovery Design

**Date:** 2026-04-20
**Status:** Design proposed, awaiting user review
**Version target:** v7.3.2
**Track:** B (Semantic Discovery)

---

## Context

Architectural pivot from split-concern model (statecharts and session types as separate composable concerns) to unified protocol representation. Work splits into three tracks:

- **Track A (Protocol):** Unified protocol representation, runtime, parsers, generators. Separate design. v7.4.0.
- **Track B (Semantic):** Vocabulary registry, semantic artifact generation, static discovery. **This track.** v7.3.2.
- **Track C (Integration):** Where Track A and Track B meet. Designed with Track A.

Track B ships before Track A. Track B proves the semantic discovery hypothesis: a naive client with no hardcoded knowledge can discover, understand, and navigate an API using standard HTTP mechanisms (JSON Home, ALPS, Link headers, OPTIONS, SHACL shapes, JSON-LD contexts).

Reference documents:
- `docs/brainstorms/V732_DECISIONS.md` — decisions log, open questions entering this design
- `docs/brainstorms/session_handoff_2026-04-20.md` — architectural pivot context
- `docs/STATECHARTS_ARCHITECTURE_DECISIONS.archive.md` — archived, contains salvageable AD-13/AD-14/AD-16

---

## Core design problem

How does a developer go from "I have F# record types and I want to use schema.org" to "correct dotNetRDF artifacts exist at runtime"?

Nobody writes RDF, SHACL, or JSON-LD by hand. Attribute-based semantic mappings go unused. The mechanism must be derived from what developers already maintain (F# types) plus lightweight configuration (vocabulary declarations).

The solution: convention engine + LLM assistance, with a committed lock file as the canonical record of resolved mappings, consumed by MSBuild-driven source generation that produces dotNetRDF artifacts at build time.

---

## Scope

### In scope for v7.3.2

Seven packages. All other packages removed from v7.3.x milestones and deferred to v7.4.0.

| Package | Status | Responsibility |
|---------|--------|---------------|
| `Frank` | Existing, unchanged | Core CE builders, resource metadata |
| `Frank.Semantic` | **New** | Vocabulary CE, registry, convention engine, lock file, vocab fetching/caching |
| `Frank.Validation` | Rewritten | SHACL validation middleware consuming generated shapes |
| `Frank.LinkedData` | Rewritten | JSON-LD content negotiation consuming generated graph |
| `Frank.Provenance` | Rewritten | Standalone request-level PROV-O consuming generated class mappings |
| `Frank.Discovery` | Rewritten | ALPS/Link/OPTIONS/JSON Home — generators moved from Statecharts |
| `Frank.Cli` / `Frank.Cli.Core` / `Frank.Cli.MSBuild` | Extended | Semantic subcommands, MSBuild source generation |

### Out of scope for v7.3.2

- `Frank.Statecharts`, `Frank.Statecharts.Core`, and all other Statecharts-adjacent packages — **removed from v7.3.x milestones**, deferred to v7.4.0 (Track A)
- `Frank.Resources.Model` — assess during Phase 2 audit. It is zero-dep and holds `ResourceSpec`/affordance types that Frank.Discovery will consume at runtime. Keep if it is clean of Statecharts entanglement; otherwise fold the minimum needed types into `Frank` core.
- Myriad codegen plugins — not needed, CLI-generated F# source via MSBuild is sufficient
- Statechart-augmented provenance / journal overlay — Track A/C
- Actor + SQLite persistence
- Role/state-dependent discovery projections — Track A
- Vocabulary CE operations for type/field IRI mappings — not expected authoring path; can be added later if hypothesis proves wrong

### Audit phase mandatory

Existing Core packages have tests but were never proven to work in actual HTTP integration. v7.3.0 lesson: tests passed, integration failed. Spec includes audit phase verifying what actually works before designing integration. Willingness to throw out and rewrite. Salvage is nice-to-have, not assumed.

---

## Design decisions reached during brainstorm

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Interactive propose-and-confirm with LLM enhancement | Auto-mapping is too magical; manual-only misses LLM leverage |
| 2 | No type/field IRI operations in vocabulary CE | Hypothesis: devs won't write or maintain them; LLMs will generate them |
| 3 | Lock-file model for mappings | Deterministic builds, reviewable diffs, regenerable; parallel to paket.lock |
| 4 | Convention engine does name matching only; LLM handles structural reasoning | Keeps engine simple; avoids reimplementing LLM judgment in code |
| 5 | CLI emits JSON (for LLM) and Markdown (for humans) | Existing `--output-format` pattern; consumable by Claude Code as MCP-friendly tool |
| 6 | MSBuild target generates `.fs` files at build time | Superset of committed generated source; existing `Frank.Cli.MSBuild` pattern |
| 7 | Generated code minimal — correctness over inspectability | Developer shouldn't need to inspect generated code |
| 8 | Vocabulary schemas fetched on demand, cached locally | Keeps package lean; vocabularies stay current |
| 9 | One `Frank.Semantic` package (not Core/impl split) | Runtime packages consume generated code, not registry directly |
| 10 | Runtime packages replace old paths — no fallback | No projects depend on v7.3.0/7.3.1 broken implementations |
| 11 | Frank.Discovery takes over from Frank.Statecharts | Discovery generators/serializers move; parsers stay in Statecharts for Track A |
| 12 | Remove Statecharts from v7.3.x milestones | Track A work — belongs in v7.4.0 |

---

## Architecture

### Package dependency graph

```
Frank.Semantic         (new, depends on dotNetRDF)
    │
    └─ Frank.Cli.Core  (depends on FCS + Frank.Semantic)
            │
            └─ Frank.Cli
                    │
                    └─ Frank.Cli.MSBuild

Runtime packages — depend on dotNetRDF + Frank, NOT on Frank.Semantic.
Generated .fs files are emitted into the consuming project's obj/
and compiled into that project's assembly.

    Frank (core) ──┬── Frank.Validation
                   ├── Frank.LinkedData
                   ├── Frank.Provenance
                   └── Frank.Discovery
                         │
                         └─ consumes ResourceSpec from Frank core
```

Generated code is emitted *into the application project*, not into the runtime packages. Runtime packages provide middleware/services that consume the generated modules by convention (e.g., `GeneratedValidation.shapesGraph` is expected to exist in the caller's assembly and is passed to `useValidationWith`).

### End-to-end flow

```
Developer writes:
  1. F# record types
  2. vocabulary { } CE declaring prefixes and `using` vocabularies
  3. .fsproj references Frank.Cli.MSBuild

Developer runs `frank semantic extract`:
  1. FCS extracts F# types (name, fields, attributes, doc comments)
  2. Frank.Semantic fetches vocabulary schemas (cached in .frank/vocabularies/)
  3. Convention engine runs name matching
  4. Lock file written with confirmed (≥0.85) / proposed (<0.85) / unresolved entries

Developer runs `frank semantic clarify --output-format json`:
  1. Emits structured data for LLM consumption

LLM (e.g. Claude Code) reads JSON, produces resolved.json:
  1. Reasons about mappings using type/attribute/doc comment context

Developer runs `frank semantic accept --input resolved.json`:
  1. Merges LLM resolutions into lock file
  2. All entries now confirmed

dotnet build:
  1. MSBuild reads lock file via Frank.Cli.MSBuild target
  2. Build error if any entries proposed or unresolved
  3. CLI generates .fs files into obj/
  4. Runtime packages consume generated modules
```

---

## Section 1: Frank.Semantic

Single package. Depends on dotNetRDF.

### Vocabulary CE

```fsharp
let registry = vocabulary {
    prefix "schema"   "https://schema.org/"
    prefix "wikidata" "https://www.wikidata.org/wiki/"
    prefix "prov"     "http://www.w3.org/ns/prov#"
    prefix "ex"       "http://example.com/vocab#"

    using "ex"
    using "schema"

    equivalentClass typeof<Order>             "schema:Order"
    seeAlso         typeof<TicTacToeGame>     "wikidata:Q11907"
    fieldSeeAlso    typeof<Order> "LineItems" "schema:orderedItem"

    provClass typeof<OrderPlaced> ProvO.Activity

    constrainPattern typeof<Address> "ZipCode" "^\d{5}$"

    include SharedLib.Vocabulary.registry
}
```

**Operations:**
- `prefix name uri` — registers a prefix
- `using prefix` — declares which prefixes are in scope for resolution
- `equivalentClass type iri` — owl:equivalentClass outbound alignment
- `seeAlso type iri` — rdfs:seeAlso outbound alignment
- `fieldSeeAlso type fieldName iri` — field-level rdfs:seeAlso
- `provClass type provOClass` — PROV-O domain typing
- `constrainPattern type fieldName regex` — constraint the F# type system can't express
- `include registry` — compose registries from shared libraries

**Operations explicitly excluded from v7.3.2:**
- `typeIri` and `fieldIri` — mappings live in lock file, not CE

### VocabularyRegistry type

```fsharp
type VocabularyRegistry =
    { Prefixes: Map<string, Uri>
      Using: Set<string>
      EquivalentClasses: Map<Type, Uri>
      SeeAlso: Map<Type, Uri list>
      FieldSeeAlso: Map<Type * string, Uri list>
      ProvClasses: Map<Type, ProvOClass>
      ConstraintPatterns: Map<Type * string, string> }
```

### Convention engine

Input: `TypeInfo` record (populated by `Frank.Cli.Core` from FCS).

```fsharp
type FieldInfo =
    { Name: string
      TypeName: string
      Attributes: Map<string, string>   // JsonPropertyName, Description, etc.
      DocComment: string option }

type TypeInfo =
    { FullName: string
      Namespace: string
      LocalName: string
      Fields: FieldInfo list
      Attributes: Map<string, string>
      DocComment: string option }
```

Algorithm:
- Normalize names: PascalCase → lowercase tokens, strip `Dto`/`Model` suffixes
- Jaro-Winkler similarity between F# type local name and vocabulary class local names
- For candidate vocabulary classes: score field name overlap against vocabulary property set
- `JsonPropertyName` attribute values used as alternate name sources
- Weighted score combines type name similarity + field overlap ratio
- Threshold: ≥0.85 = `confirmed`, <0.85 with best candidate = `proposed`, no viable candidate = `unresolved`

### Vocabulary fetching and caching

- Cache location: `.frank/vocabularies/`
- Formats supported: JSON-LD context, OWL/XML, Turtle; auto-detected via content-type/extension
- Parsed into dotNetRDF `IGraph` for querying class/property definitions
- SHA-256 hash per vocabulary recorded in lock file
- `frank semantic refresh` re-fetches, compares hashes, flags drift

### Lock file I/O

Read/write `.frank/semantic-mappings.lock.json`. Schema in Section 3.

---

## Section 2: Frank.Cli extensions

### Package structure

`Frank.Cli.Core` depends on FCS and `Frank.Semantic`. FCS type extraction feeds convention engine.

`Frank.Cli` adds new subcommands to existing `semantic` group.

### Commands

| Command | Purpose |
|---------|---------|
| `frank semantic extract` | FCS type extraction → convention engine → writes lock file |
| `frank semantic clarify` | Emits unresolved/proposed entries (JSON or Markdown) |
| `frank semantic accept` | Merges resolved mappings into lock file |
| `frank semantic refresh` | Re-fetches vocabulary schemas, flags drift |
| `frank semantic status` | Lock file summary: confirmed/proposed/unresolved counts |

`extract` and `clarify` exist today but need reshaping. `accept`, `refresh`, `status` are new.

### CLI JSON output for LLM consumption

`frank semantic clarify --output-format json` emits:

```json
{
  "unresolved": [
    {
      "fsharpType": "MyApp.OrderLine",
      "fields": [
        {
          "name": "Quantity",
          "type": "int",
          "docComment": "Number of items ordered"
        },
        {
          "name": "UnitPrice",
          "type": "decimal",
          "attributes": {"JsonPropertyName": "unit_price"}
        }
      ],
      "candidates": [
        {
          "term": "schema:OrderItem",
          "description": "An order item is a line of an order...",
          "properties": ["orderQuantity", "price", "orderedItem"],
          "nameScore": 0.62
        }
      ]
    }
  ],
  "proposed": [
    {
      "fsharpType": "MyApp.Order",
      "field": "LineItems",
      "currentCandidate": "schema:orderedItem",
      "confidence": 0.65,
      "alternates": ["schema:itemListElement", "schema:orderItemNumber"]
    }
  ]
}
```

LLM writes a resolved JSON file in the lock file entry shape. `frank semantic accept --input resolved.json` merges it in.

The JSON output is a first-class interface, not a convenience format. It is the protocol between the authoring LLM and the developer's CLI session, and it is the contract the LLM's prompt discipline is written against. Schema discipline applies: the top-level shape (`unresolved`, `proposed`, and the nested `candidates` / `fields` / `alternates` fields) is versioned; changes are additive-only within a major version; breaking changes require a major-version bump and a migration note for any LLM prompt templates that consume it. The schema version is carried as a top-level `"schemaVersion"` field in every emission. A schema version the LLM's prompt template does not recognize produces a clean failure with remediation guidance rather than a silent misinterpretation.

The same discipline applies to `resolved.json` on the inbound side: `accept` validates the schema version and the structural shape before merging, failing closed on mismatches.

---

## Section 3: Lock file format

**Path:** `.frank/semantic-mappings.lock.json` (committed)

```json
{
  "version": 1,
  "generated": "2026-04-20T12:00:00Z",
  "vocabularies": {
    "schema": {
      "uri": "https://schema.org/",
      "fetchedAt": "2026-04-20T11:58:00Z",
      "hash": "sha256:abc123..."
    }
  },
  "mappings": [
    {
      "fsharpType": "MyApp.Order",
      "iri": "schema:Order",
      "confidence": 0.92,
      "source": "convention",
      "status": "confirmed",
      "fields": [
        {
          "name": "Total",
          "iri": "schema:totalPaymentDue",
          "confidence": 0.78,
          "source": "llm",
          "status": "confirmed"
        },
        {
          "name": "LineItems",
          "iri": "schema:orderedItem",
          "confidence": 0.65,
          "source": "convention",
          "status": "proposed"
        }
      ]
    }
  ]
}
```

**Field semantics:**
- `source`: `"convention"` | `"llm"` | `"manual"`
- `status`: `"confirmed"` | `"proposed"` | `"unresolved"`
- `proposed` and `unresolved` entries block MSBuild source generation

**Regeneration:** Not hand-edited. `frank semantic extract` rewrites deterministically. `frank semantic accept` merges LLM/manual resolutions.

---

## Section 4: MSBuild source generation

### Build flow

Extends existing `Frank.Cli.MSBuild` targets.

1. MSBuild target reads `.frank/semantic-mappings.lock.json`
2. If any entry has status `proposed` or `unresolved` → build error with remediation guidance
3. All entries `confirmed` → invoke CLI to generate `.fs` files into `obj/`
4. Generated files added to `@(Compile)` item group automatically

### Generated files — one per concern

| File | Contains |
|------|----------|
| `GeneratedLinkedData.fs` | JSON-LD `@context` (including external context refs), `IGraph` with type/property triples, `owl:equivalentClass` and `rdfs:seeAlso` triples for outbound links |
| `GeneratedValidation.fs` | `ShapesGraph` construction with SHACL shapes derived from F# types + mapped IRIs |
| `GeneratedProvenance.fs` | PROV-O class mappings for `provClass` entries |
| `GeneratedDiscovery.fs` | Link header values with `rel="describedby"`, ALPS semantic descriptor entries |

### Conditional generation

Only generate files for concerns the project uses. Detect via package references:
- References `Frank.Validation` → generate `GeneratedValidation.fs`
- References `Frank.LinkedData` → generate `GeneratedLinkedData.fs`
- etc.

Keeps generated code minimal per project.

---

## Section 5: Runtime packages — rewrites

No fallbacks, no migration paths. v7.3.0/v7.3.1 implementations were never successfully integrated; no consumers to protect.

### Frank.LinkedData (rewritten)

**Removed:**
- `GraphLoader` embedded-resource loading
- `JsonLdFormatter` dynamic context extraction from predicate URIs
- Manual JSON-LD construction where the generated graph suffices

**Consumes:** `GeneratedLinkedData` module
- Pre-built `IGraph` with all type/property triples
- External `@context` references (JSON-LD responses reference `https://schema.org` etc. directly)
- Outbound link triples (`owl:equivalentClass`, `rdfs:seeAlso`)

**Exposes:** Content negotiation middleware serving JSON-LD, Turtle, RDF/XML. Serialization uses dotNetRDF's writers where available; JSON-LD uses the generated `@context` for correct compaction.

### Frank.Validation (rewritten)

**Removed:**
- Reflection-based `ShapeBuilder`
- `ShapeGraphBuilder` runtime triple construction
- `UriConventions` (`urn:frank:shape:*`, `urn:frank:property:*`)

**Consumes:** `GeneratedValidation` module
- Pre-built `ShapesGraph` with SHACL shapes using vocabulary-mapped IRIs
- Shape URIs resolve via registry, not `urn:frank:` conventions

**Exposes:** `ValidationMiddleware` validating request bodies against pre-built shapes. `Validator.validate` delegates to `VDS.RDF.Shacl.ShapesGraph.Validate` as today.

### Frank.Provenance (rewritten, standalone mode only)

**Removed:**
- Hardcoded `ProvVocabulary` module (as the sole source of class IRIs)
- Any remaining `Frank.Statecharts` dependency

**Consumes:** `GeneratedProvenance` module
- Base PROV-O vocabulary plus project-specific `provClass` mappings
- Domain types resolve to IRIs via registry

**Exposes:** Request-level PROV-O: HTTP request → Activity, response → Entity, auth principal → Agent. In-memory `MailboxProcessorStore` retained. Statechart-augmented mode deferred to Track A/C.

### Frank.Discovery (rewritten)

**Moved in (from Frank.Statecharts):**
- ALPS JSON/XML **generators** and **serializers**
- Link header middleware
- OPTIONS/Allow middleware
- JSON Home serving

**Explicitly NOT moved:**
- ALPS JSON/XML **parsers** — stay in `Frank.Statecharts` for Track A's protocol parsing work

**Consumes at runtime:**
- Endpoint routing metadata (`ResourceSpec` from `resource` CE) — routes, allowed methods
- `GeneratedDiscovery` module — vocabulary-aligned semantic descriptors, external Link header values

**Produces:**
- OPTIONS responses with `Allow` header from endpoint metadata
- Link headers with `rel="describedby"` pointing to ALPS profile and external vocabulary URIs
- ALPS profiles: resource-and-property descriptors with vocabulary-aligned IRIs, **without state/transition nesting** (state-projection descriptors are Track A). Each resource emits `descriptor` entries for its fields plus `safe`/`unsafe`/`idempotent` action descriptors for its HTTP methods.
- JSON Home: resource directory with relation types mapped to vocabulary terms

**Removed dependencies:** `Frank.Statecharts`, `Frank.Statecharts.Core`

Track A later enriches with role/state-dependent projections via `Frank.Protocol.Discovery`.

---

## Section 6: Acceptance criteria

Falsifiable HTTP request/response pairs per V732 decisions. Not unit tests.

### Per-package HTTP tests

1. **Frank.Validation** — POST with JSON body matching mapped type → 200. POST with invalid body → 422 with W3C `ValidationReport` referencing vocabulary IRIs (e.g., `schema:totalPaymentDue`), not `urn:frank:property:Total`.

2. **Frank.LinkedData** — GET with `Accept: application/ld+json` → response body contains `@context` referencing external vocabularies (`"@context": ["https://schema.org", ...]`). Response contains `owl:equivalentClass` and `rdfs:seeAlso` triples for declared outbound links.

3. **Frank.Provenance** — GET/POST → PROV-O response with domain-type IRIs from `provClass` mappings. An `OrderPlaced` domain event serializes as `prov:Activity` with type `schema:OrderAction` (or whatever was mapped), not a hardcoded IRI.

4. **Frank.Discovery** —
   - OPTIONS → correct `Allow` header plus Link header `rel="describedby"` pointing to ALPS profile
   - GET ALPS profile → semantic descriptors reference vocabulary IRIs (`schema:Order`, not `urn:frank:`)
   - GET JSON Home document → resource directory with `rel` values mapped to vocabulary terms

### Composition test

5. All four packages composed on a single resource → consistent IRIs across JSON-LD `@context`, SHACL shape property paths, PROV-O type annotations, and ALPS semantic descriptors. The same F# field resolves to the same IRI everywhere.

### Capstone test

6. Tic-tac-toe with `vocabulary { using "schema" }` → naive client:
   - Discovers API via JSON Home
   - Reads ALPS profile with schema.org-aligned descriptors
   - Validates moves via generated SHACL shapes (errors cite schema.org property IRIs)
   - Receives JSON-LD responses referencing external `@context`
   - Navigates entirely via discovery with no hardcoded API knowledge

### Negative tests

- Switch vocabulary CE from `using "schema"` to `using "ex"` with a locally-defined `ex:` vocabulary → lock file regenerates, generated artifacts emit `ex:` IRIs → naive client that hardcoded `schema.org` IRIs breaks; same client navigating purely via discovery still works. Confirms the client actually relied on discovery rather than out-of-band knowledge.
- Lock file with any `status: "proposed"` or `status: "unresolved"` entry → `dotnet build` fails with guidance to run `frank semantic clarify`. Confirms build gate works.
- Lock file `vocabularies[].hash` drift vs fetched vocabulary → `frank semantic refresh` reports the change; existing `confirmed` entries are not auto-mutated. Confirms hash staleness detection without silent mapping updates.

---

## Section 7: Work sequencing

Outside-in per V732 decisions. CLI and developer experience first, then libraries wired in behind it.

### Phase 1 — Milestone reshaping (before any code)

- Close or defer all open v7.4.0 issues related to Track A (35 issues)
- Remove `Frank.Statecharts` and adjacent packages from v7.3.x milestones
- Create v7.3.2 milestone
- File Track B issues from this spec: one per section roughly, decomposed by `/decompose`

### Phase 2 — Audit

Before any library rewrites, verify what actually works today end-to-end:
- Frank.Validation — does SHACL validation actually run on HTTP requests?
- Frank.LinkedData — does content negotiation actually serve JSON-LD?
- Frank.Provenance — does PROV-O actually serialize?
- Current Frank.Discovery pieces in Frank.Statecharts — which generators work?

Output: list of working code to salvage vs code to rewrite.

### Phase 3 — Frank.Semantic (new package)

- Vocabulary CE, registry types
- Vocabulary fetching/caching
- Convention engine (name matching)
- Lock file I/O
- Type-info input types (for CLI to populate)

### Phase 4 — Frank.Cli extensions

- FCS → TypeInfo extraction (may exist already)
- `frank semantic extract` wiring end-to-end
- `frank semantic clarify` JSON/Markdown output
- `frank semantic accept`, `refresh`, `status` commands

### Phase 5 — MSBuild source generation

- Lock file reading in MSBuild target
- Build error on non-confirmed entries
- CLI source generator for `GeneratedLinkedData.fs`, `GeneratedValidation.fs`, `GeneratedProvenance.fs`, `GeneratedDiscovery.fs`
- Conditional generation based on package references

### Phase 6 — Runtime package rewrites (parallel where possible)

- Frank.Discovery — move generators/serializers from Statecharts, strip statechart deps, wire in generated data
- Frank.Validation — replace reflection path with generated ShapesGraph
- Frank.LinkedData — replace dynamic context with generated graph + @context
- Frank.Provenance — standalone mode with generated class mappings

### Phase 7 — HTTP test suite

- Per-package HTTP tests
- Composition test
- Capstone tic-tac-toe test with naive client

### Phase 8 — Negative tests and docs

- Falsifiability tests (remove `using`, lock file proposed)
- Blog post / docs for the pit-of-success workflow

---

## Section 8: Open questions resolved in this design

From V732_DECISIONS.md:

| Question | Resolution |
|----------|-----------|
| What does the convention engine do? | Name matching (Jaro-Winkler) against vocabulary class/property local names, with `JsonPropertyName` as alternate source. Weighted score: type name similarity + field overlap ratio. Structural reasoning handed off to LLM via CLI JSON output. |
| What structured output does the CLI produce? | Per Section 2: JSON with unresolved/proposed entries including type fields, attributes, doc comments, candidate vocabulary terms with scores. |
| Canonical committed form? | Lock file (`.frank/semantic-mappings.lock.json`). Vocabulary CE is prefixes/alignments only, not mapping source. |
| How does mapping flow from developer → runtime artifacts? | vocabulary CE + F# types → `frank semantic extract` → lock file → `frank semantic clarify`+LLM+`accept` if needed → MSBuild generates `.fs` → compiled into assembly → runtime packages consume. |
| Is Myriad needed? | No. CLI-generated `.fs` into `obj/` via MSBuild target is sufficient. Vocabulary mappings are stable; no build-time codegen magic required. |
| What does codegen generate? | `.fs` source files in `obj/`, one per concern: `GeneratedLinkedData`, `GeneratedValidation`, `GeneratedProvenance`, `GeneratedDiscovery`. Contains minimal code to construct dotNetRDF artifacts. |
| What can be salvaged? | Determined in Phase 2 audit. Assume rewrite by default; salvage explicitly. |

---

## Section 9: Non-goals

Explicitly not addressed in v7.3.2:

- Runtime reflection-based mappings (replaced by build-time codegen)
- Attribute-based semantic annotations on F# types (not used — LLM-driven registry + convention engine is the replacement)
- Hand-authored Turtle/SHACL/JSON-LD as canonical source (rejected at the thesis level per AD-16)
- Role/state projections in Discovery output (Track A)
- Statechart-augmented PROV-O (Track A/C)
- Vocabulary version pinning beyond hash-based drift detection

---

## Relationship to other documents

- `docs/brainstorms/V732_DECISIONS.md` — decisions log; this spec resolves the open questions
- `docs/brainstorms/session_handoff_2026-04-20.md` — architectural pivot context, Track A/B/C rationale
- `docs/STATECHARTS_ARCHITECTURE_DECISIONS.archive.md` — AD-13, AD-14, AD-16 contain the load-bearing design this spec operationalizes for Track B
- Track A + Track C designs — separate, v7.4.0
