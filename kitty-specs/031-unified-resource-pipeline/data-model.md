# Data Model: Unified Resource Pipeline

## Core Types

### UnifiedResource

The central record produced by the unified extractor. One per HTTP resource in the project.

| Field | Type | Description |
|-------|------|-------------|
| RouteTemplate | string | HTTP route pattern (e.g., `/games/{gameId}`) |
| ResourceSlug | string | Filename-safe slug derived from route (e.g., `games`) |
| TypeInfo | AnalyzedType list | F# types associated with this resource (records, DUs) |
| Statechart | ExtractedStatechart option | Behavioral data (None for plain `resource` CEs) |
| HttpCapabilities | HttpCapability list | Methods available (globally or per-state) |
| DerivedFields | DerivedResourceFields | Computed invariant checks |

### DerivedResourceFields

Computed during extraction to enforce structure-behavior invariants.

| Field | Type | Description |
|-------|------|-------------|
| OrphanStates | string list | State DU cases not covered by any `inState` call |
| UnhandledCases | string list | DU cases in the state type but not in the statechart |
| StateStructure | Map<string, AnalyzedField list> | Per-state: which type fields are relevant |
| TypeCoverage | float | Ratio of mapped types to total types (0.0–1.0) |

### HttpCapability

| Field | Type | Description |
|-------|------|-------------|
| Method | string | HTTP method (GET, POST, PUT, DELETE, PATCH) |
| StateKey | string option | Which state this applies to (None = always available) |
| LinkRelation | string | IANA or ALPS-derived relation type URI |
| IsSafe | bool | true for GET/HEAD/OPTIONS |

### AffordanceMapEntry

One entry per (route, state) pair in the affordance map.

| Field | Type | Description |
|-------|------|-------------|
| RouteTemplate | string | HTTP route pattern |
| StateKey | string | State name, or `*` for stateless resources |
| AllowedMethods | string list | HTTP methods available in this state |
| LinkRelations | AffordanceLinkRelation list | Available transitions with relation types |
| ProfileUrl | string | URL to the ALPS profile for this resource |

### AffordanceLinkRelation

| Field | Type | Description |
|-------|------|-------------|
| Rel | string | Link relation type (IANA or ALPS URI) |
| Href | string | Target URL template |
| Method | string | HTTP method for this transition |
| Title | string option | Human-readable label |

### UnifiedExtractionState

The cached state persisted to binary.

| Field | Type | Description |
|-------|------|-------------|
| Resources | UnifiedResource list | All extracted resources |
| AffordanceMap | AffordanceMapEntry list | Pre-computed affordance entries |
| SourceHash | string | Hash of source files for staleness detection |
| BaseUri | string | Base URI for ALPS profile namespace |
| Vocabularies | string list | Schema.org vocabularies used for alignment |
| ExtractedAt | DateTimeOffset | Timestamp of extraction |
| ToolVersion | string | CLI version for cache compatibility |

## Relationships

```
UnifiedExtractionState
  └── UnifiedResource[]
        ├── AnalyzedType[]          (from existing TypeAnalyzer)
        ├── ExtractedStatechart?    (from existing StatechartSourceExtractor logic)
        ├── HttpCapability[]        (derived from statechart + route analysis)
        └── DerivedResourceFields   (computed cross-cutting invariants)

AffordanceMapEntry[]                (projected from UnifiedResource[])
  └── AffordanceLinkRelation[]      (IANA precedence, ALPS-derived)

toExtractionState projection:       (for backward compat)
  UnifiedExtractionState
    → ExtractionState               (OWL graphs, SHACL shapes, source map)
```

## State Lifecycle

```
Source files (.fs)
  → [frank-cli extract --project]
  → UnifiedExtractionState (binary cache in obj/)
  → [frank-cli generate --format ...]
  → Format artifacts (WSD, ALPS, SCXML, smcat, XState, affordance-map)

Source files (.fs)
  → [dotnet build + MSBuild target]
  → Embedded binary in assembly
  → [app startup: UseAffordances()]
  → In-memory: AffordanceMap + ALPS + OWL/SHACL projections
  → [request time]
  → Link + Allow headers, content-negotiated profiles
```

## Existing Types Reused (unchanged)

- `AnalyzedType` (from `Frank.Cli.Core.Analysis.TypeAnalyzer`)
- `AnalyzedField` (from `Frank.Cli.Core.Analysis.TypeAnalyzer`)
- `ExtractedStatechart` (from `Frank.Cli.Core.Statechart.StatechartExtractor`)
- `FormatTag` (from `Frank.Statecharts.Validation.Types`)
- `StatechartDocument` (from `Frank.Statecharts.Ast`)
- `ExtractionState` (from `Frank.Cli.Core.State.ExtractionState`) — target of projector
