# frank semantic clarify — Output Schema

Schema version: **1**

## Purpose

`frank semantic clarify` emits the unresolved and proposed entries from the semantic lock file as structured data for LLM consumption. The LLM produces a `resolved.json` file; `frank semantic accept` (B9) merges it back.

## Command

```
frank semantic clarify [--output-format json|markdown] [-o <file>]
```

Default format is `json`.

## JSON Schema (schemaVersion 1)

```json
{
  "schemaVersion": 1,
  "unresolved": [
    {
      "fsharpType": "string",
      "fields": [
        {
          "name": "string"
        }
      ]
    }
  ],
  "proposed": [
    {
      "fsharpType": "string",
      "currentCandidate": "string (IRI)",
      "confidence": "number (0.0–1.0)"
    }
  ]
}
```

### Top-level fields

| Field | Type | Description |
|-------|------|-------------|
| `schemaVersion` | integer | Always `1` for this release |
| `unresolved` | array | Types with `Status = Unresolved` in the lock file |
| `proposed` | array | Types with `Status = Proposed` in the lock file |

Confirmed types are excluded. If all mappings are confirmed, both arrays are empty and exit code is 0.

### Unresolved entry fields

| Field | Type | Description |
|-------|------|-------------|
| `fsharpType` | string | Fully-qualified F# type name |
| `fields` | array | Fields of the type |
| `fields[].name` | string | Field name |

### Proposed entry fields

| Field | Type | Description |
|-------|------|-------------|
| `fsharpType` | string | Fully-qualified F# type name |
| `currentCandidate` | string | IRI of the current proposed mapping |
| `confidence` | number | Confidence score (0.0–1.0) |

## Markdown output

When `--output-format markdown` is used, output is structured Markdown with:

- `## Unresolved Types` section with a subsection per type
- `## Proposed Types` section with current candidate and confidence per type
- Field tables for unresolved entries

## Version history

| Version | Changes |
|---------|---------|
| 1 | Initial release |
