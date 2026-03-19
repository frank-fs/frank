# Research: Unified Resource Pipeline

## R1: Binary Serialization for F# Records and DUs

**Decision**: MessagePack-CSharp with contractless resolver (primary), with evaluation of MemoryPack source generators during implementation.

**Rationale**:
- MemoryPack uses C# source generators which do not run in F# projects. F# records compile to sealed classes with init-only properties — MemoryPack *may* handle these via its `GenerateType` attribute on C# wrappers, but this adds a C# interop project and complexity.
- MessagePack-CSharp has a `ContractlessStandardResolver` that serializes any public type without attributes. It handles F# records (which are public sealed classes with public properties) natively. It also handles F# discriminated unions via the `DynamicUnionResolver`.
- The existing codebase uses System.Text.Json extensively (Utf8JsonWriter in `StatechartDocumentJson.fs`, `ExtractionState.fs`, all output formatters). No existing binary serialization dependency.
- MessagePack is ~5-10x faster than System.Text.Json. MemoryPack is ~2-5x faster than MessagePack. For a one-time startup deserialization, MessagePack's speed is sufficient.
- MessagePack format is cross-platform (BEAM port can read it with Erlang's `msgpax` library), which MemoryPack is not.

**Alternatives considered**:
- MemoryPack: Fastest, but C# source generator requirement is a blocker for pure F# projects without a C# interop shim
- FsPickler: F#-native binary serializer, but unmaintained since 2021 and has known security issues with BinaryFormatter under the hood
- System.Text.Json (binary mode): No binary mode exists; JSON is text-only
- Protocol Buffers: Requires .proto schema files, heavy tooling, not idiomatic for F# records
- CBOR (System.Formats.Cbor): Available in .NET but no high-level serializer for F# types — would require manual encoding

**Action**: Add `MessagePack` NuGet package to `Frank.Cli.Core` and `Frank.Affordances`. If MemoryPack F# source generator support improves, can swap serializer without changing the architecture (both produce opaque binary blobs behind the same API boundary).

## R2: Unified AST Walk Strategy

**Decision**: Single-pass walk of both syntax AST and typed AST, dispatching to extraction helpers based on CE type.

**Rationale**:
- The existing `AstAnalyzer.walkExpr` (semantic) and `StatechartSourceExtractor.walkExprForStateful` (statechart) both walk `SynExpr` trees looking for CE invocations. They have identical traversal logic (Sequential, LetOrUse, App, Paren, Lambda, IfThenElse, Match, etc.) but different pattern matching for the CE they're looking for (`resource` vs `statefulResource`).
- A unified walker walks `SynExpr` once. When it encounters `SynExpr.App` with `SynExpr.Ident "resource"`, it dispatches to the semantic extraction helper. When it encounters `"statefulResource"`, it dispatches to both semantic AND statechart extraction helpers.
- The typed AST walk (`TypeAnalyzer.collectEntities` and `StatechartSourceExtractor.findMachineBindings`) both walk `FSharpEntity` trees. These can be merged into a single walk that collects both analyzed types and machine bindings.

**Key risk**: The existing extractors are tested independently. The unified walker must produce identical results for both semantic and statechart data. Mitigation: write comparison tests that run both old extractors and the new unified extractor against the same project and assert identical output.

**Alternatives considered**:
- Composition (A): Call both extractors independently, merge results. Rejected — preserves the split, walks AST twice, user explicitly said "B or stop."
- Shared walker with plugins (C): Premature abstraction for exactly two consumers.

## R3: Affordance Map Key Design

**Decision**: Composite key `(routeTemplate: string, stateKey: string)` serialized as a flat dictionary with string keys of the form `"{routeTemplate}|{stateKey}"`.

**Rationale**:
- Dictionary lookup by composite key is O(1) at request time
- String concatenation key avoids struct tuple hashing overhead and is trivially serializable in MessagePack
- The `|` separator is not valid in route templates or state DU case names
- For plain resources (no statechart), `stateKey` is `"*"` (wildcard)

**Entry fields**:
- `allowedMethods: string list` — HTTP methods available in this state
- `linkRelations: (string * string) list` — (relationType, targetHref) pairs, using IANA or ALPS-derived URIs
- `transitionTargets: (string * string) list` — (event, targetState) pairs, when extractable from the statechart

## R4: ALPS Profile Structure for Unified Resources

**Decision**: Single ALPS document per resource combining `semantic` descriptors (type properties) and transition descriptors (HTTP methods with safe/unsafe/idempotent classification).

**Rationale**:
- ALPS spec (Section 2.2.5) supports descriptor types: `semantic`, `safe`, `unsafe`, `idempotent`
- Type properties map to `semantic` descriptors with Schema.org alignment via `href`
- HTTP GET maps to `safe` descriptor
- HTTP POST/PUT/PATCH map to `unsafe` descriptor
- HTTP DELETE maps to `idempotent` descriptor
- Each transition descriptor includes `rt` (return type) linking to the resource's semantic descriptors
- The `id` of each transition descriptor becomes the fragment for ALPS-derived link relation URIs

**Structure sketch** (tic-tac-toe):
```json
{
  "alps": {
    "version": "1.0",
    "descriptor": [
      { "id": "board", "type": "semantic", "href": "https://schema.org/name" },
      { "id": "currentTurn", "type": "semantic" },
      { "id": "winner", "type": "semantic" },
      { "id": "gameState", "type": "safe", "rt": "#board #currentTurn #winner" },
      { "id": "makeMove", "type": "unsafe", "rt": "#board #currentTurn" }
    ]
  }
}
```

## R5: OpenAPI Consistency Comparison Strategy

**Decision**: Generate expected OpenAPI components from the unified model at CLI time, compare against actual OpenAPI document generated by `Frank.OpenApi` at runtime (captured via TestHost).

**Rationale**:
- `Frank.OpenApi` generates OpenAPI from endpoint metadata at runtime — it inspects `EndpointDataSource` and `EndpointMetadataCollection`
- The CLI's unified extraction analyzes the same source code at compile time
- To compare: the CLI test harness builds the app via TestHost, requests the OpenAPI document, and diffs it against the CLI-generated expected schema
- This is an integration test, not a static analysis — it validates that compile-time and runtime descriptions agree

**Alternative considered**: Static comparison of F# types against hand-written OpenAPI YAML. Rejected — too fragile, doesn't capture what `Frank.OpenApi` actually produces at runtime.
