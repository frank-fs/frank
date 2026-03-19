---
work_package_id: WP05
title: Unified ALPS Generation -- Type + Behavior Descriptors
lane: "done"
dependencies: [WP02]
base_branch: 031-unified-resource-pipeline-WP02
base_commit: 35175b217c2cce39589a312a0e8317e273430068
created_at: '2026-03-19T03:40:19.974323+00:00'
subtasks:
- T026
- T027
- T028
- T029
- T030
- T031
phase: Phase 1 - Core Pipeline
assignee: ''
agent: "claude-opus-wp05-review"
shell_pid: "24010"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-007
- FR-008
---

# Work Package Prompt: WP05 -- Unified ALPS Generation -- Type + Behavior Descriptors

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````python`, ````bash`

---

## Implementation Command

Depends on WP02:

```bash
spec-kitty implement WP05 --base WP02
```

---

## Objectives & Success Criteria

1. Create `UnifiedAlpsGenerator.fs` that takes a `UnifiedResource` and produces an ALPS JSON document combining **type descriptors** (`semantic`) and **behavioral transition descriptors** (`safe`/`unsafe`/`idempotent`).
2. Align type descriptors to Schema.org vocabulary using existing `VocabularyAligner` logic.
3. Derive link relation types from (state, method, baseUri) triples with IANA-registered relations taking precedence over ALPS profile fragment URIs.
4. Handle plain resources (no statechart) -- produce ALPS with type descriptors and method-based transitions only.
5. Validate generated ALPS round-trips through the existing `Alps.JsonParser.parseAlpsJson`.
6. Pass automated tests confirming that tic-tac-toe resource ALPS contains both semantic and transition descriptors.

**Success**: `UnifiedAlpsGenerator.generate` takes a `UnifiedResource` and `baseUri: string` and returns a valid ALPS JSON string containing both type structure and behavioral semantics.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` -- User Story 2 (FR-007, FR-008), Research R4 (ALPS Profile Structure)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` -- Project Structure (unified extraction in `Frank.Cli.Core/Unified/`)
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` -- `UnifiedResource`, `HttpCapability`, `DerivedResourceFields`
- **Research**: `kitty-specs/031-unified-resource-pipeline/research.md` -- R4 (ALPS structure sketch), R6 (FSharp.Data.JsonSchema as canonical type mapping)
- **Existing ALPS generators**: `src/Frank.Statecharts/Alps/JsonGenerator.fs` and `GeneratorCommon.fs` -- these generate ALPS from `StatechartDocument` (behavioral only). The unified generator produces ALPS from `UnifiedResource` which carries **both** type and behavioral data.
- **Existing VocabularyAligner**: `src/Frank.Cli.Core/Extraction/VocabularyAligner.fs` -- `tryFindAlignment` maps field names to Schema.org vocabulary URIs. Reuse the alignment map, not the RDF graph mutation.
- **Existing ALPS parser**: `src/Frank.Statecharts/Alps/JsonParser.fs` -- `parseAlpsJson` parses ALPS JSON into `Ast.ParseResult`. Generated ALPS must parse cleanly.
- **Principle VIII**: No duplicated logic. The unified generator is new code, not a copy of `Alps.JsonGenerator`. It produces ALPS from a different input type (`UnifiedResource` vs `StatechartDocument`). The existing generator remains for format conversion (parse smcat/WSD/SCXML, generate ALPS).

---

## Subtasks & Detailed Guidance

### Subtask T026 -- Create `UnifiedAlpsGenerator.fs`

- **Purpose**: Implement the core unified ALPS generation function that combines type descriptors and behavioral transition descriptors into a single ALPS JSON document.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Unified/UnifiedAlpsGenerator.fs`
  2. Module declaration: `module Frank.Cli.Core.Unified.UnifiedAlpsGenerator`
  3. Open `System.IO`, `System.Text`, `System.Text.Json` for `Utf8JsonWriter`
  4. Define the public function signature:
     ```fsharp
     let generate (resource: UnifiedResource) (baseUri: string) : string
     ```
  5. Structure the ALPS output per Research R4:
     - Root: `{ "alps": { "version": "1.0", "descriptor": [...] } }`
     - **Type descriptors first** (from `resource.TypeInfo`): For each `AnalyzedType`, emit a `"type": "semantic"` descriptor per field. For DU types, emit each case as a nested semantic descriptor with its fields.
     - **Transition descriptors second** (from `resource.HttpCapabilities`): For each `HttpCapability`, emit a descriptor with `"type"` set to `"safe"` (GET/HEAD/OPTIONS), `"unsafe"` (POST), or `"idempotent"` (PUT/DELETE/PATCH). Include `"rt"` linking back to the relevant type descriptors.
  6. Use `Utf8JsonWriter` with `JsonWriterOptions(Indented = true)` for consistent formatting (following existing `JsonGenerator.generateAlpsJson` pattern).
  7. Return the JSON string via `Encoding.UTF8.GetString(stream.ToArray())`.

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedAlpsGenerator.fs` (NEW, ~120-180 lines)
- **Notes**:
  - Each semantic descriptor `id` should be the field name (e.g., `"board"`, `"currentTurn"`, `"winner"`).
  - Each transition descriptor `id` should be a verb-form name derived from the HTTP method and resource slug (e.g., `"getGame"`, `"makeMove"`) or the statechart event name if available.
  - The `rt` (return type) field links to semantic descriptors using `#` fragment references (e.g., `"#board #currentTurn #winner"`).
  - For DU types (like `TicTacToeState`), emit the parent DU as a semantic descriptor with child case descriptors nested inside, each containing their case-specific fields.

