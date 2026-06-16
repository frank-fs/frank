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
  "candidates": ["schema:OrderAction"],
  "fields": [ { "name": "Amount", "iri": "schema:price", "confidence": 0.72, "status": "proposed" } ]
}
```

| Field | Type | Notes |
|-------|------|-------|
| `fsharpType` | string | Fully-qualified F# type name |
| `currentCandidate` | string or null | The mapping's `Iri` from the lock; null when no IRI is set |
| `confidence` | number | Score in [0.0, 1.0] |
| `candidates` | string[] | The mapping's `Alternates` list from the lock — same key as unresolved entries |
| `fields` | FieldNode[] | See field node shape below |

Both `unresolved` and `proposed` entries use `candidates` for the alternate IRI list. An LLM consuming the `json` output consults the F# type definitions to choose among `candidates`; there is no `description` or `nameScore` in the clarify output — those are descoped to a future schema version.

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

## Resolved Input Schema (consumed by `accept`)

`frank semantic accept --input <file>` reads a resolved JSON document with this shape:

```json
{
  "schemaVersion": 1,
  "resolved": [
    {
      "fsharpType": "MyApp.Order",
      "iri": "schema:Order",
      "fields": [
        { "name": "Amount", "iri": "schema:price" }
      ]
    }
  ]
}
```

`schemaVersion` must be `1`. `accept` rejects documents with any other version.

| Field | Type | Notes |
|-------|------|-------|
| `fsharpType` | string | Must match a type present in the lock file; unmatched entries are reported and ignored |
| `iri` | string | Required non-null for the type to be confirmed; `accept` rejects a resolved entry whose `iri` is null or missing |
| `fields` | FieldNode[] | Optional; fields with a null `iri` are merged as still-unresolved |

A `fields[]` entry with `iri: null` is merged into the lock as still-unresolved — it is not confirmed. A `fields[]` entry with a non-null `iri` is confirmed.

## resolved-template Workflow

`frank semantic clarify --output-format resolved-template` emits a skeleton document that matches the `accept` input schema exactly. The LLM edits the skeleton in place rather than reshaping the `json` output.

```
frank semantic clarify \
  --lock-file .frank/semantic-mappings.lock.json \
  --output-format resolved-template > resolved.json
# edit resolved.json: fill null iri values, choosing from the candidates in the json output
frank semantic accept --input resolved.json
```

The template includes every non-confirmed mapping (unresolved + proposed) and excludes confirmed mappings. For `proposed` entries the `iri` is pre-filled with the current candidate so the LLM can confirm or override it. For `unresolved` entries the `iri` is `null` for the LLM to fill. Any entry left with `iri: null` will be rejected by `accept`.

To choose among IRIs for a type or field, run `frank semantic clarify --output-format json` and consult the `candidates` array for that entry alongside the F# type definitions.

## Descoped in v1

The following data is not persisted in the lock file and therefore not available in the v1 clarify output. These are deferred to a future schema version:

- Field `type` — the F# type name of the field (e.g. `"string"`, `"int"`)
- Field `attributes` — custom attributes on the field
- Field `docComment` — XML doc comment on the field
- Candidate `description` — human-readable description from the vocabulary source
- Candidate `properties` — vocabulary-defined properties for the candidate class
- Candidate `nameScore` — the name-similarity component of the confidence score

Clarify is a pure projection of the lock. Richer data requires additions to the lock schema first.
