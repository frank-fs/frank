# Data Model: Cross-Format Validation Pipeline and AST Merge

**Feature**: 030-cross-format-validation-pipeline
**Date**: 2026-03-18

## FormatTag (extended)

```
FormatTag (before)        FormatTag (after)
├── Wsd                   ├── Wsd
├── Alps                  ├── Alps
├── Scxml                 ├── Scxml
├── Smcat                 ├── Smcat
└── XState                ├── XState
                          └── AlpsXml    ← new
```

## Format Priority Ordering

| FormatTag | Priority | Structural Authority | Annotation Authority |
|-----------|----------|---------------------|---------------------|
| Scxml | 0 (highest) | Hierarchy, parallelism, history, executable content, data model | ScxmlAnnotation |
| Smcat | 1 | Topology, composite nesting, visual attributes | SmcatAnnotation |
| Wsd | 2 | Workflow ordering, guard extensions | WsdAnnotation |
| Alps | 3 | None (annotations only) | AlpsAnnotation |
| AlpsXml | 3 | None (annotations only) | AlpsAnnotation |
| XState | 4 | Unsupported | XStateAnnotation |

## Merge Algorithm (left fold)

```
Input: [(FormatTag * StatechartDocument)] sorted by priority

Base = document[0]  (highest priority)

For each enriching document[1..n]:
  For each state in enriching:
    Match by Identifier (exact string)?
    ├── Yes → Accumulate annotations. Fill None fields from enriching.
    └── No  → Add state to merged document.

  For each transition in enriching:
    Match by (Source, Target, Event) triple?
    ├── Yes → Accumulate annotations.
    └── No  → Add transition to merged document.

  Document annotations: accumulate from all.
  DataEntries: union by Name.
  Title: take first non-None.
  InitialStateId: take first non-None.
```

## Near-Match Detection

```
For each pair of formats (A, B):
  For each state S_A in A not exactly matched in B:
    For each state S_B in B:
      score = JaroWinkler(S_A.Identifier, S_B.Identifier)
      If score > threshold (default 0.8):
        → Warning: "'{S_A.Identifier}' in {A} is a near-match for '{S_B.Identifier}' in {B} (similarity: {score})"

  Same logic for event names across transitions.
```

## Pipeline Function Signatures

```
validateSources:
  (FormatTag * string) list → PipelineResult  (existing, unchanged)

mergeSources:
  (FormatTag * string) list → Result<StatechartDocument, PipelineError list>  (new)

validateAndMerge:  (NOT included — caller composes validateSources then mergeSources)
```