### Subtask T027 -- Schema.org Vocabulary Alignment on Type Descriptors

- **Purpose**: Enrich type descriptors with Schema.org `href` references where field names match known vocabulary terms.
- **Steps**:
  1. Extract the alignment map logic from `VocabularyAligner.fs` -- specifically the `alignmentMap` and `tryFindAlignment` function. These are the pure function parts that map field names to Schema.org URIs.
  2. In `UnifiedAlpsGenerator.fs`, when emitting a semantic descriptor for a field:
     - Call `tryFindAlignment field.Name`
     - If a Schema.org URI is found, add `"href": "<schema.org URI>"` to the descriptor
     - Example: field `"name"` gets `"href": "https://schema.org/name"`
  3. The alignment is best-effort -- fields without matches get no `href` attribute.

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedAlpsGenerator.fs` (extends T026 code)
- **Notes**:
  - The existing `VocabularyAligner.alignVocabularies` mutates an RDF graph. Do NOT call that function. Instead, reuse the `alignmentMap` data and `tryFindAlignment` logic directly. If `tryFindAlignment` is currently private, either make it internal or extract the alignment map to a shared location.
  - Consider adding a `VocabularyAlignment` module in `src/Frank.Cli.Core/Extraction/` that exposes `tryFindAlignment` as `internal` so both `VocabularyAligner` and `UnifiedAlpsGenerator` can use it without duplication.
  - The normalized field name matching (camelCase splitting, lowercasing) must be identical to the existing implementation.

### Subtask T028 -- IANA-Precedence Link Relation Derivation

- **Purpose**: Map (state, method, baseUri) triples to link relation type URIs, preferring IANA-registered relations before falling back to ALPS profile fragment URIs.
- **Steps**:
  1. Define IANA relation mapping in a private function:
     ```fsharp
     let private ianaRelationForMethod (method: string) (isSingleResource: bool) : string option =
         match method.ToUpperInvariant() with
         | "GET" when isSingleResource -> Some "self"
         | "GET" -> Some "collection"
         | "POST" -> Some "create" // Note: not IANA registered, but commonly used
         | "PUT" -> Some "edit"
         | "DELETE" -> Some "delete" // Note: not IANA registered
         | "PATCH" -> Some "edit"
         | _ -> None
     ```
  2. For each `HttpCapability`, derive the relation type:
     - First, check `ianaRelationForMethod`. If found, use the IANA relation name.
     - If no IANA match, construct an ALPS profile fragment URI: `"{baseUri}/{resourceSlug}#{descriptorId}"` where `descriptorId` is the transition descriptor's `id`.
  3. Store the derived relation type in `HttpCapability.LinkRelation` (already defined in the data model).
  4. The relation derivation should be a pure function testable in isolation:
     ```fsharp
     let deriveRelationType (baseUri: string) (resourceSlug: string) (method: string) (stateKey: string option) : string
     ```

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedAlpsGenerator.fs` (extends T026 code)
- **Parallel?**: Can be developed alongside T027 (vocabulary alignment).
- **Notes**:
  - IANA-registered link relations: see https://www.iana.org/assignments/link-relations/link-relations.xhtml
  - Only `self`, `edit`, `collection` are clearly IANA-registered from the common HTTP methods. `create` and `delete` are NOT IANA-registered -- for these, use ALPS fragment URIs.
  - The `isSingleResource` parameter is determined by whether the route template contains a parameter (e.g., `/games/{gameId}` is single, `/games` is collection).
  - For state-dependent transitions, include the state name in the descriptor `id` to differentiate: e.g., `#xTurn-makeMove` vs `#won-getResult`.

### Subtask T029 -- Handle Plain Resources (No Statechart)

