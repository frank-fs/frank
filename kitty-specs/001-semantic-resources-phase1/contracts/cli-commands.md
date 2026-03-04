# CLI Command Contracts: frank-cli

**Date**: 2026-03-04

## Common Parameters

All commands accept:
- `--project <path>` — Path to `.fsproj` file (required)
- `--text` — Human-readable output mode (default: JSON)

## extract

**Parameters**:
- `--base-uri <uri>` — Override default namespace (default: derived from assembly name)
- `--vocabularies <list>` — Comma-separated vocabulary list (default: `schema.org,hydra`)
- `--scope project|file|resource` — Extraction scope (default: `project`)
- `--file <path>` — Source file path (required when `--scope file`)
- `--resource <route>` — Route template (required when `--scope resource`)

**JSON Output**:
```json
{
  "status": "success|error",
  "ontology": {
    "classes": [{ "uri": "...", "label": "...", "sourceType": "...", "sourceLocation": "..." }],
    "properties": [{ "uri": "...", "domain": "...", "range": "...", "sourceField": "..." }],
    "resources": [{ "uri": "...", "routeTemplate": "...", "capabilities": ["..."] }]
  },
  "shapes": {
    "count": 0,
    "nodeShapes": [{ "targetClass": "...", "properties": ["..."] }]
  },
  "unmapped": [{ "type": "...", "reason": "...", "sourceLocation": "..." }],
  "stateFile": "obj/frank-cli/extraction-state.json"
}
```

## clarify

**Parameters**: None beyond common.

**JSON Output**:
```json
{
  "status": "success|error",
  "questions": [
    {
      "id": "q1",
      "category": "type-mapping|relationship|cardinality|domain-meaning",
      "question": "Is ProductStatus a closed or open enumeration?",
      "context": { "sourceType": "ProductStatus", "sourceLocation": "Models.fs:15" },
      "options": [
        { "key": "a", "label": "Closed (fixed set of values)", "impact": "..." },
        { "key": "b", "label": "Open (extensible)", "impact": "..." }
      ]
    }
  ],
  "resolvedCount": 0,
  "totalCount": 0
}
```

## validate

**Parameters**: None beyond common.

**JSON Output**:
```json
{
  "status": "success|error",
  "completeness": {
    "mappedTypes": 0,
    "unmappedTypes": 0,
    "mappedRoutes": 0,
    "unmappedRoutes": 0,
    "coveragePercent": 0.0
  },
  "consistency": {
    "issues": [
      { "severity": "error|warning|info", "message": "...", "element": "...", "suggestion": "..." }
    ]
  },
  "vocabularyAlignment": {
    "alignedConcepts": 0,
    "unalignedConcepts": 0
  },
  "staleness": {
    "isStale": false,
    "sourceHash": "...",
    "extractionHash": "..."
  }
}
```

## diff

**Parameters**:
- `--previous <path>` — Path to previous extraction state (default: auto-detect from `obj/frank-cli/`)

**JSON Output**:
```json
{
  "status": "success|error",
  "changes": {
    "added": [{ "type": "class|property|resource|shape", "uri": "...", "label": "..." }],
    "removed": [{ "type": "...", "uri": "...", "label": "..." }],
    "modified": [{ "type": "...", "uri": "...", "field": "...", "from": "...", "to": "..." }]
  },
  "summary": { "added": 0, "removed": 0, "modified": 0 }
}
```

## compile

**Parameters**: None beyond common.

**JSON Output**:
```json
{
  "status": "success|error",
  "artifacts": [
    { "type": "ontology", "format": "owl-xml", "path": "obj/frank-cli/ontology.owl.xml", "size": 0 },
    { "type": "shapes", "format": "shacl-turtle", "path": "obj/frank-cli/shapes.shacl.ttl", "size": 0 },
    { "type": "manifest", "format": "json", "path": "obj/frank-cli/manifest.json", "size": 0 }
  ],
  "embeddedResources": [
    "Frank.Semantic.ontology.owl.xml",
    "Frank.Semantic.shapes.shacl.ttl",
    "Frank.Semantic.manifest.json"
  ],
  "buildInstruction": "Run 'dotnet build' to embed artifacts into the assembly."
}
```
