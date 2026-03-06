---
work_package_id: WP02
title: Frank.LinkedData — Serialization & Disposal
lane: "doing"
dependencies: []
base_branch: master
base_commit: 1bd5337291a48a0f82ac3043f7f755a4b77c4437
created_at: '2026-03-06T18:55:05.060704+00:00'
subtasks:
- T006
- T007
- T008
- T009
- T010
phase: Phase 1 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "88436"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-06T15:25:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-020]
---

# Work Package Prompt: WP02 – Frank.LinkedData — Serialization & Disposal

## ⚠️ IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP02
```

No dependencies — this is an independent module package.

---

## Objectives & Success Criteria

- Multi-subject JSON-LD (`@graph`) output preserves `@type` and `@value` for typed literals (int, bool, double, decimal)
- `Int64` maps to `xsd:long` consistently across `TypeAnalyzer` and `InstanceProjector`
- `Decimal` maps to `xsd:decimal` (not `xsd:double`)
- `StreamReader` in `GraphLoader.fs` uses `use` binding (Constitution VI)
- `localName` and `namespaceUri` helpers defined exactly once in a shared module (Constitution VIII)

## Context & Constraints

- **Tracking Issue**: #81 — Tier 1 (correctness bugs, resource leaks) + Tier 3 (deduplication)
- **Constitution**: Principle VI (Resource Disposal), Principle VIII (No Duplicated Logic)
- **Research**: R2 (xsd:long decision), R5 (type mappings)
- **Key files**:
  - `src/Frank.LinkedData/Negotiation/JsonLdFormatter.fs` — multi-subject serialization + duplicated helpers
  - `src/Frank.LinkedData/Rdf/InstanceProjector.fs` — XSD mappings + duplicated `localName`
  - `src/Frank.LinkedData/Rdf/GraphLoader.fs` — StreamReader leak
  - `src/Frank.LinkedData/WebHostBuilderExtensions.fs` — inline `namespaceUri` extraction

## Subtasks & Detailed Guidance

### Subtask T006 – Fix JsonLdFormatter Multi-Subject Typed Literals

- **Purpose**: The `@graph` branch serializes all values as plain strings, losing type information. The single-subject path correctly handles typed literals — the `@graph` branch must match.
- **Steps**:
  1. Open `src/Frank.LinkedData/Negotiation/JsonLdFormatter.fs`
  2. Locate the `@graph` code path (multi-subject branch)
  3. Find where literal nodes are serialized — look for where values are written as plain strings without `@type`/`@value`
  4. Mirror the single-subject typed literal handling:
     - For `xsd:integer`/`xsd:long`: output `{ "@value": <int>, "@type": "xsd:long" }`
     - For `xsd:boolean`: output `{ "@value": <bool>, "@type": "xsd:boolean" }`
     - For `xsd:double`/`xsd:float`: output `{ "@value": <float>, "@type": "xsd:double" }`
     - For `xsd:decimal`: output `{ "@value": "<decimal>", "@type": "xsd:decimal" }`
     - For `xsd:string` or untyped: output plain string value
  5. Ensure both code paths produce identical output for the same input
- **Files**: `src/Frank.LinkedData/Negotiation/JsonLdFormatter.fs`
- **Parallel?**: No — T007 also touches XSD type handling
- **Notes**: Check how the single-subject path distinguishes typed from untyped literals and replicate that logic exactly.

### Subtask T007 – Fix Int64 and Decimal XSD Mappings in InstanceProjector

- **Purpose**: `TypeAnalyzer.fs` maps `Int64` → `xsd:long` but `InstanceProjector.fs` maps `Int64` → `xsd:integer`. Must be consistent. Also `Decimal` → `xsd:double` is lossy.
- **Steps**:
  1. Open `src/Frank.LinkedData/Rdf/InstanceProjector.fs`
  2. Find the type mapping logic (where .NET types map to XSD URIs)
  3. Change `Int64` mapping from `xsd:integer` to `xsd:long`
  4. Change `Decimal` mapping from `xsd:double` to `xsd:decimal`
  5. Verify `TypeAnalyzer.fs` already uses `xsd:long` for `Int64` (it should — just confirm)
  6. Search for any other XSD mapping locations to ensure global consistency
- **Files**:
  - `src/Frank.LinkedData/Rdf/InstanceProjector.fs` (primary change)
  - `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs` (verify consistency)
- **Parallel?**: No — related to T006 (both affect serialization output)

### Subtask T008 – Fix StreamReader Disposal in GraphLoader

- **Purpose**: `new StreamReader(...)` without `use` binding is a resource leak. Constitution Principle VI requires `use` for all `IDisposable`.
- **Steps**:
  1. Open `src/Frank.LinkedData/Rdf/GraphLoader.fs`
  2. Find `new StreamReader(...)` (or `StreamReader(...)`) calls
  3. Replace `let reader = new StreamReader(...)` with `use reader = new StreamReader(...)`
  4. Ensure the `use` scope covers the entire usage of the reader, including exception paths
  5. If the reader is returned from a function (ownership transfer), document this explicitly
- **Files**: `src/Frank.LinkedData/Rdf/GraphLoader.fs`
- **Parallel?**: No
- **Notes**: In F#, `use` ensures disposal even when exceptions occur — this is the correct pattern for `IDisposable`.

### Subtask T009 – Create Shared RdfUriHelpers Module

- **Purpose**: `localName` and `namespaceUri` are duplicated across `JsonLdFormatter.fs` (lines 12-22), `InstanceProjector.fs` (lines 31-38), and inline in `WebHostBuilderExtensions.fs`. Constitution VIII requires single definition.
- **Steps**:
  1. Create `src/Frank.LinkedData/Rdf/RdfUriHelpers.fs`
  2. Define the module:
     ```fsharp
     module Frank.LinkedData.Rdf.RdfUriHelpers

     /// Extract local name from URI (after last '/' or '#')
     let localName (uri: string) =
         // Use the existing logic from JsonLdFormatter or InstanceProjector
         // Pick the most correct/comprehensive implementation

     /// Extract namespace URI (up to and including last '/' or '#')
     let namespaceUri (uri: string) =
         // Use the existing logic from JsonLdFormatter
     ```
  3. Add `RdfUriHelpers.fs` to `Frank.LinkedData.fsproj` — place it BEFORE `JsonLdFormatter.fs`, `InstanceProjector.fs`, and `WebHostBuilderExtensions.fs` in compilation order
  4. Verify the module compiles
- **Files**:
  - `src/Frank.LinkedData/Rdf/RdfUriHelpers.fs` (new)
  - `src/Frank.LinkedData/Frank.LinkedData.fsproj` (add file reference)
- **Parallel?**: Yes — can start before T006-T008

### Subtask T010 – Update Consumers to Use RdfUriHelpers

- **Purpose**: Replace duplicated `localName`/`namespaceUri` definitions with references to the shared module.
- **Steps**:
  1. In `src/Frank.LinkedData/Negotiation/JsonLdFormatter.fs`:
     - Remove local `localName` function (lines 12-16)
     - Remove local `namespaceUri` function (lines 18-22)
     - Add `open Frank.LinkedData.Rdf.RdfUriHelpers` at the top
  2. In `src/Frank.LinkedData/Rdf/InstanceProjector.fs`:
     - Remove inline `localName` logic (lines 31-38)
     - Add `open Frank.LinkedData.Rdf.RdfUriHelpers`
  3. In `src/Frank.LinkedData/WebHostBuilderExtensions.fs`:
     - Remove inline namespace extraction logic
     - Add `open Frank.LinkedData.Rdf.RdfUriHelpers`
  4. Build to verify: `dotnet build src/Frank.LinkedData/`
  5. Search for any other occurrences: `grep -rn "localName\|namespaceUri" src/Frank.LinkedData/`
- **Files**:
  - `src/Frank.LinkedData/Negotiation/JsonLdFormatter.fs`
  - `src/Frank.LinkedData/Rdf/InstanceProjector.fs`
  - `src/Frank.LinkedData/WebHostBuilderExtensions.fs`
- **Parallel?**: Depends on T009
- **Notes**: Ensure function signatures match exactly to avoid breaking callers.

## Risks & Mitigations

- **JSON-LD format change**: Existing consumers may parse JSON-LD differently if typed literals now include `@type`/`@value` in `@graph` output. This is a correctness fix — the old behavior was wrong.
- **XSD mapping change**: Changing `xsd:integer` → `xsd:long` in InstanceProjector may affect existing RDF consumers. This is the correct mapping per planning decision.
- **Compilation order**: `RdfUriHelpers.fs` must appear before its consumers in the `.fsproj` file.

## Review Guidance

- Verify JSON-LD output for multi-subject graph includes typed literal annotations
- Verify `xsd:long` is used consistently for `Int64` across all mapping locations
- Verify `Decimal` maps to `xsd:decimal`
- Verify `StreamReader` has `use` binding
- Verify `localName`/`namespaceUri` appear exactly once in `RdfUriHelpers.fs`
- Verify no remaining duplicates: `grep -rn "let localName\|let namespaceUri" src/Frank.LinkedData/`

## Activity Log

- 2026-03-06T15:25:00Z – system – lane=planned – Prompt created.
- 2026-03-06T18:55:05Z – claude-opus – shell_pid=88436 – lane=doing – Assigned agent via workflow command