- **Purpose**: Generate valid ALPS for resources that use `resource` CE (not `statefulResource`) -- type descriptors and method-based transitions only, no state-dependent behavior.
- **Steps**:
  1. Check `resource.Statechart` -- if `None`, generate ALPS without:
     - State-scoped transition descriptors (no `stateKey` grouping)
     - No state DU case descriptors
  2. For plain resources, emit:
     - Semantic descriptors for all fields of the resource's type
     - Transition descriptors for each HTTP method the resource supports (from `resource.HttpCapabilities` where `StateKey = None`)
  3. The transition descriptors use the same IANA-precedence relation derivation (T028) but without state qualification.
  4. Example for a `resource "/health"` with GET only:
     ```json
     {
       "alps": {
         "version": "1.0",
         "descriptor": [
           { "id": "status", "type": "semantic" },
           { "id": "getHealth", "type": "safe", "rt": "#status" }
         ]
       }
     }
     ```

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedAlpsGenerator.fs` (conditional logic in T026's `generate` function)
- **Notes**:
  - This is the simpler code path in the generator -- no state grouping, no transition targets. Test it first as a stepping stone to the stateful path.
  - A plain resource with zero type information (only route + methods) should produce a valid but minimal ALPS document with just transition descriptors and no semantic descriptors.

### Subtask T030 -- Validate ALPS Round-Trip Through Parser

- **Purpose**: Ensure the generated ALPS JSON is structurally valid by parsing it with the existing ALPS JSON parser.
- **Steps**:
  1. After generating ALPS JSON, pass it through `Frank.Statecharts.Alps.JsonParser.parseAlpsJson`.
  2. Assert that `parseResult.Errors` is empty.
  3. This serves as a structural validation -- the parser checks for:
     - Valid JSON structure
     - Presence of `alps` root object
     - Valid descriptor structure (id, type, href, rt, etc.)
  4. Add this as a validation step in the generation pipeline, not just in tests. The `generate` function should return `Result<string, string list>` or at minimum log warnings if the round-trip produces parse errors.

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedAlpsGenerator.fs` (add round-trip check)
- **Notes**:
  - The existing `parseAlpsJson` function classifies descriptors into `StatechartDocument` elements. The unified ALPS includes semantic descriptors that aren't state/transition elements -- these will appear as unclassified or annotation elements. Verify the parser doesn't reject them.
  - If the parser does reject non-statechart descriptors, that's a finding to document. The round-trip check should verify zero errors, but warnings about unclassified descriptors are acceptable.
  - Consider whether the return type should be `Result<string, string list>` (where errors are parse failures) or just `string` with a separate validation function. The caller (generate command) will need to decide whether parse warnings are blockers.

### Subtask T031 -- Tests: Tic-Tac-Toe ALPS Generation

- **Purpose**: Write tests verifying that ALPS generation for a tic-tac-toe-like `UnifiedResource` produces a document with both semantic and transition descriptors.
- **Steps**:
  1. Create test file in `test/Frank.Cli.Core.Tests/` or `test/Frank.Affordances.Tests/` (whichever houses unified pipeline tests from WP02).
  2. Construct a `UnifiedResource` fixture representing the tic-tac-toe resource:
     ```fsharp
     let ticTacToeResource : UnifiedResource =
         { RouteTemplate = "/games/{gameId}"
           ResourceSlug = "games"
           TypeInfo = [
               { FullName = "TicTacToe.TicTacToeState"
                 ShortName = "TicTacToeState"
                 Kind = DiscriminatedUnion [
                     { Name = "XTurn"; Fields = [{ Name = "board"; Kind = Reference "Board"; ... }] }
                     { Name = "OTurn"; Fields = [{ Name = "board"; Kind = Reference "Board"; ... }] }
                     { Name = "Won"; Fields = [{ Name = "winner"; Kind = Primitive "xsd:string"; ... }] }
                     { Name = "Draw"; Fields = [{ Name = "board"; Kind = Reference "Board"; ... }] }
                 ]
                 ... }
           ]
           Statechart = Some { ... } // XTurn->OTurn->Won/Draw
           HttpCapabilities = [
               { Method = "GET"; StateKey = Some "XTurn"; LinkRelation = "self"; IsSafe = true }
               { Method = "POST"; StateKey = Some "XTurn"; LinkRelation = "...#makeMove"; IsSafe = false }
               { Method = "GET"; StateKey = Some "Won"; LinkRelation = "self"; IsSafe = true }
               // etc.
           ]
           DerivedFields = { OrphanStates = []; UnhandledCases = []; ... }
         }
     ```
  3. Call `UnifiedAlpsGenerator.generate ticTacToeResource "https://example.com/alps"`.
  4. Assert the result:
     - Contains `"type": "semantic"` descriptors for `board`, `currentTurn`, `winner`
     - Contains `"type": "safe"` descriptor for GET
     - Contains `"type": "unsafe"` descriptor for POST/move
     - Parses without errors via `Alps.JsonParser.parseAlpsJson`
  5. Also test the plain resource case:
     - Construct a `UnifiedResource` with `Statechart = None` and a single GET capability
     - Assert ALPS contains semantic descriptors and one safe transition descriptor
     - Assert no state-dependent transition descriptors appear

