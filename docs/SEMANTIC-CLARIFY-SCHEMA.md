# Semantic Clarify Schema

`frank semantic clarify` reads the committed lock file (`.frank/semantic-mappings.lock.json`) and projects its `unresolved` and `proposed` mappings into a structured contract for LLM or human review. It performs no extraction, no network I/O, and no FCS evaluation — it is a pure projection of the lock.

## v1 Top-Level Shape

```json
{
  "schemaVersion": 1,
  "unresolved": [ ... ],
  "proposed": [ ... ]
}
```

`schemaVersion` is mandatory. Consumers must pin to the version they support and reject unknown versions. The current and only supported version is `1`.

`Confirmed` mappings are excluded from both arrays; they are already accepted and do not require LLM or human action.

## Unresolved Entry Shape

An entry appears in `unresolved` when the lock mapping has `status = "unresolved"`.

```json
{
  "fsharpType": "MyApp.Order",
  "candidates": ["schema:Foo", "schema:Bar"],
  "fields": [ { "name": "Id", "iri": null, "confidence": 0.0, "status": "unresolved" } ]
}
```

| Field | Type | Notes |
|-------|------|-------|
| `fsharpType` | string | Fully-qualified F# type name |
| `candidates` | string[] | The mapping's `Alternates` list from the lock; may be empty |
| `fields` | FieldNode[] | See field node shape below |

## Proposed Entry Shape

An entry appears in `proposed` when the lock mapping has `status = "proposed"`.

```json
{
  "fsharpType": "MyApp.Order",
  "currentCandidate": "schema:Order",
  "confidence": 0.65,
  "alternates": ["schema:OrderAction"],
  "fields": [ { "name": "Amount", "iri": "schema:price", "confidence": 0.72, "status": "proposed" } ]
}
```

| Field | Type | Notes |
|-------|------|-------|
| `fsharpType` | string | Fully-qualified F# type name |
| `currentCandidate` | string or null | The mapping's `Iri` from the lock; null when no IRI is set |
| `confidence` | number | Score in [0.0, 1.0] |
| `alternates` | string[] | The mapping's `Alternates` list from the lock |
| `fields` | FieldNode[] | See field node shape below |

## Field Node Shape

```json
{ "name": "Amount", "iri": "schema:price", "confidence": 0.72, "status": "proposed" }
```

| Field | Type | Notes |
|-------|------|-------|
| `name` | string | Field name as extracted from source |
| `iri` | string or null | Mapped IRI; null when no IRI has been assigned |
| `confidence` | number | Score in [0.0, 1.0] |
| `status` | string | `"confirmed"`, `"proposed"`, or `"unresolved"` |

IRI strings are emitted verbatim from the lock — CURIE form (e.g. `schema:Order`) and absolute IRI form (e.g. `https://schema.org/Order`) are both preserved as-is.

## Descoped in v1

The following data is not persisted in the lock file and therefore not available in the v1 clarify output. These are deferred to a future schema version:

- Field `type` — the F# type name of the field (e.g. `"string"`, `"int"`)
- Field `attributes` — custom attributes on the field
- Field `docComment` — XML doc comment on the field
- Candidate `description` — human-readable description from the vocabulary source
- Candidate `properties` — vocabulary-defined properties for the candidate class
- Candidate `nameScore` — the name-similarity component of the confidence score

Clarify is a pure projection of the lock. Richer data requires additions to the lock schema first.