- **Files**: Test file in the unified pipeline test project (location determined by WP02's test structure)
- **Notes**:
  - Use `Expecto` test framework (project standard).
  - The fixture construction may be verbose -- consider a helper module for building test `UnifiedResource` values.
  - Parse the generated JSON with `System.Text.Json.JsonDocument` to assert specific properties, in addition to the round-trip parser check.

---

## Test Strategy

- **Unit tests**: Test `deriveRelationType` in isolation with various (method, isSingleResource) combinations.
- **Integration tests**: Generate ALPS for fixture resources, parse with `JsonDocument` for structural assertions, parse with `parseAlpsJson` for round-trip validation.
- **Regression**: The existing ALPS generators (`Alps.JsonGenerator`, `Alps.XmlGenerator`) must continue to pass their existing tests unchanged -- this WP adds a new generator, it does not modify existing ones.

Run tests:
```bash
dotnet test test/Frank.Cli.Core.Tests/ --filter "UnifiedAlps"
```

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| `parseAlpsJson` rejects non-statechart semantic descriptors | Verify parser behavior in T030. If it rejects, the round-trip check should assert zero *errors* but allow *warnings*. File a follow-up issue if parser needs extension. |
| Schema.org alignment map is private in `VocabularyAligner.fs` | Extract to shared internal module (T027 step 2). Keep the change minimal -- extract, don't refactor. |
| Transition descriptor `id` naming collisions across states | Include state name prefix in descriptor id for state-dependent transitions: `"{stateName}-{method}"` |
| Large number of type fields produces very long `rt` values | Limit `rt` to top-level semantic descriptor ids (not nested DU case fields). This matches ALPS convention. |

---

## Review Guidance

- Verify generated ALPS JSON is valid JSON and follows ALPS 1.0 structure.
- Verify both `semantic` and `safe`/`unsafe`/`idempotent` descriptor types appear in the output.
- Verify Schema.org `href` attributes appear on fields that match the alignment map (e.g., `name`, `description`, `email`).
- Verify IANA relation precedence: `self` for GET on single resources, ALPS fragment URI for domain-specific transitions.
- Verify plain resource path produces valid ALPS without state-dependent descriptors.
- Verify round-trip through `parseAlpsJson` produces zero errors.
- Verify `dotnet build` and `dotnet test` pass cleanly.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

### How to Add Activity Log Entries

**When adding an entry**:
1. Scroll to the bottom of this file (Activity Log section below "Valid lanes")
2. **APPEND the new entry at the END** (do NOT prepend or insert in middle)
3. Use exact format: `- YYYY-MM-DDTHH:MM:SSZ -- agent_id -- lane=<lane> -- <action>`
4. Timestamp MUST be current time in UTC (check with `date -u "+%Y-%m-%dT%H:%M:%SZ"`)
5. Lane MUST match the frontmatter `lane:` field exactly
6. Agent ID should identify who made the change (claude-sonnet-4-5, codex, etc.)

**Format**:
```
- YYYY-MM-DDTHH:MM:SSZ -- <agent_id> -- lane=<lane> -- <brief action description>
```

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

**Initial entry**:
- 2026-03-19T02:15:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-19T03:40:20Z – claude-opus-wp05 – shell_pid=20809 – lane=doing – Assigned agent via workflow command
- 2026-03-19T03:50:14Z – claude-opus-wp05 – shell_pid=20809 – lane=for_review – Implemented UnifiedAlpsGenerator with all 6 subtasks: T026 core generation, T027 Schema.org vocabulary alignment, T028 IANA-precedence link relations, T029 plain resource support, T030 round-trip validation, T031 23 passing tests including tic-tac-toe fixture.
- 2026-03-19T03:52:45Z – claude-opus-wp05-review – shell_pid=24010 – lane=doing – Started review via workflow command
- 2026-03-19T03:59:35Z – claude-opus-wp05-review – shell_pid=24010 – lane=done – Review passed: 23/23 tests, ALPS with type+behavioral descriptors, Schema.org alignment, IANA link relations, round-trip validation
